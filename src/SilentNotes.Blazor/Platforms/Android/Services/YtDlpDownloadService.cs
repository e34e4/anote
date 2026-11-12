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
using Android.Views;
using Android.Webkit;
using Android.Widget;
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
            bool directMedia = false;
            string shortOutputStem = null;
            string referer = null;

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
                    directMedia = true;
                    referer = "https://vkvideo.ru/";
                    shortOutputStem = YtDlpUrlHelper.TryParseVkVideoId(originalUrl, out string vid)
                        ? "vk_" + vid
                        : "vk_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
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
                    StatusText = "Resolving Reddit media via WebView…",
                });
                string redditDirect = await ResolveRedditDirectViaWebViewAsync(originalUrl, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(redditDirect))
                {
                    AnoteFileLog.Append("ytdlp", "Reddit resolved direct media: " + Truncate(redditDirect, 160));
                    downloadUrl = redditDirect;
                    directMedia = true;
                    referer = "https://www.reddit.com/";
                    shortOutputStem = "reddit_" + (ExtractRedditPostId(originalUrl)
                        ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    AnoteFileLog.Append("ytdlp", "Reddit direct resolve failed; falling back to yt-dlp with cookies");
                }
            }
            else if (!string.Equals(downloadUrl, originalUrl, StringComparison.Ordinal))
            {
                AnoteFileLog.Append("ytdlp", "URL rewritten: " + originalUrl + " → " + downloadUrl);
            }

            // Progressive CDN URLs: download with Java HttpURLConnection. yt-dlp's Python
            // resolver often fails DNS on okcdn / some CDNs even when Java/curl succeed.
            if (directMedia
                && !string.IsNullOrEmpty(shortOutputStem)
                && LooksLikeProgressiveMediaUrl(downloadUrl))
            {
                progress?.Report(new YtDlpDownloadProgress
                {
                    Percent = 0,
                    StatusText = "Downloading media…",
                });
                string saved = await Task.Run(
                    () => DownloadDirectViaJava(
                        downloadUrl,
                        outDir,
                        shortOutputStem,
                        referer ?? "https://vkvideo.ru/",
                        progress,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                {
                    progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                    return new YtDlpDownloadResult
                    {
                        Success = true,
                        OutputPath = saved,
                        Message = "Saved: " + Path.GetFileName(saved),
                    };
                }

                AnoteFileLog.Append("ytdlp", "Java direct download failed; falling back to yt-dlp for " + Truncate(downloadUrl, 120));
            }

            var args = new List<string>(choice.BuildArguments(outDir, ffmpeg));
            if (directMedia && !string.IsNullOrEmpty(shortOutputStem))
            {
                // Generic CDN URLs make %(title)/%(id) enormous → ENAMETOOLONG on Android.
                SetOutputTemplate(args, Path.Combine(outDir, shortOutputStem + ".%(ext)s"));
                // Single progressive/HLS URL — do not require separate video+audio formats.
                SetFormatSelector(args, "b/best");
            }

            AppendCommonArgs(args, downloadUrl, originalUrl, directMedia, referer, out string cookiesPath);

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

        private static bool LooksLikeProgressiveMediaUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            string path = uri.AbsolutePath ?? string.Empty;
            if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
                return false;
            string host = uri.Host ?? string.Empty;
            return host.IndexOf("okcdn.ru", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("vkvd", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("v.redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                || url.IndexOf("type=1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DownloadDirectViaJava(
            string mediaUrl,
            string outDir,
            string stem,
            string referer,
            IProgress<YtDlpDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            HttpURLConnection conn = null;
            string partPath = null;
            try
            {
                var endpoint = new URL(mediaUrl);
                conn = (HttpURLConnection)endpoint.OpenConnection();
                conn.RequestMethod = "GET";
                conn.ConnectTimeout = 20000;
                conn.ReadTimeout = 120000;
                conn.InstanceFollowRedirects = true;
                conn.SetRequestProperty("User-Agent", YtDlpUrlHelper.BrowserUserAgent);
                if (!string.IsNullOrEmpty(referer))
                    conn.SetRequestProperty("Referer", referer);
                conn.SetRequestProperty("Accept", "*/*");
                conn.SetRequestProperty("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");

                int code = (int)conn.ResponseCode;
                if (code < 200 || code >= 300)
                {
                    AnoteFileLog.Append("ytdlp", "Java direct HTTP " + code + " for " + Truncate(mediaUrl, 120));
                    return null;
                }

                string ext = GuessMediaExtension(conn.ContentType, mediaUrl);
                string finalPath = Path.Combine(outDir, stem + ext);
                partPath = finalPath + ".part";
                long total = -1;
                try { total = conn.ContentLengthLong; } catch (Exception) { try { total = conn.ContentLength; } catch (Exception) { } }

                using (Stream input = conn.InputStream)
                using (FileStream output = File.Create(partPath))
                {
                    byte[] buf = new byte[64 * 1024];
                    long written = 0;
                    int n;
                    int lastPct = -1;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n);
                        written += n;
                        if (total > 0)
                        {
                            int pct = (int)Math.Min(99, (written * 100) / total);
                            if (pct != lastPct && pct % 5 == 0)
                            {
                                lastPct = pct;
                                progress?.Report(new YtDlpDownloadProgress
                                {
                                    Percent = pct,
                                    StatusText = "Downloading… " + pct + "%",
                                });
                            }
                        }
                    }
                }

                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(partPath, finalPath);
                AnoteFileLog.Append("ytdlp", "Java direct saved " + finalPath + " (" + new FileInfo(finalPath).Length + " bytes)");
                return finalPath;
            }
            catch (OperationCanceledException)
            {
                TryDelete(partPath);
                throw;
            }
            catch (Exception ex)
            {
                TryDelete(partPath);
                AnoteFileLog.Append("ytdlp", "Java direct download error: " + ex.Message);
                return null;
            }
            finally
            {
                try { conn?.Disconnect(); } catch (Exception) { }
            }
        }

        private static string GuessMediaExtension(string contentType, string url)
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                string ct = contentType.ToLowerInvariant();
                if (ct.Contains("webm"))
                    return ".webm";
                if (ct.Contains("mp4") || ct.Contains("m4v"))
                    return ".mp4";
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                string path = uri.AbsolutePath ?? string.Empty;
                if (path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                    return ".webm";
                if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    return ".mp4";
            }

            return ".mp4";
        }

        /// <summary>
        /// Light URL cleanup / Vimeo player rewrite. Prefer <see cref="YtDlpUrlHelper.NormalizeDownloadUrl"/>.
        /// </summary>
        internal static string NormalizeDownloadUrl(string url) => YtDlpUrlHelper.NormalizeDownloadUrl(url);

        private void AppendCommonArgs(
            List<string> args,
            string downloadUrl,
            string originalUrl,
            bool directMedia,
            string referer,
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

            bool needCookies = !directMedia && (
                YtDlpUrlHelper.IsRedditUrl(originalUrl)
                || YtDlpUrlHelper.IsVimeoUrl(originalUrl)
                || YtDlpUrlHelper.IsVkUrl(originalUrl));

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

            if (!string.IsNullOrEmpty(referer))
            {
                args.Add("--referer");
                args.Add(referer);
            }
            else if (YtDlpUrlHelper.IsVimeoUrl(originalUrl) || YtDlpUrlHelper.IsVimeoUrl(downloadUrl))
            {
                args.Add("--referer");
                args.Add("https://vimeo.com/");
            }

            args.Add(downloadUrl);
        }

        private static void SetOutputTemplate(List<string> args, string template)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], "-o", StringComparison.Ordinal)
                    || string.Equals(args[i], "--output", StringComparison.Ordinal))
                {
                    args[i + 1] = template;
                    return;
                }
            }

            args.Insert(0, template);
            args.Insert(0, "-o");
        }

        private static void SetFormatSelector(List<string> args, string format)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], "-f", StringComparison.Ordinal)
                    || string.Equals(args[i], "--format", StringComparison.Ordinal))
                {
                    args[i + 1] = format;
                    return;
                }
            }

            args.Add("-f");
            args.Add(format);
        }

        private static string ExtractRedditPostId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                url,
                @"/comments/([a-z0-9]+)/",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
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
        /// Resolve a direct v.redd.it URL inside Chromium WebView (TLS fingerprint works;
        /// curl / yt-dlp Python get 403). Warms cookies, loads the post with a real-sized
        /// WebView, intercepts media, and fetches .json via in-page JS bridge.
        /// </summary>
        private async Task<string> ResolveRedditDirectViaWebViewAsync(string url, CancellationToken cancellationToken)
        {
            Activity activity = _appContext.RootActivity;
            if (activity == null || activity.IsFinishing)
            {
                AnoteFileLog.Append("ytdlp", "Reddit WebView resolve skipped (no activity)");
                return null;
            }

            string pageUrl = BuildRedditPageUrl(url);
            string postId = ExtractRedditPostId(url);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AWebView webView = null;
            ViewGroup host = null;
            RedditMediaBridge bridge = null;

            void Start()
            {
                try
                {
                    ACookieManager.Instance?.SetAcceptCookie(true);
                    webView = new AWebView(activity);
                    webView.Settings.JavaScriptEnabled = true;
                    webView.Settings.DomStorageEnabled = true;
                    webView.Settings.DatabaseEnabled = true;
                    webView.Settings.MediaPlaybackRequiresUserGesture = false;
                    webView.Settings.LoadWithOverviewMode = true;
                    webView.Settings.UseWideViewPort = true;
                    webView.Settings.UserAgentString = YtDlpUrlHelper.BrowserUserAgent;
                    ACookieManager.Instance?.SetAcceptThirdPartyCookies(webView, true);

                    // Real size required — shreddit skips media init in 1×1 / Invisible views.
                    host = activity.FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
                    if (host != null)
                    {
                        float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
                        var lp = new FrameLayout.LayoutParams(
                            (int)(360 * density),
                            (int)(640 * density))
                        {
                            Gravity = GravityFlags.Top | GravityFlags.Start,
                            LeftMargin = 0,
                            TopMargin = 0,
                        };
                        webView.Alpha = 0.02f;
                        webView.Visibility = ViewStates.Visible;
                        host.AddView(webView, lp);
                    }

                    bridge = new RedditMediaBridge(media =>
                    {
                        if (string.IsNullOrWhiteSpace(media))
                            return;
                        string prefer = PreferProgressiveRedditUrl(media.Trim());
                        AnoteFileLog.Append("ytdlp", "Reddit bridge media: " + Truncate(prefer, 160));
                        tcs.TrySetResult(prefer);
                    });
                    webView.AddJavascriptInterface(bridge, "AnoteReddit");
                    webView.SetWebViewClient(new RedditResolveWebViewClient(tcs, pageUrl, postId));
                    AnoteFileLog.Append("ytdlp", "Reddit WebView warm-up then " + pageUrl);
                    // Warm cookies / challenge first, then navigate to the post.
                    webView.LoadUrl("https://www.reddit.com/");
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView start failed: " + ex.Message);
                    tcs.TrySetResult(null);
                }
            }

            if (Looper.MyLooper() == Looper.MainLooper)
                Start();
            else
                activity.RunOnUiThread(Start);

            try
            {
                Task finished = await Task.WhenAny(tcs.Task, Task.Delay(45000, cancellationToken))
                    .ConfigureAwait(false);
                if (finished != tcs.Task)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView resolve timed out");
                    return null;
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
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
                            host?.RemoveView(webView);
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

        private static string BuildRedditPageUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return url;

            string path = uri.AbsolutePath ?? string.Empty;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 5);

            // www shreddit page — WebView TLS passes where curl/Python get 403.
            return "https://www.reddit.com" + path + uri.Query;
        }

        private static string PreferProgressiveRedditUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (url.IndexOf("v.redd.it/", StringComparison.OrdinalIgnoreCase) >= 0
                && (url.IndexOf("DASHPlaylist.mpd", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("HLSPlaylist.m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("/DASHPlaylist", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                int idx = url.IndexOf("v.redd.it/", StringComparison.OrdinalIgnoreCase);
                string rest = url.Substring(idx + "v.redd.it/".Length);
                string id = rest.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
                if (!string.IsNullOrEmpty(id))
                    return "https://v.redd.it/" + id + "/DASH_720.mp4?source=fallback";
            }

            return url;
        }

        private sealed class RedditMediaBridge : Java.Lang.Object
        {
            private readonly Action<string> _onMedia;

            public RedditMediaBridge(Action<string> onMedia)
            {
                _onMedia = onMedia;
            }

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("onMedia")]
            public void OnMedia(string url)
            {
                try { _onMedia?.Invoke(url); } catch (Exception) { }
            }
        }

        private sealed class RedditResolveWebViewClient : WebViewClient
        {
            private readonly TaskCompletionSource<string> _tcs;
            private readonly string _pageUrl;
            private readonly string _postId;
            private bool _done;
            private bool _loadedPost;
            private int _extractAttempts;

            // Sync DOM/HTML scrape.
            private const string ExtractJs =
                "(function(){"
                + "try{"
                + "function pick(rv){if(!rv)return'';return rv.fallback_url||rv.dash_url||rv.hls_url||'';}"
                + "var html=document.documentElement?document.documentElement.innerHTML:'';"
                + "var m=html.match(/https:\\/\\/v\\.redd\\.it\\/[A-Za-z0-9]+\\/DASH_\\d+\\.mp4[^\\\"\\s]*/);"
                + "if(m) return m[0].replace(/&amp;/g,'&');"
                + "m=html.match(/https:\\/\\/v\\.redd\\.it\\/[A-Za-z0-9]+\\/(?:DASHPlaylist\\.mpd|HLSPlaylist\\.m3u8)[^\\\"\\s]*/);"
                + "if(m) return m[0].replace(/&amp;/g,'&');"
                + "m=html.match(/\"fallback_url\"\\s*:\\s*\"(https:[^\"]+)\"/);"
                + "if(m) return m[1].replace(/\\\\u0026/g,'&').replace(/\\\\\\//g,'/');"
                + "m=html.match(/v\\.redd\\.it\\/([A-Za-z0-9]+)/);"
                + "if(m) return 'https://v.redd.it/'+m[1]+'/DASH_720.mp4?source=fallback';"
                + "var v=document.querySelector('video');"
                + "if(v && v.currentSrc && v.currentSrc.indexOf('redd.it')>=0) return v.currentSrc;"
                + "if(v && v.src && v.src.indexOf('redd.it')>=0) return v.src;"
                + "}catch(e){return '';}"
                + "return '';"
                + "})()";

            // Async JSON fetch through Chromium (same TLS/cookies as the page).
            private const string FetchJsonJs =
                "(function(){"
                + "try{"
                + "var id=document.location.pathname.match(/comments\\/([a-z0-9]+)/i);"
                + "id=id?id[1]:'';"
                + "var urls=[];"
                + "if(id) urls.push('https://www.reddit.com/comments/'+id+'.json');"
                + "urls.push(document.location.origin+document.location.pathname.replace(/\\/$/,'')+'.json');"
                + "function pick(rv){if(!rv)return'';return rv.fallback_url||rv.dash_url||rv.hls_url||'';}"
                + "function dig(j){"
                + "try{"
                + "var d=j[0].data.children[0].data;"
                + "var rv=(d.secure_media&&d.secure_media.reddit_video)||(d.media&&d.media.reddit_video);"
                + "if(!rv&&d.crosspost_parent_list&&d.crosspost_parent_list[0]){"
                + "var c=d.crosspost_parent_list[0];"
                + "rv=(c.secure_media&&c.secure_media.reddit_video)||(c.media&&c.media.reddit_video);"
                + "}"
                + "return pick(rv)||'';"
                + "}catch(e){return '';}"
                + "}"
                + "function go(i){"
                + "if(i>=urls.length){return;}"
                + "fetch(urls[i],{credentials:'include',headers:{'Accept':'application/json'}})"
                + ".then(function(r){return r.ok?r.json():Promise.reject(r.status);})"
                + ".then(function(j){var u=dig(j); if(u&&window.AnoteReddit) window.AnoteReddit.onMedia(u); else go(i+1);})"
                + ".catch(function(){go(i+1);});"
                + "}"
                + "go(0);"
                + "}catch(e){}"
                + "})()";

            public RedditResolveWebViewClient(TaskCompletionSource<string> tcs, string pageUrl, string postId)
            {
                _tcs = tcs;
                _pageUrl = pageUrl;
                _postId = postId ?? string.Empty;
            }

            public override WebResourceResponse ShouldInterceptRequest(AWebView view, IWebResourceRequest request)
            {
                try
                {
                    string reqUrl = request?.Url?.ToString();
                    if (!_done && IsRedditMediaUrl(reqUrl))
                    {
                        string prefer = PreferProgressiveRedditUrl(reqUrl);
                        AnoteFileLog.Append("ytdlp", "Reddit intercepted media: " + Truncate(prefer, 160));
                        Complete(prefer);
                    }
                }
                catch (Exception)
                {
                }

                return base.ShouldInterceptRequest(view, request);
            }

            public override void OnLoadResource(AWebView view, string url)
            {
                base.OnLoadResource(view, url);
                if (!_done && IsRedditMediaUrl(url))
                {
                    string prefer = PreferProgressiveRedditUrl(url);
                    AnoteFileLog.Append("ytdlp", "Reddit OnLoadResource media: " + Truncate(prefer, 160));
                    Complete(prefer);
                }
            }

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                if (_done || view == null)
                    return;

                string current = url ?? string.Empty;
                // After warm-up homepage, open the target post.
                if (!_loadedPost
                    && current.IndexOf("reddit.com", StringComparison.OrdinalIgnoreCase) >= 0
                    && current.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _loadedPost = true;
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit warm-up done; loading post"
                        + (string.IsNullOrEmpty(_postId) ? string.Empty : " id=" + _postId));
                    new Handler(Looper.MainLooper).PostDelayed(() =>
                    {
                        if (_done || view == null)
                            return;
                        try { view.LoadUrl(_pageUrl); } catch (Exception) { }
                    }, 700);
                    return;
                }

                if (current.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) >= 0
                    || (!_loadedPost && string.Equals(current, _pageUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    _loadedPost = true;
                    ScheduleExtract(view, 800);
                    ScheduleExtract(view, 2000);
                    ScheduleExtract(view, 4000);
                    ScheduleExtract(view, 8000);
                    new Handler(Looper.MainLooper).PostDelayed(() =>
                    {
                        if (_done || view == null)
                            return;
                        try { view.EvaluateJavascript(FetchJsonJs, null); } catch (Exception) { }
                    }, 1200);
                }
            }

            private void ScheduleExtract(AWebView view, int delayMs)
            {
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_done || view == null)
                        return;
                    _extractAttempts++;
                    try
                    {
                        view.EvaluateJavascript(ExtractJs, new RedditJsCallback(result =>
                        {
                            if (_done)
                                return;
                            string media = UnwrapJsString(result);
                            if (!string.IsNullOrWhiteSpace(media)
                                && media.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                AnoteFileLog.Append("ytdlp", "Reddit JS media: " + Truncate(media, 160));
                                Complete(PreferProgressiveRedditUrl(media.Trim()));
                            }
                            else if (_extractAttempts >= 4)
                            {
                                AnoteFileLog.Append("ytdlp", "Reddit JS extract empty after retries");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        AnoteFileLog.Append("ytdlp", "Reddit JS extract failed: " + ex.Message);
                    }
                }, delayMs);
            }

            public override void OnReceivedError(
                AWebView view,
                IWebResourceRequest request,
                WebResourceError error)
            {
                base.OnReceivedError(view, request, error);
                if (request?.IsForMainFrame == true && !_done && _loadedPost)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView main-frame error: " + error?.Description);
                    Complete(null);
                }
            }

            private void Complete(string media)
            {
                if (_done)
                    return;
                _done = true;
                try { ACookieManager.Instance?.Flush(); } catch (Exception) { }
                _tcs.TrySetResult(media);
            }

            private static bool IsRedditMediaUrl(string url)
            {
                if (string.IsNullOrEmpty(url))
                    return false;
                return url.IndexOf("v.redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static string UnwrapJsString(string result)
            {
                if (string.IsNullOrWhiteSpace(result) || result == "null" || result == "\"\"")
                    return null;
                result = result.Trim();
                if (result.Length >= 2 && result[0] == '"' && result[^1] == '"')
                {
                    try
                    {
                        return System.Text.Json.JsonSerializer.Deserialize<string>(result);
                    }
                    catch (Exception)
                    {
                        return result.Substring(1, result.Length - 2);
                    }
                }

                return result;
            }
        }

        private sealed class RedditJsCallback : Java.Lang.Object, IValueCallback
        {
            private readonly Action<string> _onResult;

            public RedditJsCallback(Action<string> onResult)
            {
                _onResult = onResult;
            }

            public void OnReceiveValue(Java.Lang.Object value)
            {
                _onResult?.Invoke(value?.ToString());
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
