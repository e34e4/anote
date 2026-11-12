// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Webkit;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

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
                string direct = await YtDlpUrlHelper.TryResolveVkDirectMediaUrlAsync(originalUrl, cancellationToken)
                    .ConfigureAwait(false);
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

        private string TryWriteWebViewCookiesFile(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    return null;

                CookieManager manager = CookieManager.Instance;
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
