using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Paranoidal
{
    sealed class CommitInfo
    {
        public string Sha, Message, Author;
        public DateTime Date;
    }

    /// <summary>Read-only GitHub REST calls for the change list (anonymous, 60 requests/hour is plenty).</summary>
    static class GitHubApi
    {
        const string Api = "https://api.github.com/repos/" + AppInfo.Owner + "/" + AppInfo.RepoName;

        // the parts of the REST answers the launcher reads (camelCase names, unknown fields ignored)
        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        sealed class ApiCommit { public string Sha { get; set; } public ApiCommitBody Commit { get; set; } }
        sealed class ApiCommitBody { public string Message { get; set; } public ApiAuthor Author { get; set; } }
        sealed class ApiAuthor { public string Name { get; set; } public DateTime? Date { get; set; } }
        sealed class ApiCompare { public string Status { get; set; } public List<ApiCommit> Commits { get; set; } }
        sealed class ApiTree { public List<ApiTreeItem> Tree { get; set; } }
        sealed class ApiTreeItem { public string Type { get; set; } public string Sha { get; set; } public long? Size { get; set; } }

        static async Task<T> GetAsync<T>(string url, CancellationToken ct, int timeoutMs = 20000)
        {
            if (AppInfo.TestOffline) throw new Exception("нет связи (PARANOIDAL_LAUNCHER_TEST_OFFLINE)");
            try
            {
                return await Http.GetAsync(url, timeoutMs, "application/vnd.github+json",
                    (s, t) => JsonSerializer.DeserializeAsync<T>(s, JsonOptions, t).AsTask(), ct).ConfigureAwait(false);
            }
            catch (HttpFailure ex) when (ex.Limited) { throw new Exception("GitHub временно ограничил запросы, попробуйте позже"); }
        }

        public static async Task<List<CommitInfo>> CompareAsync(string baseSha, string headSha, CancellationToken ct = default)
        {
            var d = await GetAsync<ApiCompare>(Api + "/compare/" + baseSha + "..." + headSha + "?per_page=250", ct).ConfigureAwait(false);
            var list = Parse(d == null ? null : d.Commits);
            list.Reverse();   // newest first
            return list;
        }

        /// <summary>Blob id -> size for every file in mods/ of a commit (one request), used to size download batches.</summary>
        public static async Task<Dictionary<string, long>> BlobSizesAsync(string commit, CancellationToken ct = default)
        {
            var d = await GetAsync<ApiTree>(Api + "/git/trees/" + commit + ":mods?recursive=1", ct, 90000).ConfigureAwait(false);
            var res = new Dictionary<string, long>();
            if (d == null || d.Tree == null) return res;
            foreach (var e in d.Tree)
                if (e != null && e.Type == "blob" && e.Sha != null && e.Size != null) res[e.Sha] = e.Size.Value;
            return res;
        }

        /// <summary>A commit by its full or abbreviated id (the host's version line has 10 characters).</summary>
        public static async Task<CommitInfo> CommitAsync(string sha, CancellationToken ct = default)
        {
            var list = Parse(new List<ApiCommit> { await GetAsync<ApiCommit>(Api + "/commits/" + Uri.EscapeDataString(sha), ct).ConfigureAwait(false) });
            return list.Count == 1 && list[0].Sha != null ? list[0] : null;
        }

        /// <summary>
        /// The commit is in the history of the branch (the branch head or an older version of it); false also when GitHub
        /// does not know one of them. Tells a bare commit id of the main 2.1 build from one of the Space Age build.
        /// </summary>
        public static async Task<bool> InBranchAsync(string branch, string sha, CancellationToken ct = default)
        {
            try
            {
                var d = await GetAsync<ApiCompare>(Api + "/compare/" + Uri.EscapeDataString(branch) + "..." + Uri.EscapeDataString(sha) + "?per_page=1", ct).ConfigureAwait(false);
                var status = d == null ? null : d.Status;
                return status == "behind" || status == "identical";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return false; }
        }

        public static async Task<List<CommitInfo>> RecentAsync(string branch, int count, CancellationToken ct = default)
        {
            return Parse(await GetAsync<List<ApiCommit>>(Api + "/commits?sha=" + Uri.EscapeDataString(branch) + "&per_page=" + count, ct).ConfigureAwait(false));
        }

        static List<CommitInfo> Parse(List<ApiCommit> items)
        {
            var list = new List<CommitInfo>();
            if (items == null) return list;
            foreach (var c in items)
            {
                if (c == null) continue;
                var ci = new CommitInfo { Sha = c.Sha };
                var msg = (c.Commit == null ? null : c.Commit.Message) ?? "";
                int nl = msg.IndexOf('\n');
                ci.Message = (nl >= 0 ? msg.Substring(0, nl) : msg).Trim();
                var author = c.Commit == null ? null : c.Commit.Author;
                if (author != null)
                {
                    ci.Author = author.Name;
                    if (author.Date != null) ci.Date = author.Date.Value.ToLocalTime();
                }
                list.Add(ci);
            }
            return list;
        }
    }

    /// <summary>
    /// zzzparanoidal/info.json of a build version on GitHub (a branch or a commit): which Factorio it is for and which
    /// game version it needs. One small file from raw.githubusercontent.com: not a REST call, the API limit is not spent.
    /// </summary>
    static class RemoteBuild
    {
        public static string Url(string reference) =>
            "https://raw.githubusercontent.com/" + AppInfo.Owner + "/" + AppInfo.RepoName + "/" + Uri.EscapeDataString(reference) + "/mods/zzzparanoidal/info.json";

        /// <summary>Null without a connection or when the file is not there; never throws.</summary>
        public static async Task<BuildFound> InfoAsync(string reference, int timeoutMs = 10000, CancellationToken ct = default)
        {
            if (AppInfo.TestOffline || string.IsNullOrEmpty(reference)) return null;
            try
            {
                // info.json is ~1 KB; never read an endless answer
                var text = await Http.GetTextAsync(Url(reference), timeoutMs, 64 * 1024, null, ct).ConfigureAwait(false);
                var f = new BuildFound { Present = true };
                BuildFound.Parse(text, f);
                return f.FactorioVersion == null ? null : f;
            }
            catch { return null; }
        }
    }

    /// <summary>The last list of build changes, shown when GitHub cannot be reached.</summary>
    static class CommitsCache
    {
        static string FilePath => Path.Combine(AppInfo.DataDir, Lines.Key("commits-cache") + ".txt");

        public static void Save(List<CommitInfo> list)
        {
            if (list == null || list.Count == 0) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var c in list)
                    sb.Append(c.Sha).Append('\t').Append(c.Date == default(DateTime) ? 0 : new DateTimeOffset(c.Date).ToUnixTimeSeconds())
                      .Append('\t').Append((c.Message ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
                Directory.CreateDirectory(AppInfo.DataDir);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static List<CommitInfo> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var list = new List<CommitInfo>();
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var f = line.Split(new[] { '\t' }, 3);
                    long t;
                    if (f.Length < 3 || f[0].Length != 40 || !long.TryParse(f[1], out t)) continue;
                    list.Add(new CommitInfo { Sha = f[0], Message = f[2], Date = t == 0 ? default(DateTime) : DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime });
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }
    }
}
