using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Paranoidal
{
    sealed class GitResult
    {
        public int Code;
        public byte[] Out = new byte[0];
        public string Err = "";
        public string Text => Encoding.UTF8.GetString(Out).Trim();

        public string LastErrorLine()
        {
            var lines = Err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var l = lines[i].Trim();
                if (l.StartsWith("fatal:") || l.StartsWith("error:")) return l;
            }
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "код " + Code;
        }
    }

    /// <summary>Lets the caller kill one running git process (used by the download watchdog).</summary>
    sealed class RunHandle
    {
        volatile Process proc;
        public volatile bool Killed;

        internal void Attach(Process p) { proc = p; if (Killed) KillTree(p); }

        public void Kill()
        {
            Killed = true;
            var p = proc;
            if (p != null) KillTree(p);
        }

        internal static void KillTree(Process p)
        {
            // git.exe starts git-remote-https and its helpers: the whole tree goes
            try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); }
            catch { }
        }
    }

    /// <summary>Runs the bundled git with an isolated environment (no user/system config, no prompts, no lazy fetch).</summary>
    sealed class GitRunner
    {
        public readonly string Exe;
        readonly string globalConfig;
        public string GitDir;
        public string WorkTree;
        public readonly Dictionary<string, string> Env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        readonly object gate = new object();
        readonly HashSet<Process> running = new HashSet<Process>();
        volatile bool cancelled;
        volatile CancellationTokenSource cts = new CancellationTokenSource();

        public GitRunner(string exe, string globalConfig)
        {
            Exe = exe;
            this.globalConfig = globalConfig;
        }

        public bool Cancelled => cancelled;
        /// <summary>Cancelled by Cancel(): waits and network requests of the operation end with it.</summary>
        public CancellationToken Token => cts.Token;

        public void ResetCancel()
        {
            cancelled = false;
            if (cts.IsCancellationRequested) cts = new CancellationTokenSource();
        }

        public void ThrowIfCancelled() { if (cancelled) throw new OperationCanceledException(); }

        /// <summary>A pause that ends early with OperationCanceledException when the operation is cancelled.</summary>
        public Task DelayAsync(int ms) => Task.Delay(ms, Token);

        public void Cancel()
        {
            cancelled = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            List<Process> list;
            lock (gate) list = new List<Process>(running);
            foreach (var p in list) RunHandle.KillTree(p);
        }

        public async Task<GitResult> RunAsync(string[] args, byte[] stdin = null, Action<string> onErrLine = null,
            IDictionary<string, string> extraEnv = null, RunHandle handle = null)
        {
            ThrowIfCancelled();
            var cmd = new StringBuilder();
            foreach (var a in args) { if (cmd.Length > 0) cmd.Append(' '); cmd.Append(FsUtil.QuoteArg(a)); }

            var psi = new ProcessStartInfo(Exe, cmd.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = WorkTree != null && Directory.Exists(WorkTree) ? WorkTree : Path.GetTempPath(),
            };
            var env = psi.EnvironmentVariables;
            var drop = new List<string>();
            foreach (string k in env.Keys) if (k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)) drop.Add(k);
            foreach (var k in drop) env.Remove(k);
            env["GIT_CONFIG_NOSYSTEM"] = "1";
            env["GIT_CONFIG_GLOBAL"] = globalConfig;
            env["GIT_TERMINAL_PROMPT"] = "0";
            env["GCM_INTERACTIVE"] = "never";
            env["GIT_NO_LAZY_FETCH"] = "1";
            env["GIT_CONFIG_COUNT"] = "1";
            env["GIT_CONFIG_KEY_0"] = "safe.directory";
            env["GIT_CONFIG_VALUE_0"] = "*";
            if (AppInfo.TestOffline)
            {
                // tests: no connection (a proxy that refuses), without touching the network settings of the computer
                env["GIT_CONFIG_COUNT"] = "2";
                env["GIT_CONFIG_KEY_1"] = "http.proxy";
                env["GIT_CONFIG_VALUE_1"] = "http://127.0.0.1:9";
            }
            if (GitDir != null)
            {
                env["GIT_DIR"] = GitDir;
                env["GIT_WORK_TREE"] = WorkTree;
            }
            lock (gate) foreach (var kv in Env) env[kv.Key] = kv.Value;
            if (extraEnv != null) foreach (var kv in extraEnv) env[kv.Key] = kv.Value;

            var p = new Process { StartInfo = psi };
            p.Start();
            lock (gate) running.Add(p);
            if (cancelled) RunHandle.KillTree(p);
            if (handle != null) handle.Attach(p);

            var outMs = new MemoryStream();
            var tOut = p.StandardOutput.BaseStream.CopyToAsync(outMs);
            var err = new StringBuilder();
            var tErr = ReadErrAsync(p.StandardError, err, onErrLine);

            try
            {
                if (stdin != null)
                {
                    await p.StandardInput.BaseStream.WriteAsync(stdin, 0, stdin.Length).ConfigureAwait(false);
                    await p.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
                }
                p.StandardInput.Close();
            }
            catch (IOException) { }

            // a cancel kills the process: this wait ends with it
            await p.WaitForExitAsync().ConfigureAwait(false);
            try { await tOut.ConfigureAwait(false); } catch { }
            await tErr.ConfigureAwait(false);
            lock (gate) running.Remove(p);
            var res = new GitResult { Code = p.ExitCode, Out = outMs.ToArray(), Err = err.ToString() };
            p.Dispose();
            ThrowIfCancelled();
            return res;
        }

        /// <summary>stderr line by line (git ends progress lines with \r): kept for the error text, each line to onErrLine.</summary>
        static async Task ReadErrAsync(StreamReader r, StringBuilder err, Action<string> onErrLine)
        {
            var sb = new StringBuilder();
            void Flush()
            {
                if (sb.Length == 0) return;
                var line = sb.ToString(); sb.Clear();
                lock (err)
                {
                    err.AppendLine(line);
                    if (err.Length > 40000) err.Remove(0, err.Length - 30000);
                }
                if (onErrLine != null) try { onErrLine(line); } catch { }
            }
            try
            {
                var buf = new char[4096];
                int n;
                while ((n = await r.ReadAsync(buf.AsMemory()).ConfigureAwait(false)) > 0)
                    for (int i = 0; i < n; i++)
                    {
                        char c = buf[i];
                        if (c == '\r' || c == '\n') Flush(); else sb.Append(c);
                    }
                Flush();
            }
            catch { }
        }

        public async Task<GitResult> CheckAsync(string what, params string[] args)
        {
            var r = await RunAsync(args).ConfigureAwait(false);
            if (r.Code != 0) throw new SyncException(what + ": " + r.LastErrorLine());
            return r;
        }
    }

    /// <summary>Parses git progress lines like "Receiving objects:  45% (450/1000), 12.34 MiB | 1.20 MiB/s".</summary>
    sealed class GitProgress
    {
        public string Phase;
        public int Percent, Done, Total;
        public long Bytes = -1;

        static readonly Regex Rx = new Regex(
            @"^(?:remote:\s*)?(?<ph>[A-Za-z][A-Za-z ]*?):\s+(?<pct>\d+)%\s+\((?<d>\d+)/(?<t>\d+)\)(?:,\s*(?<b>[\d.]+)\s*(?<u>bytes|KiB|MiB|GiB))?",
            RegexOptions.Compiled);

        public static GitProgress Parse(string line)
        {
            var m = Rx.Match(line);
            if (!m.Success) return null;
            var p = new GitProgress
            {
                Phase = m.Groups["ph"].Value,
                Percent = int.Parse(m.Groups["pct"].Value, CultureInfo.InvariantCulture),
                Done = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture),
                Total = int.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture),
            };
            double b;
            if (m.Groups["b"].Success && double.TryParse(m.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
            {
                switch (m.Groups["u"].Value)
                {
                    case "KiB": b *= 1024; break;
                    case "MiB": b *= 1024 * 1024; break;
                    case "GiB": b *= 1024.0 * 1024 * 1024; break;
                }
                p.Bytes = (long)b;
            }
            return p;
        }
    }

    /// <summary>Extracts the embedded MinGit once into AppInfo.DataDir (Windows); elsewhere the system git.</summary>
    static class GitBootstrap
    {
        public static string Ensure(Action<string, string> stage)
        {
            if (!OperatingSystem.IsWindows()) return SystemGit();
            var dir = Path.Combine(AppInfo.DataDir, "mingit-" + AppInfo.MinGitVersion);
            var exe = Path.Combine(dir, "cmd", "git.exe");
            var marker = Path.Combine(dir, ".extracted");
            if (File.Exists(exe) && File.Exists(marker)) return exe;

            stage("Подготовка встроенного git (только при первом запуске)…", "Preparing the built-in git (first start only)…");
            Directory.CreateDirectory(AppInfo.DataDir);
            var tmp = dir + ".tmp" + Process.GetCurrentProcess().Id;
            if (Directory.Exists(tmp)) FsUtil.DeleteDir(tmp);
            using (var s = Res.MinGitZip())
            {
                if (s == null) throw new SyncException("Встроенный git не найден внутри программы.", "The built-in git is missing from the launcher.");
                using (var z = new ZipArchive(s, ZipArchiveMode.Read)) z.ExtractToDirectory(tmp);
            }
            File.WriteAllText(Path.Combine(tmp, ".extracted"), AppInfo.MinGitVersion);
            if (Directory.Exists(dir)) FsUtil.DeleteDir(dir);
            Directory.Move(tmp, dir);
            return exe;
        }

        /// <summary>git from PATH (Linux, macOS): MinGit is a Windows build.</summary>
        static string SystemGit()
        {
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (d.Length == 0) continue;
                try
                {
                    var exe = Path.Combine(d, "git");
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }
            throw new SyncException("Не найден git. Установите пакет git из репозитория вашей системы и запустите лаунчер снова.",
                "git was not found. Install the git package of your system and start the launcher again.");
        }
    }
}
