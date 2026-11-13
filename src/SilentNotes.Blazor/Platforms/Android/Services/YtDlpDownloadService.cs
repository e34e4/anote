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
                    StatusText = YtDlpUrlHelper.IsRedditShareUrl(originalUrl)
                        ? "Resolving Reddit share link via WebView…"
                        : "Resolving Reddit media via WebView…",
                });
                string stem = "reddit_" + (ExtractRedditPostId(downloadUrl)
                    ?? ExtractRedditPostId(originalUrl)
                    ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                // v.redd.it is TLS-fingerprinted (Java/curl → 403). Resolve + download inside Chromium.
                // Share links (/s/CODE) must be resolved to /comments/{id}/ — yt-dlp has no extractor for /s/.
                RedditWebViewResult redditResult = await DownloadRedditViaWebViewAsync(
                        downloadUrl, outDir, stem, progress, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(redditResult?.SavedPath) && File.Exists(redditResult.SavedPath))
                {
                    progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                    return new YtDlpDownloadResult
                    {
                        Success = true,
                        OutputPath = redditResult.SavedPath,
                        Message = "Saved: " + Path.GetFileName(redditResult.SavedPath),
                    };
                }

                // If WebView found a v.redd.it URL but Chromium fetch 403'd, try Java HTTP next.
                // yt-dlp's Reddit JSON API is often IP-blocked even with cookies/SOCKS.
                if (!string.IsNullOrEmpty(redditResult?.LastMediaUrl))
                {
                    progress?.Report(new YtDlpDownloadProgress
                    {
                        Percent = 0,
                        StatusText = "Downloading Reddit media…",
                    });
                    string javaSaved = await Task.Run(
                        () => TryDownloadRedditMediaCandidates(
                            redditResult.LastMediaUrl,
                            outDir,
                            stem,
                            progress,
                            cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(javaSaved) && File.Exists(javaSaved))
                    {
                        progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                        return new YtDlpDownloadResult
                        {
                            Success = true,
                            OutputPath = javaSaved,
                            Message = "Saved: " + Path.GetFileName(javaSaved),
                        };
                    }
                }

                if (!string.IsNullOrEmpty(redditResult?.ResolvedCommentsUrl)
                    && !string.Equals(downloadUrl, redditResult.ResolvedCommentsUrl, StringComparison.OrdinalIgnoreCase))
                {
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit share resolved to " + redditResult.ResolvedCommentsUrl + " for yt-dlp fallback");
                    downloadUrl = redditResult.ResolvedCommentsUrl;
                }
                else if (YtDlpUrlHelper.IsRedditShareUrl(downloadUrl))
                {
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit share link did not resolve to a comments URL; yt-dlp [generic] will likely 403");
                }

                // yt-dlp's Reddit JSON API is blocked for this IP/proxy even with cookies —
                // falling through only wastes time and shows a misleading "API blocked" error.
                AnoteFileLog.Append(
                    "ytdlp",
                    "Reddit WebView download failed; skipping yt-dlp (JSON API blocked on this network)");
                return Fail(
                    "Could not capture Reddit video from the page"
                    + HintForFailure(originalUrl, "unable to access the Reddit API"));
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
                if (!string.IsNullOrEmpty(referer)
                    && referer.IndexOf("reddit.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    conn.SetRequestProperty("Origin", "https://www.reddit.com");
                    try
                    {
                        string cookies = ACookieManager.Instance?.GetCookie("https://www.reddit.com/");
                        if (string.IsNullOrEmpty(cookies))
                            cookies = ACookieManager.Instance?.GetCookie(referer);
                        if (!string.IsNullOrEmpty(cookies))
                            conn.SetRequestProperty("Cookie", cookies);
                    }
                    catch (Exception)
                    {
                    }
                }

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
                if (!YtDlpUrlHelper.TryProbeSocks5Tcp(settings.Socks5Host, settings.Socks5Port))
                {
                    AnoteFileLog.Append(
                        "ytdlp",
                        "SOCKS5 proxy unreachable (connection refused): "
                        + settings.Socks5Host + ":" + settings.Socks5Port
                        + " — skipping --proxy for this run");
                }
                else
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
            return YtDlpUrlHelper.TryExtractRedditPostId(url, out string id) ? id : null;
        }

        private sealed class RedditWebViewResult
        {
            public string SavedPath { get; init; }
            public string ResolvedCommentsUrl { get; init; }
            public string LastMediaUrl { get; init; }
        }

        /// <summary>
        /// Try progressive v.redd.it qualities via Java HttpURLConnection (Chromium fetch often 403s).
        /// </summary>
        private static string TryDownloadRedditMediaCandidates(
            string mediaUrl,
            string outDir,
            string stem,
            IProgress<YtDlpDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            foreach (string candidate in ExpandRedditMediaCandidates(mediaUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnoteFileLog.Append("ytdlp", "Reddit Java try " + Truncate(candidate, 140));
                string saved = DownloadDirectViaJava(
                    candidate,
                    outDir,
                    stem,
                    "https://www.reddit.com/",
                    progress,
                    cancellationToken);
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                    return saved;
            }

            return null;
        }

        private static System.Collections.Generic.List<string> ExpandRedditMediaCandidates(string mediaUrl)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new System.Collections.Generic.List<string>();
            void Add(string u)
            {
                if (!string.IsNullOrWhiteSpace(u) && seen.Add(u))
                    list.Add(u);
            }

            string progressive = PreferProgressiveRedditUrl(mediaUrl);
            Add(progressive);
            Add(mediaUrl);

            // Derive v.redd.it id and try common progressive ladders (720→240).
            string id = null;
            int idx = (progressive ?? mediaUrl ?? string.Empty)
                .IndexOf("v.redd.it/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rest = (progressive ?? mediaUrl).Substring(idx + "v.redd.it/".Length);
                id = rest.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            }

            if (!string.IsNullOrEmpty(id))
            {
                foreach (string q in new[] { "720", "480", "360", "240", "96" })
                    Add("https://v.redd.it/" + id + "/DASH_" + q + ".mp4?source=fallback");
            }

            return list;
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
        /// Also follows <c>/s/</c> share redirects to a comments permalink for yt-dlp fallback.
        /// </summary>
        private async Task<RedditWebViewResult> DownloadRedditViaWebViewAsync(
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
                        // Near-full viewport — shreddit often refuses to start media in tiny/invisible views.
                        int w = host.Width > 0 ? Math.Max(host.Width - (int)(16 * density), (int)(360 * density))
                            : (int)(360 * density);
                        int h = host.Height > 0 ? Math.Max(host.Height / 2, (int)(520 * density))
                            : (int)(520 * density);
                        var lp = new FrameLayout.LayoutParams(w, h)
                        {
                            Gravity = GravityFlags.Bottom | GravityFlags.End,
                            RightMargin = (int)(8 * density),
                            BottomMargin = (int)(8 * density),
                        };
                        webView.Alpha = 0.92f;
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
                            // Do not fail the whole attempt yet — Java retry runs below / after timeout.
                            string media = client?.LastPickedMediaUrl;
                            if (string.IsNullOrEmpty(media))
                                return;
                            Task.Run(() =>
                            {
                                string saved = TryDownloadRedditMediaCandidates(
                                    media, outDir, stem, progress, cancellationToken);
                                if (!string.IsNullOrEmpty(saved))
                                    tcs.TrySetResult(saved);
                                // else leave TCS unset so timeout / outer Java pass can finish
                            }, cancellationToken);
                        });
                    webView.AddJavascriptInterface(bridge, "AnoteReddit");
                    client = new RedditResolveWebViewClient(
                        webView,
                        pageUrl,
                        postId,
                        mediaUrl =>
                        {
                            AnoteFileLog.Append("ytdlp", "Reddit picked media: " + Truncate(mediaUrl, 160));
                            // Video is already playing in this WebView — Chromium fetch has cookies/TLS.
                            // Java HttpURLConnection often 403s on v.redd.it / packaged-media.
                            activity.RunOnUiThread(() =>
                            {
                                client?.StartChromiumFetch(mediaUrl);
                                // Parallel Java attempt as backup (may win on packaged-media).
                                Task.Run(() =>
                                {
                                    string saved = TryDownloadRedditMediaCandidates(
                                        mediaUrl, outDir, stem, progress, cancellationToken);
                                    if (!string.IsNullOrEmpty(saved))
                                        tcs.TrySetResult(saved);
                                    else
                                        AnoteFileLog.Append("ytdlp", "Reddit Java download failed (Chromium may still save)");
                                }, cancellationToken);
                            });
                        },
                        () => { /* keep waiting for Java retries / timeout */ });
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
                string saved = null;
                if (finished != tcs.Task)
                {
                    string best = client?.BestMediaUrl;
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit WebView download timed out best="
                        + (string.IsNullOrEmpty(best) ? "(none)" : Truncate(best, 140)));
                    // Last chance: force-pick + Java if we saw a URL but never finished saving.
                    if (!string.IsNullOrEmpty(best))
                    {
                        try
                        {
                            activity.RunOnUiThread(() => client?.ForceFinishPick());
                        }
                        catch (Exception)
                        {
                        }

                        saved = await Task.Run(
                            () => TryDownloadRedditMediaCandidates(
                                best, outDir, stem, progress, cancellationToken),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    saved = await tcs.Task.ConfigureAwait(false);
                }

                return new RedditWebViewResult
                {
                    SavedPath = saved,
                    ResolvedCommentsUrl = client?.ResolvedCommentsUrl,
                    LastMediaUrl = client?.BestMediaUrl,
                };
            }
            catch (OperationCanceledException)
            {
                return new RedditWebViewResult
                {
                    ResolvedCommentsUrl = client?.ResolvedCommentsUrl,
                    LastMediaUrl = client?.LastPickedMediaUrl,
                };
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

            if (url.IndexOf("v.redd.it/", StringComparison.OrdinalIgnoreCase) < 0)
                return url;

            // Playlists and CMAF variants → stable progressive DASH MP4 (what Abro plays).
            if (url.IndexOf("DASHPlaylist.mpd", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("HLSPlaylist.m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("/DASHPlaylist", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("CMAF_", StringComparison.OrdinalIgnoreCase) >= 0)
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

            if (url.IndexOf(".mp4", StringComparison.OrdinalIgnoreCase) >= 0)
                return 35;
            if (url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf(".mpd", StringComparison.OrdinalIgnoreCase) >= 0)
                return 25;

            return 10;
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
            private readonly bool _startedFromShare;
            private string _postId;
            private readonly System.Action<string> _onPicked;
            private readonly System.Action _onFatal;
            private bool _mediaArmed;
            private bool _pickStarted;
            private bool _fetchStarted;
            private bool _debounceScheduled;
            private bool _shareReloadScheduled;
            private string _bestUrl;
            private int _bestRank = -1;
            private int _extractAttempts;
            // Media often starts before OnPageFinished arms — keep early CDN hits.
            private readonly System.Collections.Generic.List<string> _preArmHits =
                new System.Collections.Generic.List<string>();

            /// <summary>Comments permalink discovered after a <c>/s/</c> share redirect (for yt-dlp).</summary>
            public string ResolvedCommentsUrl { get; private set; }

            /// <summary>Last accepted media CDN URL (for Java download after Chromium 403).</summary>
            public string LastPickedMediaUrl { get; private set; }

            /// <summary>Best candidate seen so far (even if <see cref="FinishPick"/> has not run yet).</summary>
            public string BestMediaUrl => !string.IsNullOrEmpty(LastPickedMediaUrl) ? LastPickedMediaUrl : _bestUrl;

            // Prefer post video URLs (v.redd.it / packaged-media). Filter out preview/thumb noise.
            // Returns newline-separated candidates (first is best guess).
            private const string ExtractJs =
                "(function(){"
                + "try{"
                + "var out=[],seen={};"
                + "function isMedia(u){"
                + "return /v\\.redd\\.it/i.test(u)||/packaged-media\\.redd\\.it/i.test(u)"
                + "||/\\.(m3u8|mpd|mp4)([?#]|$)/i.test(u);}"
                + "function add(u){if(!u)return;u=String(u).replace(/\\\\u0026/g,'&').replace(/\\\\\\//g,'/').replace(/&amp;/g,'&');"
                + "if(!isMedia(u))return; if(seen[u])return;seen[u]=1;out.push(u);}"
                + "function addJsonBlob(raw){if(!raw)return; try{"
                + "var s=typeof raw==='string'?raw:JSON.stringify(raw);"
                + "var m=s.match(/https:\\/\\/[^\\\"\\s]+/g)||[];"
                + "m.forEach(function(x){add(x.replace(/\\\\u0026/g,'&'));});"
                + "}catch(e){}}"
                + "function walk(node){if(!node)return;"
                + "try{if(node.currentSrc)add(node.currentSrc);if(node.src)add(node.src);"
                + "if(node.getAttribute){add(node.getAttribute('src'));add(node.getAttribute('data-hls-url'));"
                + "add(node.getAttribute('data-mpd-url'));"
                + "addJsonBlob(node.getAttribute('packaged-media-json'));"
                + "addJsonBlob(node.getAttribute('data-packaged-media-json'));}}"
                + "catch(e){} try{if(node.shadowRoot)walk(node.shadowRoot);}catch(e){}"
                + "var ch=node.children||node.childNodes; if(ch){for(var i=0;i<ch.length;i++)walk(ch[i]);}}"
                + "var html=document.documentElement?document.documentElement.outerHTML:'';"
                + "var m;"
                + "m=html.match(/\"fallback_url\"\\s*:\\s*\"(https:[^\"]+)\"/g)||[];"
                + "m.forEach(function(x){var y=x.match(/\"(https:[^\"]+)\"/);if(y)add(y[1]);});"
                + "m=html.match(/\"(?:dash_url|hls_url)\"\\s*:\\s*\"(https:[^\"]+)\"/g)||[];"
                + "m.forEach(function(x){var y=x.match(/\"(https:[^\"]+)\"/);if(y)add(y[1]);});"
                + "m=html.match(/https:\\/\\/v\\.redd\\.it\\/[A-Za-z0-9]+\\/(?:DASH_\\d+\\.mp4|HLSPlaylist\\.m3u8|DASHPlaylist\\.mpd)[^\\\"\\s]*/g)||[];"
                + "m.forEach(add);"
                + "m=html.match(/https:\\/\\/packaged-media\\.redd\\.it\\/[^\\\"\\s]+/g)||[];"
                + "m.forEach(add);"
                + "m=html.match(/v\\.redd\\.it\\/([A-Za-z0-9]+)/g)||[];"
                + "m.forEach(function(x){var id=x.split('/')[1];if(id)add('https://v.redd.it/'+id+'/DASH_720.mp4?source=fallback');});"
                + "var metas=document.querySelectorAll('meta[property=\"og:video\"],meta[property=\"og:video:url\"],meta[property=\"og:video:secure_url\"],meta[name=\"twitter:player:stream\"]');"
                + "for(var i=0;i<metas.length;i++)add(metas[i].content);"
                + "document.querySelectorAll('shreddit-player,shreddit-player-2,video,source').forEach(walk);"
                + "walk(document.body);"
                + "try{performance.getEntriesByType('resource').forEach(function(e){if(e&&e.name)add(e.name);});}catch(e){}"
                + "return out.join('\\n');"
                + "}catch(e){return '';}"
                + "})()";

            private const string FetchJsonJs =
                "(function(){"
                + "try{"
                + "var id=document.location.pathname.match(/comments\\/([a-z0-9]+)/i);"
                + "id=id?id[1]:'';"
                + "if(!id) return;"
                + "var urls=["
                + "'https://www.reddit.com/comments/'+id+'.json',"
                + "document.location.origin+document.location.pathname.replace(/\\/$/,'')+'.json',"
                + "'https://old.reddit.com/comments/'+id+'.json',"
                + "'https://old.reddit.com'+document.location.pathname.replace(/\\/$/,'')+'.json'"
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
                + "function emit(u){if(u&&window.AnoteRedditPick) window.AnoteRedditPick(u);}"
                + "function go(i){"
                + "if(i>=urls.length){tryOldHtml();return;}"
                + "fetch(urls[i],{credentials:'include',headers:{'Accept':'application/json'}})"
                + ".then(function(r){return r.ok?r.json():Promise.reject(r.status);})"
                + ".then(function(j){var u=dig(j); if(u) emit(u); else go(i+1);})"
                + ".catch(function(){go(i+1);});"
                + "}"
                // HTML fallback when JSON API is IP-blocked (common even with SOCKS).
                + "function tryOldHtml(){"
                + "var pages=["
                + "'https://old.reddit.com'+document.location.pathname,"
                + "'https://old.reddit.com/comments/'+id+'/',"
                + "document.location.href"
                + "];"
                + "function gh(i){"
                + "if(i>=pages.length)return;"
                + "fetch(pages[i],{credentials:'include',headers:{'Accept':'text/html'}})"
                + ".then(function(r){return r.ok?r.text():Promise.reject(r.status);})"
                + ".then(function(t){"
                + "var m=t.match(/\"fallback_url\"\\s*:\\s*\"(https:[^\"]+)\"/);"
                + "if(m){emit(m[1].replace(/\\\\u0026/g,'&').replace(/\\\\\\//g,'/'));return;}"
                + "m=t.match(/https:\\/\\/v\\.redd\\.it\\/[A-Za-z0-9]+\\/DASH_\\d+\\.mp4[^\\\"\\s]*/);"
                + "if(m){emit(m[0]);return;}"
                + "m=t.match(/v\\.redd\\.it\\/([A-Za-z0-9]+)/);"
                + "if(m){emit('https://v.redd.it/'+m[1]+'/DASH_720.mp4?source=fallback');return;}"
                + "gh(i+1);"
                + "}).catch(function(){gh(i+1);});"
                + "}"
                + "gh(0);"
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
                _startedFromShare = YtDlpUrlHelper.IsRedditShareUrl(pageUrl);
                _onPicked = onPicked;
                _onFatal = onFatal;
            }

            private void RememberCommentsUrl(string url)
            {
                if (!YtDlpUrlHelper.TryExtractRedditPostId(url, out string id))
                    return;
                if (string.IsNullOrEmpty(_postId))
                    _postId = id;
                ResolvedCommentsUrl ??= YtDlpUrlHelper.BuildRedditCommentsUrl(id);
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && (uri.Host ?? string.Empty).EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase))
                {
                    // Prefer the full /r/sub/comments/id/… form when available.
                    string path = uri.AbsolutePath ?? string.Empty;
                    if (path.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) >= 0)
                        ResolvedCommentsUrl = "https://www.reddit.com" + path.TrimEnd('/') + "/";
                }
            }

            public void StartChromiumFetch(string mediaUrl)
            {
                if (_fetchStarted || _webView == null || string.IsNullOrEmpty(mediaUrl))
                    return;

                // JavascriptInterface callbacks run on JavaBridge — EvaluateJavascript needs main.
                if (Looper.MyLooper() != Looper.MainLooper)
                {
                    new Handler(Looper.MainLooper).Post(() => StartChromiumFetch(mediaUrl));
                    return;
                }

                if (_fetchStarted || _webView == null)
                    return;
                _fetchStarted = true;
                string escaped = mediaUrl.Replace("\\", "\\\\").Replace("'", "\\'");
                // v.redd.it uses ACAO:* → credentials must be omit. packaged-media often needs cookies.
                bool packaged = mediaUrl.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0;
                string primaryCreds = packaged ? "include" : "omit";
                string fallbackCreds = packaged ? "omit" : "include";
                string js =
                    "(async function(){"
                    + "try{"
                    + "var url='" + escaped + "';"
                    + "function b64(u8){var s='';for(var i=0;i<u8.length;i++)s+=String.fromCharCode(u8[i]);return btoa(s);}"
                    + "function get(c){return fetch(url,{credentials:c,mode:'cors',headers:{"
                    + "'Referer':'https://www.reddit.com/','Origin':'https://www.reddit.com','Accept':'*/*'}});}"
                    + "var r=null;"
                    + "try{r=await get('" + primaryCreds + "');}catch(e1){r=null;}"
                    + "if(!r||!r.ok){try{r=await get('" + fallbackCreds + "');}catch(e2){r=null;}}"
                    + "if(!r||!r.ok){AnoteReddit.fail('HTTP '+(r&&r.status?r.status:'fetch'));return;}"
                    + "var total=Number(r.headers.get('content-length')||0);"
                    + "AnoteReddit.begin('', total);"
                    + "var reader=r.body.getReader();"
                    + "while(true){var n=await reader.read();if(n.done)break;AnoteReddit.chunk(b64(n.value));}"
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

            /// <summary>Force-complete pick using the best URL seen so far (timeout recovery).</summary>
            public void ForceFinishPick()
            {
                FinishPick();
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

            public override void OnPageStarted(AWebView view, string url, global::Android.Graphics.Bitmap favicon)
            {
                base.OnPageStarted(view, url, favicon);
                // Arm as soon as we land on the comments URL so early CDN hits are kept.
                if (_pickStarted || view == null || _mediaArmed)
                    return;
                string current = url ?? string.Empty;
                RememberCommentsUrl(current);
                if (IsTargetPostUrl(current))
                    ArmMedia(view, current);
            }

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                if (_pickStarted || view == null)
                    return;

                string current = url ?? string.Empty;
                RememberCommentsUrl(current);

                if (!IsTargetPostUrl(current))
                {
                    bool onShare = YtDlpUrlHelper.IsRedditShareUrl(current)
                        || (_startedFromShare && current.IndexOf("/comments/", StringComparison.OrdinalIgnoreCase) < 0);

                    if (onShare)
                    {
                        // Share links redirect to /comments/{id}/ in Chromium — wait; do NOT
                        // reload the /s/ URL (that fights the redirect and never arms media).
                        AnoteFileLog.Append(
                            "ytdlp",
                            "Reddit share redirect pending (" + Truncate(current, 80) + ")");
                        if (!_shareReloadScheduled)
                        {
                            _shareReloadScheduled = true;
                            new Handler(Looper.MainLooper).PostDelayed(() =>
                            {
                                if (_pickStarted || _mediaArmed || view == null)
                                    return;
                                try
                                {
                                    string now = view.Url ?? string.Empty;
                                    RememberCommentsUrl(now);
                                    if (IsTargetPostUrl(now))
                                    {
                                        OnPageFinished(view, now);
                                        return;
                                    }

                                    // Still stuck on /s/ — ask JS for final location once.
                                    view.EvaluateJavascript(
                                        "(function(){return document.location.href;})()",
                                        new RedditJsCallback(result =>
                                        {
                                            string href = UnwrapJsString(result);
                                            RememberCommentsUrl(href);
                                            if (!string.IsNullOrEmpty(href) && IsTargetPostUrl(href))
                                            {
                                                try { view.LoadUrl(href); } catch (Exception) { }
                                            }
                                        }));
                                }
                                catch (Exception)
                                {
                                }
                            }, 2500);
                        }

                        return;
                    }

                    // Redirected away from the post (login/challenge) — try hard navigation once.
                    // Never reload a share URL here.
                    if (!string.IsNullOrEmpty(_pageUrl)
                        && !YtDlpUrlHelper.IsRedditShareUrl(_pageUrl)
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

                ArmMedia(view, current);
            }

            private void ArmMedia(AWebView view, string current)
            {
                if (_mediaArmed || _pickStarted || view == null)
                    return;

                _mediaArmed = true;
                AnoteFileLog.Append(
                    "ytdlp",
                    "Reddit media armed for post"
                    + (string.IsNullOrEmpty(_postId) ? string.Empty : " id=" + _postId)
                    + (string.IsNullOrEmpty(ResolvedCommentsUrl) ? string.Empty : " @ " + ResolvedCommentsUrl)
                    + " preArm=" + _preArmHits.Count);
                try
                {
                    view.EvaluateJavascript(
                        "window.AnoteRedditPick=function(u){if(u){AnoteReddit&&AnoteReddit.onMedia&&AnoteReddit.onMedia(u);}};",
                        null);
                }
                catch (Exception) { }

                // Replay CDN hits that arrived before arming.
                if (_preArmHits.Count > 0)
                {
                    string[] early = _preArmHits.ToArray();
                    _preArmHits.Clear();
                    foreach (string hit in early)
                        OfferCandidate(hit, fromJson: false);
                }

                ScheduleForcePlayScan(view, 400);
                ScheduleForcePlayScan(view, 1500);
                ScheduleForcePlayScan(view, 3500);
                ScheduleForcePlayScan(view, 8000);
                ScheduleExtract(view, 700);
                ScheduleExtract(view, 2000);
                ScheduleExtract(view, 4500);
                ScheduleExtract(view, 9000);
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_pickStarted || view == null)
                        return;
                    try { view.EvaluateJavascript(FetchJsonJs, null); } catch (Exception) { }
                }, 900);
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (_pickStarted || view == null)
                        return;
                    try { view.EvaluateJavascript(FetchJsonJs, null); } catch (Exception) { }
                }, 5000);
            }

            private const string ForcePlayScanJs =
                "(function(){"
                + "try{"
                + "function poke(el){try{"
                + "if(!el)return;"
                + "if(el.tagName==='VIDEO'){el.muted=true;el.playsInline=true;el.play();return;}"
                + "el.click&&el.click();"
                + "var v=el.querySelector&&el.querySelector('video');"
                + "if(v){v.muted=true;v.playsInline=true;v.play();}"
                + "if(el.shadowRoot){var sv=el.shadowRoot.querySelector('video');"
                + "if(sv){sv.muted=true;sv.play();} var btn=el.shadowRoot.querySelector('button,[role=button],.play-button');"
                + "if(btn&&btn.click)btn.click();}"
                + "}catch(e){}}"
                + "document.querySelectorAll('video,shreddit-player,shreddit-player-2,[data-testid*=play]').forEach(poke);"
                + "var hits=[];"
                + "performance.getEntriesByType('resource').forEach(function(e){"
                + "if(e&&e.name&&(e.name.indexOf('v.redd.it')>=0||e.name.indexOf('packaged-media.redd.it')>=0||e.name.indexOf('.m3u8')>=0||e.name.indexOf('.mpd')>=0)) hits.push(e.name);"
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
                                OfferCandidate(line, fromJson: false, fromPage: true);
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
                            if (string.IsNullOrWhiteSpace(media))
                            {
                                if (_extractAttempts == 1 || _extractAttempts == 4)
                                {
                                    AnoteFileLog.Append(
                                        "ytdlp",
                                        "Reddit JS extract empty (attempt " + _extractAttempts + ")");
                                }

                                return;
                            }

                            string[] lines = media.Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            AnoteFileLog.Append(
                                "ytdlp",
                                "Reddit JS extract hits=" + lines.Length
                                + " attempt=" + _extractAttempts
                                + " sample=" + Truncate(lines[0], 120));
                            int accepted = 0;
                            foreach (string line in lines)
                            {
                                int before = _bestRank;
                                OfferCandidate(line, fromJson: false, fromPage: true);
                                if (_bestRank > before)
                                    accepted++;
                            }

                            if (accepted == 0 && lines.Length > 0)
                            {
                                AnoteFileLog.Append(
                                    "ytdlp",
                                    "Reddit extract produced no accepted media; raw0="
                                    + Truncate(lines[0], 160));
                                // Last resort: take first URL that merely looks like media.
                                foreach (string line in lines)
                                {
                                    if (!IsRedditMediaUrl(line))
                                        continue;
                                    _bestUrl = PreferProgressiveRedditUrl(line);
                                    _bestRank = Math.Max(_bestRank, 1);
                                    AnoteFileLog.Append(
                                        "ytdlp",
                                        "Reddit force-candidate " + Truncate(_bestUrl, 140));
                                    FinishPick();
                                    break;
                                }
                            }
                            else if (!_pickStarted && !string.IsNullOrEmpty(_bestUrl))
                            {
                                FinishPick();
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
                if (request?.IsForMainFrame == true && _mediaArmed && !_pickStarted)
                {
                    AnoteFileLog.Append("ytdlp", "Reddit WebView main-frame error: " + error?.Description);
                    _onFatal?.Invoke();
                }
            }

            public void OfferCandidate(string rawUrl, bool fromJson = false, bool fromPage = false)
            {
                if (_pickStarted || string.IsNullOrEmpty(rawUrl) || !IsRedditMediaUrl(rawUrl))
                    return;

                // Buffer CDN hits that race ahead of OnPageFinished arming.
                if (!_mediaArmed)
                {
                    if (_preArmHits.Count < 32
                        && !_preArmHits.Contains(rawUrl, StringComparer.OrdinalIgnoreCase))
                        _preArmHits.Add(rawUrl);
                    return;
                }

                string url = PreferProgressiveRedditUrl(rawUrl);
                // We no longer warm the homepage, so packaged-media on an armed comments page
                // is almost always this post's player (modern shreddit often has no v.redd.it).
                // Still prefer JSON / page-extract over incidental network prefetch.
                bool packaged = url.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0;
                if (packaged && !fromJson && !fromPage)
                {
                    // Keep as low-priority network candidate; do not drop.
                }

                int rank = RankRedditMediaUrl(url);
                if (rank < 0)
                    return;

                // Post JSON / in-page scrape is more trustworthy than raw network prefetch.
                if (fromJson)
                    rank += 100;
                else if (fromPage)
                    rank += 40;

                if (rank > _bestRank)
                {
                    _bestRank = rank;
                    _bestUrl = url;
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Reddit candidate rank=" + rank
                        + (fromJson ? " json" : fromPage ? " page" : " net")
                        + " " + Truncate(url, 140));
                }

                // Trusted sources: pick immediately. Else short debounce.
                if (fromJson || fromPage || rank >= 150)
                {
                    FinishPick();
                    return;
                }

                if (!_debounceScheduled)
                {
                    _debounceScheduled = true;
                    new Handler(Looper.MainLooper).PostDelayed(FinishPick, 1200);
                }
            }

            private void FinishPick()
            {
                if (_pickStarted)
                    return;
                if (string.IsNullOrEmpty(_bestUrl))
                    return;
                _pickStarted = true;
                LastPickedMediaUrl = _bestUrl;
                try { ACookieManager.Instance?.Flush(); } catch (Exception) { }
                _onPicked?.Invoke(_bestUrl);
            }

            private static bool IsRedditMediaUrl(string url)
            {
                if (string.IsNullOrEmpty(url))
                    return false;
                if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return false;
                // Exclude preview thumbnails, icons, styles, scripts
                if (url.IndexOf("/preview.", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("/avatar", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("/styles/", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                return url.IndexOf("v.redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("packaged-media.redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("reddit.com/media", StringComparison.OrdinalIgnoreCase) >= 0
                    || (url.IndexOf("redd.it", StringComparison.OrdinalIgnoreCase) >= 0
                        && (url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                            || url.IndexOf(".mpd", StringComparison.OrdinalIgnoreCase) >= 0
                            || url.IndexOf(".mp4", StringComparison.OrdinalIgnoreCase) >= 0));
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
            if (d.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("[Errno 111]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return " — Connection refused (often a dead SOCKS5 proxy). Check Settings → SOCKS5 host/port is reachable, then retry.";
            }

            if (YtDlpUrlHelper.IsRedditShareUrl(url)
                || (YtDlpUrlHelper.IsRedditUrl(url)
                    && d.IndexOf("[generic]", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return " — Reddit Share links (/s/…) are not downloadable directly; use Share → Copy link on the post itself (…/comments/…), keep SOCKS5 on, and retry.";
            }

            if (YtDlpUrlHelper.IsRedditUrl(url)
                && (d.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("unable to access the Reddit API", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("Your IP address", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("Blocked", StringComparison.OrdinalIgnoreCase) >= 0
                    || d.IndexOf("impersonat", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return " — Reddit JSON API blocked (SOCKS alone is not enough). Open the post in Abro while logged in so cookies/media load, then retry the same /comments/… link.";
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
