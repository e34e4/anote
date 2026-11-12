// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Webkit;
using Java.Net;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using File = System.IO.File;
using HttpURLConnection = Java.Net.HttpURLConnection;
using AWebView = Android.Webkit.WebView;
using ACookieManager = Android.Webkit.CookieManager;
using OperationCanceledException = System.OperationCanceledException;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Downloads media with yt-dlp via the Android Python runtime (youtubedl-android libs).
    /// Official Linux yt-dlp ELFs are not used — they fail on Android with e_type / glibc errors.
    /// </summary>
    internal sealed class YtDlpDownloadService : IYtDlpDownloadService
    {
        private readonly IMediaToolsService _mediaTools;
        private readonly IAppContextService _appContext;
        private readonly ISettingsService _settingsService;

        public YtDlpDownloadService(
            IMediaToolsService mediaTools,
            IAppContextService appContext,
            ISettingsService settingsService = null)
        {
            _mediaTools = mediaTools ?? throw new ArgumentNullException(nameof(mediaTools));
            _appContext = appContext ?? throw new ArgumentNullException(nameof(appContext));
            _settingsService = settingsService;
        }

        /// <inheritdoc/>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    global::Android.Content.Context context = _appContext.RootActivity ?? _appContext.Context;
                    if (context == null || !AnoteYtDlpRuntime.IsPythonAvailable(context))
                        return false;
                    _mediaTools.EnsureReady();
                    string script = ResolveYtDlpScript();
                    return !string.IsNullOrEmpty(script)
                        && File.Exists(script)
                        && !AnoteYtDlpRuntime.LooksLikeElf(script);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<YtDlpDownloadResult> DownloadAsync(
            string url,
            YtDlpQualityChoice choice,
            IProgress<YtDlpDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Fail("URL is empty");
            if (choice == null)
                return Fail("Quality choice is required");

            global::Android.Content.Context context = _appContext.RootActivity ?? _appContext.Context;
            if (context == null)
                return Fail("No Android context for yt-dlp");

            if (!AnoteYtDlpRuntime.IsPythonAvailable(context))
            {
                return Fail(
                    "Android Python runtime missing. Rebuild/install an APK that includes youtubedl-android.");
            }

            try
            {
                await _mediaTools.EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "EnsureInstalled failed: " + ex);
                return Fail("Could not prepare yt-dlp: " + ex.Message);
            }

            // Replace any leftover Linux ELF download with the Python script.
            string script = ResolveYtDlpScript();
            if (string.IsNullOrEmpty(script) || !File.Exists(script) || AnoteYtDlpRuntime.LooksLikeElf(script))
            {
                AnoteFileLog.Append("ytdlp", "Refreshing yt-dlp Python script (missing or Linux ELF).");
                bool ok = await _mediaTools.UpdateYtDlpAsync(cancellationToken).ConfigureAwait(false);
                script = ResolveYtDlpScript();
                if (!ok || string.IsNullOrEmpty(script) || !File.Exists(script) || AnoteYtDlpRuntime.LooksLikeElf(script))
                    return Fail("yt-dlp script not ready. Use Settings → Update yt-dlp.");
            }

            AnotePaths.EnsureDirectories();
            string outDir = AnotePaths.Down;
            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "Create down dir failed: " + ex);
                return Fail("Cannot write to " + outDir + ": " + ex.Message);
            }

            string ffmpeg = AnoteYtDlpRuntime.ResolvePackagedFfmpeg(context)
                ?? ResolveFfmpegPath();
            string originalUrl = url.Trim();
            string downloadUrl = YtDlpUrlHelper.NormalizeDownloadUrl(originalUrl);
            bool vkResolved = false;

            if (YtDlpUrlHelper.IsVkUrl(originalUrl))
            {
                progress?.Report(new YtDlpDownloadProgress
                {
                    Percent = 0,
                    StatusText = "Resolving VK Video via vkvideo.ru…",
                });

                // Prefer Android HttpURLConnection (matches curl on-device); HttpClient often fails here.
                string direct = await Task.Run(
                    () => ResolveVkDirectViaJava(originalUrl),
                    cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(direct))
                {
                    direct = await YtDlpUrlHelper.TryResolveVkDirectMediaUrlAsync(originalUrl, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(direct))
                {
                    AnoteFileLog.Append("ytdlp", "VK resolved direct media (bypass sinkholed vk.com): " + Truncate(direct, 160));
                    downloadUrl = direct;
                    vkResolved = true;
                }
                else
                {
                    AnoteFileLog.Append("ytdlp", "VK direct resolve failed; falling back to yt-dlp extractor for " + originalUrl);
                }
            }
            else if (YtDlpUrlHelper.IsRedditUrl(originalUrl))
            {
                progress?.Report(new YtDlpDownloadProgress
                {
                    Percent = 0,
                    StatusText = "Warming Reddit session via WebView…",
                });
                await WarmWebViewSessionAsync(originalUrl, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.Equals(downloadUrl, originalUrl, StringComparison.Ordinal))
            {
                AnoteFileLog.Append("ytdlp", "URL rewritten: " + originalUrl + " → " + downloadUrl);
            }

            var args = new List<string>(choice.BuildArguments(outDir, ffmpeg));
            AppendCommonArgs(args, downloadUrl, originalUrl, vkResolved, out string cookiesPath);

            progress?.Report(new YtDlpDownloadProgress { Percent = 0, StatusText = "Starting yt-dlp…" });

            try
            {
                AnoteYtDlpRuntime.EnsureReady(context);
                var runtimeProgress = new Progress<(double? Percent, string Status)>(p =>
                {
                    progress?.Report(new YtDlpDownloadProgress
                    {
                        Percent = p.Percent,
                        StatusText = p.Status,
                    });
                });

                (int code, string output, string lastPath) = await AnoteYtDlpRuntime.RunAsync(
                    context, script, args, runtimeProgress, cancellationToken).ConfigureAwait(false);

                TryDelete(cookiesPath);

                if (cancellationToken.IsCancellationRequested)
                    return Fail("Cancelled");

                if (code != 0)
                {
                    string detail = Truncate(output, 2000);
                    AnoteFileLog.Append("ytdlp", "exit=" + code + " url=" + url + "\n" + detail);
                    return Fail(string.IsNullOrWhiteSpace(detail)
                        ? "yt-dlp failed (exit " + code + ")"
                        : "yt-dlp failed: " + FirstUsefulLine(detail) + HintForFailure(originalUrl, detail));
                }

                string path = lastPath;
                if (string.IsNullOrEmpty(path))
                    path = TryFindNewestFile(outDir);

                progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                return new YtDlpDownloadResult
                {
                    Success = true,
                    OutputPath = path,
                    Message = string.IsNullOrEmpty(path)
                        ? "Downloaded to " + outDir
                        : "Saved: " + Path.GetFileName(path),
                };
            }
            catch (OperationCanceledException)
            {
                TryDelete(cookiesPath);
                return Fail("Cancelled");
            }
            catch (Exception ex)
            {
                TryDelete(cookiesPath);
                AnoteFileLog.Append("ytdlp", "Download exception for " + url + ": " + ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Light URL cleanup / Vimeo player rewrite. Prefer <see cref="YtDlpUrlHelper.NormalizeDownloadUrl"/>.
        /// </summary>
        internal static string NormalizeDownloadUrl(string url) => YtDlpUrlHelper.NormalizeDownloadUrl(url);

        private void AppendCommonArgs(
            List<string> args,
            string downloadUrl,
            string originalUrl,
            bool vkResolved,
            out string cookiesPath)
        {
            cookiesPath = null;
            args.Add("--user-agent");
            args.Add(YtDlpUrlHelper.BrowserUserAgent);

            SettingsModel settings = null;
            try
            {
                settings = _settingsService?.LoadSettingsOrDefault();
            }
            catch (Exception)
            {
            }

            if (settings != null && settings.Socks5Enabled && settings.HasSocks5Config)
            {
                string proxy = YtDlpUrlHelper.BuildSocks5ProxyArgument(
                    settings.Socks5Host,
                    settings.Socks5Port,
                    settings.Socks5Username,
                    settings.Socks5Password);
                if (!string.IsNullOrEmpty(proxy))
                {
                    args.Add("--proxy");
                    args.Add(proxy);
                    AnoteFileLog.Append("ytdlp", "Using SOCKS5 proxy " + settings.Socks5Host + ":" + settings.Socks5Port);
                }
            }

            bool needCookies = YtDlpUrlHelper.IsRedditUrl(originalUrl)
                || YtDlpUrlHelper.IsVimeoUrl(originalUrl)
                || (!vkResolved && YtDlpUrlHelper.IsVkUrl(originalUrl));

            if (needCookies)
            {
                cookiesPath = TryWriteWebViewCookiesFile(originalUrl);
                if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
                {
                    args.Add("--cookies");
                    args.Add(cookiesPath);
                    AnoteFileLog.Append("ytdlp", "Attached WebView cookies for " + HostOf(originalUrl));
                }
                else if (YtDlpUrlHelper.IsRedditUrl(originalUrl) || YtDlpUrlHelper.IsVimeoUrl(originalUrl))
                {
                    AnoteFileLog.Append(
                        "ytdlp",
                        "No WebView cookies for " + HostOf(originalUrl)
                        + " — open the site once in Abro browser (logged in if needed), then retry.");
                }
            }

            if (YtDlpUrlHelper.IsVimeoUrl(originalUrl) || YtDlpUrlHelper.IsVimeoUrl(downloadUrl))
            {
                args.Add("--referer");
                args.Add("https://vimeo.com/");
            }

            args.Add(downloadUrl);
        }

        /// <summary>
        /// Resolve VK media with Java HttpURLConnection — more reliable on-device than .NET HttpClient.
        /// </summary>
        private static string ResolveVkDirectViaJava(string url)
        {
            if (!YtDlpUrlHelper.TryParseVkVideoId(url, out string videoId))
                return null;

            HttpURLConnection conn = null;
            try
            {
                var endpoint = new URL("https://vkvideo.ru/al_video.php");
                conn = (HttpURLConnection)endpoint.OpenConnection();
                conn.RequestMethod = "POST";
                conn.ConnectTimeout = 15000;
                conn.ReadTimeout = 20000;
                conn.DoInput = true;
                conn.DoOutput = true;
                conn.InstanceFollowRedirects = true;
                conn.SetRequestProperty("User-Agent", YtDlpUrlHelper.BrowserUserAgent);
                conn.SetRequestProperty("Referer", "https://vkvideo.ru/");
                conn.SetRequestProperty("Origin", "https://vkvideo.ru/");
                conn.SetRequestProperty("X-Requested-With", "XMLHttpRequest");
                conn.SetRequestProperty("Accept", "*/*");
                conn.SetRequestProperty("Content-Type", "application/x-www-form-urlencoded");
                conn.SetRequestProperty("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");

                byte[] bodyBytes = Encoding.ASCII.GetBytes("al=1&act=show&video=" + Uri.EscapeDataString(videoId));
                using (var os = conn.OutputStream)
                    os.Write(bodyBytes, 0, bodyBytes.Length);

                int code = (int)conn.ResponseCode;
                Stream stream = code >= 400 ? conn.ErrorStream : conn.InputStream;
                if (stream == null)
                {
                    AnoteFileLog.Append("ytdlp", "VK Java HTTP empty stream, code=" + code);
                    return null;
                }

                byte[] raw;
                using (stream)
                using (var ms = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                        ms.Write(buf, 0, n);
                    raw = ms.ToArray();
                }

                AnoteFileLog.Append("ytdlp", "VK Java HTTP code=" + code + " bytes=" + raw.Length);
                string text = YtDlpUrlHelper.DecodeVkBody(raw);
                string media = YtDlpUrlHelper.PickBestVkSource(text);
                if (media == null)
                {
                    try
                    {
                        media = YtDlpUrlHelper.PickBestVkSource(Encoding.GetEncoding(1251).GetString(raw));
                    }
                    catch (Exception)
                    {
                    }
                }

                return media;
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "VK Java resolve error: " + ex.Message);
                return null;
            }
            finally
            {
                try { conn?.Disconnect(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Load the page in a hidden WebView so CookieManager gets a Chromium TLS session (Reddit/Vimeo).
        /// </summary>
        private async Task WarmWebViewSessionAsync(string url, CancellationToken cancellationToken)
        {
            Activity activity = _appContext.RootActivity;
            if (activity == null || activity.IsFinishing)
            {
                AnoteFileLog.Append("ytdlp", "WebView warm-up skipped (no activity)");
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            AWebView webView = null;

            void Start()
            {
                try
                {
                    ACookieManager.Instance?.SetAcceptCookie(true);
                    webView = new AWebView(activity);
                    webView.Tag = new Java.Lang.String(url);
                    webView.Settings.JavaScriptEnabled = true;
                    webView.Settings.DomStorageEnabled = true;
                    webView.Settings.UserAgentString = YtDlpUrlHelper.BrowserUserAgent;
                    ACookieManager.Instance?.SetAcceptThirdPartyCookies(webView, true);
                    webView.SetWebViewClient(new WarmWebViewClient(tcs));
                    // old.reddit often sets session cookies without Cloudflare Turnstile.
                    if (YtDlpUrlHelper.IsRedditUrl(url))
                        webView.LoadUrl("https://old.reddit.com/");
                    else
                        webView.LoadUrl(url);
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "WebView warm-up start failed: " + ex.Message);
                    tcs.TrySetResult(false);
                }
            }

            if (Looper.MyLooper() == Looper.MainLooper)
                Start();
            else
                activity.RunOnUiThread(Start);

            try
            {
                Task finished = await Task.WhenAny(tcs.Task, Task.Delay(14000, cancellationToken))
                    .ConfigureAwait(false);
                if (finished == tcs.Task)
                    AnoteFileLog.Append("ytdlp", "WebView warm-up finished for " + HostOf(url));
                else
                    AnoteFileLog.Append("ytdlp", "WebView warm-up timed out for " + HostOf(url));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    activity.RunOnUiThread(() =>
                    {
                        try
                        {
                            webView?.StopLoading();
                            webView?.Destroy();
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
                catch (Exception)
                {
                }
            }
        }

        private sealed class WarmWebViewClient : WebViewClient
        {
            private readonly TaskCompletionSource<bool> _tcs;
            private bool _done;

            public WarmWebViewClient(TaskCompletionSource<bool> tcs)
            {
                _tcs = tcs;
            }

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                if (_done)
                    return;

                // After old.reddit, load the original target so shreddit cookies land too.
                string current = url ?? string.Empty;
                if (current.IndexOf("old.reddit.com", StringComparison.OrdinalIgnoreCase) >= 0
                    && view?.Tag is Java.Lang.String target
                    && !string.IsNullOrEmpty(target.ToString()))
                {
                    view.LoadUrl(target.ToString());
                    return;
                }

                // Give Reddit a moment to set loid / challenge cookies.
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_done)
                        return;
                    _done = true;
                    try { ACookieManager.Instance?.Flush(); } catch (Exception) { }
                    _tcs.TrySetResult(true);
                }, 1800);
            }

            public override void OnReceivedError(
                AWebView view,
                IWebResourceRequest request,
                WebResourceError error)
            {
                base.OnReceivedError(view, request, error);
                if (request?.IsForMainFrame == true && !_done)
                {
                    _done = true;
                    _tcs.TrySetResult(false);
                }
            }
        }

        private string TryWriteWebViewCookiesFile(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    return null;

                ACookieManager manager = ACookieManager.Instance;
                if (manager == null)
                    return null;

                string host = uri.Host ?? string.Empty;
                var headers = new List<(string Domain, string CookieHeader)>();

                void Add(string domain, string pageUrl)
                {
                    string raw = manager.GetCookie(pageUrl);
                    if (!string.IsNullOrWhiteSpace(raw))
                        headers.Add((domain, raw));
                }

                if (host.EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("redd.it", StringComparison.OrdinalIgnoreCase))
                {
                    Add(".reddit.com", "https://www.reddit.com/");
                    Add(".reddit.com", "https://old.reddit.com/");
                    Add(".reddit.com", url);
                }
                else if (host.EndsWith("vimeo.com", StringComparison.OrdinalIgnoreCase))
                {
                    Add(".vimeo.com", "https://vimeo.com/");
                    Add(".vimeo.com", "https://player.vimeo.com/");
                    Add(".vimeo.com", url);
                }
                else if (host.Contains("vk", StringComparison.OrdinalIgnoreCase))
                {
                    Add(".vkvideo.ru", "https://vkvideo.ru/");
                    Add(".vk.com", "https://vk.com/");
                    Add(".vk.ru", "https://vk.ru/");
                }
                else
                {
                    Add("." + host.TrimStart('.'), url);
                }

                if (headers.Count == 0)
                    return null;

                string path = Path.Combine(
                    _appContext.Context?.CacheDir?.AbsolutePath ?? Path.GetTempPath(),
                    "ytdlp-cookies-" + Guid.NewGuid().ToString("N") + ".txt");
                YtDlpUrlHelper.WriteNetscapeCookiesFile(path, headers);
                return File.Exists(path) && new FileInfo(path).Length > 40 ? path : null;
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "WebView cookie export failed: " + ex.Message);
                return null;
            }
        }

        private static string HintForFailure(string url, string detail)
        {
            string d = detail ?? string.Empty;
            if (YtDlpUrlHelper.IsRedditUrl(url)
                && (d.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("Blocked", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("impersonat", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return " — Open reddit.com in Abro once (accept cookies), or enable SOCKS5 in Settings, then retry.";
            }

            if (YtDlpUrlHelper.IsVimeoUrl(url)
                && (d.IndexOf("logged-in", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("impersonat", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return " — Open the Vimeo page in Abro while logged in, or enable SOCKS5, then retry.";
            }

            if (YtDlpUrlHelper.IsVkUrl(url)
                && (d.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("111", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return " — vk.com is DNS-blocked on this network; enable SOCKS5 in Settings if direct resolve failed.";
            }

            return string.Empty;
        }

        private string ResolveYtDlpScript()
        {
            if (!string.IsNullOrEmpty(_mediaTools.YtDlpPath) && File.Exists(_mediaTools.YtDlpPath))
                return _mediaTools.YtDlpPath;
            return AnotePaths.ResolveYtDlpBinary();
        }

        private string ResolveFfmpegPath()
        {
            if (!string.IsNullOrEmpty(_mediaTools.FfmpegPath) && File.Exists(_mediaTools.FfmpegPath))
                return _mediaTools.FfmpegPath;
            return AnotePaths.ResolveFfmpegBinary();
        }

        private static string TryFindNewestFile(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return null;
                string newest = null;
                DateTime newestTime = DateTime.MinValue;
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    DateTime t = File.GetLastWriteTimeUtc(file);
                    if (t > newestTime)
                    {
                        newestTime = t;
                        newest = file;
                    }
                }

                return newest;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static YtDlpDownloadResult Fail(string message)
        {
            AnoteFileLog.Append("ytdlp", message ?? "unknown error");
            return new YtDlpDownloadResult { Success = false, Message = message ?? "Download failed" };
        }

        private static string FirstUsefulLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    continue;
                return Truncate(line, 200);
            }

            return Truncate(text.Trim(), 200);
        }

        private static string HostOf(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.Host : url;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "…";
        }
    }
}
