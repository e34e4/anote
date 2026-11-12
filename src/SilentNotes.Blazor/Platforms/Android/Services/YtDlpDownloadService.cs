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
                string stem = "reddit_" + (ExtractRedditPostId(originalUrl)
                    ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                // v.redd.it is TLS-fingerprinted (Java/curl → 403). Resolve + download inside Chromium.
                string redditSaved = await DownloadRedditViaWebViewAsync(
                        originalUrl, outDir, stem, progress, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(redditSaved) && File.Exists(redditSaved))
                {
                    progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                    return new YtDlpDownloadResult
                    {
                        Success = true,
                        OutputPath = redditSaved,
                        Message = "Saved: " + Path.GetFileName(redditSaved),
                    };
                }

                AnoteFileLog.Append("ytdlp", "Reddit WebView download failed; falling back to yt-dlp with cookies");
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
        /// Resolve + download Reddit media inside Chromium. v.redd.it is TLS-fingerprinted
        /// (Java/curl → 403); packaged-media.redd.it often works with Java. Prefers packaged-media,
        /// otherwise streams the file through an in-page fetch into a JS bridge.
        /// </summary>
        private async Task<string> DownloadRedditViaWebViewAsync(
            string url,
            string outDir,
            string stem,
            IProgress<YtDlpDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            Activity activity = _appContext.RootActivity;
            if (activity == null || activity.IsFinishing)
            {
                AnoteFileLog.Append("ytdlp", "Reddit WebView resolve skipped (no activity)");
                return null;
            }

            string pageUrl = BuildRedditPageUrl(url);
            string postId = ExtractRedditPostId(url);
            string destPath = Path.Combine(outDir, stem + ".mp4");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AWebView webView = null;
            ViewGroup host = null;
            RedditMediaBridge bridge = null;
            RedditResolveWebViewClient client = null;

            // Match Abro: process-wide WebView SOCKS (via local forwarder when auth is set).
            // Must finish before LoadUrl or CDN requests bypass the proxy.
            try
            {
                await WebViewSocksProxy.ApplyFromSettingsAsync(_settingsService).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "Reddit WebView SOCKS prepare failed: " + ex.Message);
            }

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

                    host = activity.FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
                    if (host != null)
                    {
                        float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
                        // Real on-screen size — tiny/invisible WebViews often never start media.
                        var lp = new FrameLayout.LayoutParams(
                            (int)(320 * density),
                            (int)(480 * density))
                        {
                            Gravity = GravityFlags.Bottom | GravityFlags.End,
                            RightMargin = (int)(8 * density),
                            BottomMargin = (int)(8 * density),
                        };
                        webView.Alpha = 0.15f;
                        webView.Visibility = ViewStates.Visible;
                        host.AddView(webView, lp);
                    }

                    bridge = new RedditMediaBridge(
                        destPath,
                        progress,
                        savedPath => tcs.TrySetResult(savedPath),
                        err =>
                        {
                            AnoteFileLog.Append("ytdlp", "Reddit bridge fail: " + err);
                            tcs.TrySetResult(null);
                        });
                    webView.AddJavascriptInterface(bridge, "AnoteReddit");
                    client = new RedditResolveWebViewClient(
                        webView,
                        pageUrl,
                        postId,
                        mediaUrl =>
                        {
                            AnoteFileLog.Append("ytdlp", "Reddit picked media: " + Truncate(mediaUrl, 160));
                            // Prefer Java for packaged-media (worked for 1xafo7x); Chromium fetch for v.redd.it.
                            if (mediaUrl.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Task.Run(() =>
                                {
                                    string saved = DownloadDirectViaJava(
                                        mediaUrl,
                                        outDir,
                                        stem,
                                        "https://www.reddit.com/",
                                        progress,
                                        cancellationToken);
                                    if (!string.IsNullOrEmpty(saved))
                                        tcs.TrySetResult(saved);
                                    else
                                    {
                                        activity.RunOnUiThread(() =>
                                            client?.StartChromiumFetch(mediaUrl));
                                    }
                                }, cancellationToken);
                            }
                            else
                            {
                                client?.StartChromiumFetch(mediaUrl);
                            }
                        },
                        () => tcs.TrySetResult(null));
                    bridge.SetMediaHandler(u => client?.OfferCandidate(u, fromJson: true));
                    webView.SetWebViewClient(client);
                    // Load the post directly — homepage warm-up was grabbing feed videos
                    // (same wrong packaged-media URL for every download).
                    AnoteFileLog.Append("ytdlp", "Reddit WebView loading post " + pageUrl);
                    webView.LoadUrl(pageUrl);
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
                Task finished = await Task.WhenAny(tcs.Task, Task.Delay(90000, cancellationToken))
                    .ConfigureAwait(false);
                if (finished != tcs.Task)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView download timed out");
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

        private static int RankRedditMediaUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return -1;
            if (url.IndexOf("DASH_AUDIO", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("/audio", StringComparison.OrdinalIgnoreCase) >= 0)
                return -1;
            if (url.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (url.IndexOf("1080", StringComparison.OrdinalIgnoreCase) >= 0) return 90;
                if (url.IndexOf("854", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("720", StringComparison.OrdinalIgnoreCase) >= 0) return 80;
                return 70;
            }

            if (url.IndexOf("v.redd.it", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (url.IndexOf("DASH_1080", StringComparison.OrdinalIgnoreCase) >= 0) return 60;
                if (url.IndexOf("DASH_720", StringComparison.OrdinalIgnoreCase) >= 0) return 50;
                if (url.IndexOf("DASH_480", StringComparison.OrdinalIgnoreCase) >= 0) return 40;
                if (url.IndexOf("DASH_360", StringComparison.OrdinalIgnoreCase) >= 0) return 30;
                return 20;
            }

            return 0;
        }

        private sealed class RedditMediaBridge : Java.Lang.Object
        {
            private readonly string _destPath;
            private readonly IProgress<YtDlpDownloadProgress> _progress;
            private readonly System.Action<string> _onSaved;
            private readonly System.Action<string> _onFail;
            private System.Action<string> _onMediaUrl;
            private readonly object _gate = new object();
            private FileStream _stream;
            private long _written;
            private long _total;
            private bool _closed;

            public RedditMediaBridge(
                string destPath,
                IProgress<YtDlpDownloadProgress> progress,
                System.Action<string> onSaved,
                System.Action<string> onFail)
            {
                _destPath = destPath;
                _progress = progress;
                _onSaved = onSaved;
                _onFail = onFail;
            }

            public void SetMediaHandler(System.Action<string> onMediaUrl) => _onMediaUrl = onMediaUrl;

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("onMedia")]
            public void OnMedia(string url)
            {
                try { _onMediaUrl?.Invoke(url); } catch (Exception) { }
            }

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("begin")]
            public void Begin(string path, long totalBytes)
            {
                lock (_gate)
                {
                    try
                    {
                        TryDelete(_destPath);
                        TryDelete(_destPath + ".part");
                        _stream = File.Create(_destPath + ".part");
                        _written = 0;
                        _total = totalBytes;
                        _closed = false;
                        AnoteFileLog.Append("ytdlp", "Reddit Chromium fetch begin total=" + totalBytes);
                    }
                    catch (Exception ex)
                    {
                        _onFail?.Invoke(ex.Message);
                    }
                }
            }

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("chunk")]
            public void Chunk(string base64)
            {
                lock (_gate)
                {
                    if (_stream == null || _closed || string.IsNullOrEmpty(base64))
                        return;
                    try
                    {
                        byte[] data = Convert.FromBase64String(base64);
                        _stream.Write(data, 0, data.Length);
                        _written += data.Length;
                        if (_total > 0)
                        {
                            int pct = (int)Math.Min(99, (_written * 100) / _total);
                            if (pct % 5 == 0)
                            {
                                _progress?.Report(new YtDlpDownloadProgress
                                {
                                    Percent = pct,
                                    StatusText = "Downloading… " + pct + "%",
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CloseStream();
                        _onFail?.Invoke(ex.Message);
                    }
                }
            }

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("end")]
            public void End()
            {
                lock (_gate)
                {
                    try
                    {
                        if (_stream == null)
                        {
                            _onFail?.Invoke("no stream");
                            return;
                        }

                        _stream.Flush();
                        CloseStream();
                        string part = _destPath + ".part";
                        if (!File.Exists(part) || new FileInfo(part).Length < 1000)
                        {
                            TryDelete(part);
                            _onFail?.Invoke("empty file");
                            return;
                        }

                        if (File.Exists(_destPath))
                            File.Delete(_destPath);
                        File.Move(part, _destPath);
                        AnoteFileLog.Append(
                            "ytdlp",
                            "Reddit Chromium saved " + _destPath + " (" + new FileInfo(_destPath).Length + " bytes)");
                        _onSaved?.Invoke(_destPath);
                    }
                    catch (Exception ex)
                    {
                        CloseStream();
                        _onFail?.Invoke(ex.Message);
                    }
                }
            }

            [global::Android.Webkit.JavascriptInterface]
            [Java.Interop.Export("fail")]
            public void Fail(string message)
            {
                lock (_gate)
                {
                    CloseStream();
                    TryDelete(_destPath + ".part");
                    _onFail?.Invoke(message ?? "fail");
                }
            }

            private void CloseStream()
            {
                if (_closed)
                    return;
                _closed = true;
                try { _stream?.Dispose(); } catch (Exception) { }
                _stream = null;
            }
        }

        private sealed class RedditResolveWebViewClient : WebViewClient
        {
            private readonly AWebView _webView;
            private readonly string _pageUrl;
            private readonly string _postId;
            private readonly System.Action<string> _onPicked;
            private readonly System.Action _onFatal;
            private bool _mediaArmed;
            private bool _pickStarted;
            private bool _fetchStarted;
            private bool _debounceScheduled;
            private string _bestUrl;
            private int _bestRank = -1;
            private int _extractAttempts;

            // Prefer post-specific reddit_video / v.redd.it — never first packaged-media in page
            // (homepage/related rails inject unrelated packaged-media URLs).
            private const string ExtractJs =
                "(function(){"
                + "try{"
                + "var html=document.documentElement?document.documentElement.innerHTML:'';"
                + "var m=html.match(/\"fallback_url\"\\s*:\\s*\"(https:[^\"]*v\\.redd\\.it[^\"]+)\"/);"
                + "if(m) return m[1].replace(/\\\\u0026/g,'&').replace(/\\\\\\//g,'/');"
                + "m=html.match(/https:\\/\\/v\\.redd\\.it\\/[A-Za-z0-9]+\\/DASH_\\d+\\.mp4[^\\\"\\s]*/);"
                + "if(m) return m[0].replace(/&amp;/g,'&');"
                + "m=html.match(/v\\.redd\\.it\\/([A-Za-z0-9]+)/);"
                + "if(m) return 'https://v.redd.it/'+m[1]+'/DASH_720.mp4?source=fallback';"
                + "var v=document.querySelector('shreddit-player video, video');"
                + "if(v && v.currentSrc && v.currentSrc.indexOf('redd.it')>=0) return v.currentSrc;"
                + "}catch(e){return '';}"
                + "return '';"
                + "})()";

            private const string FetchJsonJs =
                "(function(){"
                + "try{"
                + "var id=document.location.pathname.match(/comments\\/([a-z0-9]+)/i);"
                + "id=id?id[1]:'';"
                + "if(!id) return;"
                + "var urls=["
                + "'https://www.reddit.com/comments/'+id+'.json',"
                + "document.location.origin+document.location.pathname.replace(/\\/$/,'')+'.json'"
                + "];"
                + "function pick(rv){if(!rv)return'';return rv.fallback_url||rv.dash_url||rv.hls_url||'';}"
                + "function dig(j){"
                + "try{"
                + "var d=j[0].data.children[0].data;"
                + "if(d.id&&id&&d.id!==id&&d.name!=='t3_'+id) return '';"
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
                + ".then(function(j){var u=dig(j); if(u&&window.AnoteRedditPick) window.AnoteRedditPick(u); else go(i+1);})"
                + ".catch(function(){go(i+1);});"
                + "}"
                + "go(0);"
                + "}catch(e){}"
                + "})()";

            public RedditResolveWebViewClient(
                AWebView webView,
                string pageUrl,
                string postId,
                System.Action<string> onPicked,
                System.Action onFatal)
            {
                _webView = webView;
                _pageUrl = pageUrl;
                _postId = postId ?? string.Empty;
                _onPicked = onPicked;
                _onFatal = onFatal;
            }

            public void StartChromiumFetch(string mediaUrl)
            {
                if (_fetchStarted || _webView == null || string.IsNullOrEmpty(mediaUrl))
                    return;
                _fetchStarted = true;
                string escaped = mediaUrl.Replace("\\", "\\\\").Replace("'", "\\'");
                // credentials:'include' + Access-Control-Allow-Origin:* is rejected by Chromium
                // (v.redd.it uses ACAO:*). Omit credentials so the body is readable.
                string js =
                    "(async function(){"
                    + "try{"
                    + "var url='" + escaped + "';"
                    + "function b64(u8){var s='';var n=u8.length;for(var i=0;i<n;i++)s+=String.fromCharCode(u8[i]);return btoa(s);}"
                    + "var r=await fetch(url,{credentials:'omit',mode:'cors',headers:{"
                    + "'Referer':'https://www.reddit.com/',"
                    + "'Accept':'*/*'}});"
                    + "if(!r.ok){AnoteReddit.fail('HTTP '+r.status);return;}"
                    + "var total=Number(r.headers.get('content-length')||0);"
                    + "AnoteReddit.begin('', total);"
                    + "var reader=r.body.getReader();"
                    + "while(true){"
                    + "var n=await reader.read();"
                    + "if(n.done)break;"
                    + "AnoteReddit.chunk(b64(n.value));"
                    + "}"
                    + "AnoteReddit.end();"
                    + "}catch(e){AnoteReddit.fail(String(e&&e.message?e.message:e));}"
                    + "})();";
                try
                {
                    AnoteFileLog.Append("ytdlp", "Reddit Chromium fetch start " + Truncate(mediaUrl, 120));
                    _webView.EvaluateJavascript(js, null);
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit Chromium fetch inject failed: " + ex.Message);
                    _onFatal?.Invoke();
                }
            }

            public override WebResourceResponse ShouldInterceptRequest(AWebView view, IWebResourceRequest request)
            {
                try { OfferCandidate(request?.Url?.ToString(), fromJson: false); }
                catch (Exception) { }
                return base.ShouldInterceptRequest(view, request);
            }

            public override void OnLoadResource(AWebView view, string url)
            {
                base.OnLoadResource(view, url);
                OfferCandidate(url, fromJson: false);
            }

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                if (_pickStarted || view == null)
                    return;

                string current = url ?? string.Empty;
                if (!IsTargetPostUrl(current))
                {
                    // Redirected away from the post (login/challenge) — try hard navigation once.
                    if (!string.IsNullOrEmpty(_pageUrl)
                        && current.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        AnoteFileLog.Append("ytdlp", "Reddit not on post yet (" + Truncate(current, 80) + "); reloading target");
                        new Handler(Looper.MainLooper).PostDelayed(() =>
                        {
                            if (_pickStarted || view == null || _mediaArmed)
                                return;
                            try { view.LoadUrl(_pageUrl); } catch (Exception) { }
                        }, 900);
                    }

                    return;
                }

                if (!_mediaArmed)
                {
                    _mediaArmed = true;
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit media armed for post"
                        + (string.IsNullOrEmpty(_postId) ? string.Empty : " id=" + _postId));
                    try
                    {
                        view.EvaluateJavascript(
                            "window.AnoteRedditPick=function(u){if(u){AnoteReddit&&AnoteReddit.onMedia&&AnoteReddit.onMedia(u);}};",
                            null);
                    }
                    catch (Exception) { }

                    ScheduleForcePlayScan(view, 600);
                    ScheduleForcePlayScan(view, 2000);
                    ScheduleForcePlayScan(view, 4500);
                    ScheduleExtract(view, 900);
                    ScheduleExtract(view, 2500);
                    ScheduleExtract(view, 5500);
                    new Handler(Looper.MainLooper).PostDelayed(() =>
                    {
                        if (_pickStarted || view == null)
                            return;
                        try { view.EvaluateJavascript(FetchJsonJs, null); } catch (Exception) { }
                    }, 1100);
                }
            }

            private const string ForcePlayScanJs =
                "(function(){"
                + "try{"
                + "document.querySelectorAll('video,shreddit-player').forEach(function(el){"
                + "try{if(el.tagName==='VIDEO'){el.muted=true;el.playsInline=true;el.play();}else{var v=el.querySelector&&el.querySelector('video');if(v){v.muted=true;v.play();}} }catch(e){}"
                + "});"
                + "var hits=[];"
                + "performance.getEntriesByType('resource').forEach(function(e){"
                + "if(e&&e.name&&e.name.indexOf('v.redd.it')>=0) hits.push(e.name);"
                + "});"
                + "return hits.join('\\n');"
                + "}catch(e){return '';}"
                + "})()";

            private void ScheduleForcePlayScan(AWebView view, int delayMs)
            {
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_pickStarted || view == null || !_mediaArmed)
                        return;
                    try
                    {
                        view.EvaluateJavascript(ForcePlayScanJs, new RedditJsCallback(result =>
                        {
                            string text = UnwrapJsString(result);
                            if (string.IsNullOrWhiteSpace(text))
                                return;
                            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                OfferCandidate(line, fromJson: false);
                        }));
                    }
                    catch (Exception ex)
                    {
                        AnoteFileLog.Append("ytdlp", "Reddit force-play scan failed: " + ex.Message);
                    }
                }, delayMs);
            }

            private bool IsTargetPostUrl(string url)
            {
                if (string.IsNullOrEmpty(url)
                    || url.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (string.IsNullOrEmpty(_postId))
                    return true;
                return url.IndexOf("/comments/" + _postId, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private void ScheduleExtract(AWebView view, int delayMs)
            {
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_pickStarted || view == null || !_mediaArmed)
                        return;
                    _extractAttempts++;
                    try
                    {
                        view.EvaluateJavascript(ExtractJs, new RedditJsCallback(result =>
                        {
                            string media = UnwrapJsString(result);
                            if (!string.IsNullOrWhiteSpace(media))
                                OfferCandidate(media.Trim(), fromJson: false);
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
                if (request?.IsForMainFrame == true && _mediaArmed && !_pickStarted)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView main-frame error: " + error?.Description);
                    _onFatal?.Invoke();
                }
            }

            public void OfferCandidate(string rawUrl, bool fromJson = false)
            {
                // Ignore feed/homepage media until the target comments page is ready.
                if (!_mediaArmed || _pickStarted || string.IsNullOrEmpty(rawUrl) || !IsRedditMediaUrl(rawUrl))
                    return;

                string url = PreferProgressiveRedditUrl(rawUrl);
                // Network packaged-media is often a related/feed clip, not this post's video.
                // Only trust it when the post JSON (or explicit bridge) provided it.
                if (!fromJson
                    && url.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                int rank = RankRedditMediaUrl(url);
                if (rank < 0)
                    return;

                // Post JSON is authoritative — boost above incidental network hits.
                if (fromJson)
                    rank += 100;

                if (rank > _bestRank)
                {
                    _bestRank = rank;
                    _bestUrl = url;
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit candidate rank=" + rank
                        + (fromJson ? " json" : " net")
                        + " " + Truncate(url, 140));
                }

                // JSON hit: take soon. Network packaged-media: debounce so JSON can win.
                if (fromJson || rank >= 150)
                {
                    FinishPick();
                    return;
                }

                if (!_debounceScheduled)
                {
                    _debounceScheduled = true;
                    new Handler(Looper.MainLooper).PostDelayed(FinishPick, 3500);
                }
            }

            private void FinishPick()
            {
                if (_pickStarted)
                    return;
                if (string.IsNullOrEmpty(_bestUrl))
                    return;
                _pickStarted = true;
                try { ACookieManager.Instance?.Flush(); } catch (Exception) { }
                _onPicked?.Invoke(_bestUrl);
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
            private readonly System.Action<string> _onResult;

            public RedditJsCallback(System.Action<string> onResult)
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
