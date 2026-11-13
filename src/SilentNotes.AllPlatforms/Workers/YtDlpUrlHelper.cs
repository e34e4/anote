// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Site-specific URL rewrites and pre-resolvers for yt-dlp on restricted networks.
    /// </summary>
    public static class YtDlpUrlHelper
    {
        public const string BrowserUserAgent =
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";

        private static readonly Regex VimeoIdRegex = new Regex(
            @"^https?://(?:(?:www|player)\.)?vimeo\.com/(?:video/)?(?<id>\d+)(?:/(?<hash>[0-9a-f]+))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex VkVideoIdRegex = new Regex(
            @"^https?://(?:(?:m|new|www|vksport)\.)?vk(?:video)?\.(?:ru|com)/(?:video|clip)(?<id>-?\d+_\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex VkZParamRegex = new Regex(
            @"[?&]z=(?:video|clip)(?<id>-?\d+_\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SourceSrcRegex = new Regex(
            @"<source\s+[^>]*src\s*=\s*""(?<url>[^""]+)""[^>]*(?:type\s*=\s*""(?<type>[^""]+)"")?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SourceTypeFirstRegex = new Regex(
            @"<source\s+[^>]*type\s*=\s*""(?<type>[^""]+)""[^>]*src\s*=\s*""(?<url>[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RedditCommentsIdRegex = new Regex(
            @"/(?:r|user)/[^/]+/comments/(?<id>[a-z0-9]+)|/comments/(?<id>[a-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RedditSharePathRegex = new Regex(
            @"^/(?:r|user)/[^/]+/s/[A-Za-z0-9]+/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Light synchronous rewrites (Vimeo → embed player). VK stays on vkvideo.ru —
        /// vk.com is often DNS-sinkholed to 0.0.0.0 (errno 111).
        /// Reddit <c>/s/</c> share links stay as-is here — they must be resolved via WebView
        /// (curl/HttpClient get HTTP 403).
        /// </summary>
        public static string NormalizeDownloadUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return url;

            string host = uri.Host ?? string.Empty;
            if (host.EndsWith("vimeo.com", StringComparison.OrdinalIgnoreCase))
                return RewriteVimeoToPlayer(url, uri);

            // redd.it/<id> → comments permalink (yt-dlp Reddit extractor accepts this).
            if (host.Equals("redd.it", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.redd.it", StringComparison.OrdinalIgnoreCase))
            {
                string id = (uri.AbsolutePath ?? string.Empty).Trim('/');
                if (id.Length > 0 && id.IndexOf('/') < 0)
                    return "https://www.reddit.com/comments/" + id + "/";
            }

            return url;
        }

        /// <summary>
        /// For vk/vkvideo URLs, resolve a direct mp4/m3u8 via vkvideo.ru/al_video.php so yt-dlp
        /// never needs the sinkholed vk.com AJAX host.
        /// </summary>
        public static async Task<string> TryResolveVkDirectMediaUrlAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseVkVideoId(url, out string videoId))
                return null;

            try
            {
                using var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://vkvideo.ru/");
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://vkvideo.ru");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");

                using var content = new ByteArrayContent(
                    Encoding.ASCII.GetBytes("al=1&act=show&video=" + Uri.EscapeDataString(videoId)));
                content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

                using HttpResponseMessage response = await client
                    .PostAsync("https://vkvideo.ru/al_video.php", content, cancellationToken)
                    .ConfigureAwait(false);
                byte[] raw = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (raw == null || raw.Length == 0)
                    return null;

                string body = DecodeVkBody(raw);
                string media = PickBestVkSource(body);
                if (media == null)
                {
                    // Fallback: response sometimes arrives as windows-1251.
                    try
                    {
                        body = Encoding.GetEncoding(1251).GetString(raw);
                        media = PickBestVkSource(body);
                    }
                    catch (Exception)
                    {
                    }
                }

                return media;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Decode VK AJAX body; URLs are ASCII so Latin-1 is a safe fallback.</summary>
        public static string DecodeVkBody(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
                return string.Empty;
            string utf8 = Encoding.UTF8.GetString(raw);
            if (utf8.IndexOf("okcdn", StringComparison.OrdinalIgnoreCase) >= 0
                || utf8.IndexOf("<source", StringComparison.OrdinalIgnoreCase) >= 0)
                return utf8;
            return Encoding.Latin1.GetString(raw);
        }

        /// <summary>True when the URL looks like Reddit.</summary>
        public static bool IsRedditUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            string host = uri.Host ?? string.Empty;
            return host.EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("redd.it", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".redd.it", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True for Reddit Share sheet links (<c>/r/sub/s/CODE</c>). yt-dlp has no extractor for
        /// these and falls back to <c>[generic]</c> → HTTP 403; resolve to a comments permalink first.
        /// </summary>
        public static bool IsRedditShareUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            string host = uri.Host ?? string.Empty;
            if (!host.EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase))
                return false;
            return RedditSharePathRegex.IsMatch(uri.AbsolutePath ?? string.Empty);
        }

        /// <summary>True when the URL is a post comments permalink yt-dlp's Reddit extractor accepts.</summary>
        public static bool IsRedditCommentsUrl(string url)
        {
            return TryExtractRedditPostId(url, out _);
        }

        /// <summary>Extracts the post id from a comments permalink (not from <c>/s/</c> share codes).</summary>
        public static bool TryExtractRedditPostId(string url, out string postId)
        {
            postId = null;
            if (string.IsNullOrWhiteSpace(url))
                return false;
            Match m = RedditCommentsIdRegex.Match(url);
            if (!m.Success)
                return false;
            postId = m.Groups["id"].Value;
            return !string.IsNullOrEmpty(postId);
        }

        /// <summary>Builds a canonical comments URL for yt-dlp when only a post id is known.</summary>
        public static string BuildRedditCommentsUrl(string postId)
        {
            if (string.IsNullOrWhiteSpace(postId))
                return null;
            return "https://www.reddit.com/comments/" + postId.Trim() + "/";
        }

        /// <summary>True when the URL looks like Vimeo.</summary>
        public static bool IsVimeoUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            return (uri.Host ?? string.Empty).EndsWith("vimeo.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the URL looks like VK / VK Video.</summary>
        public static bool IsVkUrl(string url)
        {
            return TryParseVkVideoId(url, out _);
        }

        /// <summary>Builds a yt-dlp <c>--proxy</c> value for SOCKS5 settings.</summary>
        public static string BuildSocks5ProxyArgument(string host, int port, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
                return null;

            // Prefer socks5h so DNS is resolved by the proxy (important when local DNS sinkholes sites).
            // yt-dlp treats socks5 as socks5h anyway; curl and other tools do not.
            var builder = new StringBuilder("socks5h://");
            if (!string.IsNullOrEmpty(username))
            {
                builder.Append(Uri.EscapeDataString(username));
                builder.Append(':');
                builder.Append(Uri.EscapeDataString(password ?? string.Empty));
                builder.Append('@');
            }

            builder.Append(host.Trim());
            builder.Append(':');
            builder.Append(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        /// <summary>
        /// Quick TCP reachability check for a SOCKS5 host:port (does not validate credentials).
        /// </summary>
        public static bool TryProbeSocks5Tcp(string host, int port, int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
                return false;
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connect = client.ConnectAsync(host.Trim(), port);
                if (!connect.Wait(timeoutMs))
                    return false;
                return client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes a Netscape cookies.txt from <c>name=value; name2=value2</c> WebView cookie strings.
        /// </summary>
        public static void WriteNetscapeCookiesFile(
            string path,
            IEnumerable<(string Domain, string CookieHeader)> cookieHeaders)
        {
            if (string.IsNullOrEmpty(path) || cookieHeaders == null)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("# Netscape HTTP Cookie File");
            sb.AppendLine("# Generated by aNote for yt-dlp");

            foreach ((string domain, string header) in cookieHeaders)
            {
                if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(header))
                    continue;

                string cookieDomain = domain.StartsWith(".", StringComparison.Ordinal)
                    ? domain
                    : "." + domain.TrimStart('.');

                foreach (string part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string name = part.Substring(0, eq).Trim();
                    string value = part.Substring(eq + 1).Trim();
                    if (name.Length == 0 || name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // domain, flag, path, secure, expiry, name, value
                    sb.Append(cookieDomain);
                    sb.Append("\tTRUE\t/\tTRUE\t0\t");
                    sb.Append(name);
                    sb.Append('\t');
                    sb.Append(value);
                    sb.AppendLine();
                }
            }

            // No BOM — yt-dlp rejects UTF-8 BOM as an invalid Netscape cookies file.
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string RewriteVimeoToPlayer(string originalUrl, Uri uri)
        {
            Match m = VimeoIdRegex.Match(originalUrl);
            if (!m.Success)
                return originalUrl;

            string id = m.Groups["id"].Value;
            string hash = m.Groups["hash"].Success ? m.Groups["hash"].Value : null;

            // Unlisted videos use /ID/HASH on vimeo.com → player ?h=HASH
            if (string.IsNullOrEmpty(hash))
                hash = GetQueryValue(uri.Query, "h");

            string player = "https://player.vimeo.com/video/" + id;
            if (!string.IsNullOrEmpty(hash))
                player += "?h=" + Uri.EscapeDataString(hash);
            return player;
        }

        private static string GetQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(key))
                return null;
            string q = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            foreach (string part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string name = eq >= 0 ? part.Substring(0, eq) : part;
                if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1)) : string.Empty;
            }

            return null;
        }

        public static bool TryParseVkVideoId(string url, out string videoId)
        {
            videoId = null;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            Match m = VkVideoIdRegex.Match(url.Trim());
            if (m.Success)
            {
                videoId = m.Groups["id"].Value;
                return true;
            }

            m = VkZParamRegex.Match(url);
            if (m.Success)
            {
                videoId = m.Groups["id"].Value;
                return true;
            }

            return false;
        }

        public static string PickBestVkSource(string htmlOrJson)
        {
            if (string.IsNullOrEmpty(htmlOrJson))
                return null;

            // al_video.php returns JSON with escaped HTML (\" and \/).
            string decoded = htmlOrJson
                .Replace("\\/", "/")
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("&amp;", "&");

            string bestMp4 = null;
            string bestHls = null;

            void Consider(string rawUrl, string type)
            {
                if (string.IsNullOrWhiteSpace(rawUrl))
                    return;
                string u = WebUtility.HtmlDecode(rawUrl.Trim());
                if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return;

                bool isHls = (type != null && type.IndexOf("mpegurl", StringComparison.OrdinalIgnoreCase) >= 0)
                    || u.IndexOf("m3u8", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHls)
                {
                    bestHls ??= u;
                    return;
                }

                if (type != null && type.IndexOf("mp4", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Prefer later sources (VK lists ascending quality).
                    bestMp4 = u;
                }
                else if (bestMp4 == null && u.IndexOf("okcdn.ru", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bestMp4 = u;
                }
            }

            foreach (Match m in SourceTypeFirstRegex.Matches(decoded))
                Consider(m.Groups["url"].Value, m.Groups["type"].Value);
            foreach (Match m in SourceSrcRegex.Matches(decoded))
                Consider(m.Groups["url"].Value, m.Groups["type"].Value);

            if (bestMp4 == null && bestHls == null)
            {
                // Escaped JSON sometimes leaves URLs without a clean <source> tag after decode.
                foreach (Match m in Regex.Matches(
                    decoded,
                    @"https://[^\s""'<>\\]+okcdn\.ru[^\s""'<>\\]+",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    Consider(m.Value.TrimEnd('\\', '"', '\'', ','), null);
                }
            }

            // Prefer progressive mp4 (no ffmpeg merge needed); fall back to HLS.
            return bestMp4 ?? bestHls;
        }
    }
}
