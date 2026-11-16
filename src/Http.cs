using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Paranoidal
{
    /// <summary>An HTTP answer that is not a success, or no answer at all (Status null). Message is Russian.</summary>
    sealed class HttpFailure : Exception
    {
        public readonly int? Status;
        public HttpFailure(string message, int? status = null) : base(message) { Status = status; }
        public bool Limited => Status == 403 || Status == 429;
    }

    /// <summary>
    /// The one HttpClient of the launcher: anonymous GET requests to GitHub (API, raw files, release downloads), the system
    /// proxy, gzip. Every call has its own time limit; the caller's token cancels it (OperationCanceledException).
    /// </summary>
    static class Http
    {
        static readonly HttpClient Client = Create();

        static HttpClient Create()
        {
            var h = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            var c = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ParanoidalLauncher/" + AppInfo.Version);
            return c;
        }

        /// <summary>
        /// Sends a GET and returns the response once its headers are here (the body is read by the caller under `limit`).
        /// A status other than 2xx and a failed or timed out connection throw HttpFailure.
        /// </summary>
        public static async Task<HttpResponseMessage> GetAsync(string url, string accept, CancellationTokenSource limit, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept != null) req.Headers.Accept.ParseAdd(accept);
            HttpResponseMessage resp;
            try { resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false); }
            catch (Exception ex) { throw Translate(ex, ct); }
            if (resp.IsSuccessStatusCode) return resp;
            int code = (int)resp.StatusCode;
            resp.Dispose();
            throw new HttpFailure("HTTP " + code, code);
        }

        /// <summary>The exception of a failed request: the caller's cancel stays a cancel, the time limit becomes HttpFailure.</summary>
        public static Exception Translate(Exception ex, CancellationToken ct)
        {
            if (ex is HttpFailure) return ex;
            if (ct.IsCancellationRequested) return new OperationCanceledException(ct);
            if (ex is OperationCanceledException) return new HttpFailure("нет ответа, время ожидания истекло");
            var inner = ex is HttpRequestException && ex.InnerException != null ? ex.InnerException : ex;
            return new HttpFailure(inner.Message);
        }

        /// <summary>A time limit for one request that also ends with the caller's token.</summary>
        public static CancellationTokenSource Limit(int timeoutMs, CancellationToken ct)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            return cts;
        }

        /// <summary>The body as UTF-8 text, at most maxChars (never read an endless answer).</summary>
        public static async Task<string> GetTextAsync(string url, int timeoutMs, int maxChars, string accept = null, CancellationToken ct = default)
        {
            using (var limit = Limit(timeoutMs, ct))
            using (var resp = await GetAsync(url, accept, limit, ct).ConfigureAwait(false))
            {
                try
                {
                    using (var s = await resp.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false))
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        var buf = new char[maxChars];
                        int n = await r.ReadBlockAsync(buf.AsMemory(), limit.Token).ConfigureAwait(false);
                        return new string(buf, 0, n);
                    }
                }
                catch (Exception ex) { throw Translate(ex, ct); }
            }
        }

        /// <summary>The whole body as a stream, read under the time limit (for JSON answers).</summary>
        public static async Task<T> GetAsync<T>(string url, int timeoutMs, string accept, Func<Stream, CancellationToken, Task<T>> read, CancellationToken ct = default)
        {
            using (var limit = Limit(timeoutMs, ct))
            using (var resp = await GetAsync(url, accept, limit, ct).ConfigureAwait(false))
            {
                try
                {
                    using (var s = await resp.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false))
                        return await read(s, limit.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is System.Text.Json.JsonException)) { throw Translate(ex, ct); }
            }
        }
    }
}
