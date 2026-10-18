// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SilentNotes.Workers
{
    /// <summary>
    /// URLCheck-style utilities for inspecting URLs, stripping tracking query parameters, and
    /// unshortening redirect links.
    /// </summary>
    public static class UrlCheckUtils
    {
        private static readonly HashSet<string> TrackingParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_name",
            "utm_cid", "utm_reader", "utm_viz_id", "utm_pubreferrer", "utm_swu",
            "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid", "twclid", "ttclid",
            "yclid", "ymclid", "ysclid", "_openstat",
            "igshid", "igsh", "mc_cid", "mc_eid",
            "_hsenc", "_hsmi", "__hssc", "__hstc", "__hsfp", "hsCtaTracking",
            "mkt_tok", "oly_enc_id", "oly_anon_id", "vero_id", "vero_conv",
            "rb_clickid", "s_kwcid", "wickedid",
            "si", "feature", "ref", "ref_src", "ref_url", "spm", "scm",
        };

        /// <summary>
        /// Counts how many known tracking parameters are present in the URL query string.
        /// </summary>
        public static int CountTrackingParams(string url)
        {
            CleanTrackingParams(url, out int removedCount);
            return removedCount;
        }

        /// <summary>
        /// Removes known tracking query parameters (such as utm_*, fbclid, gclid, yclid, igshid, si)
        /// from a URL while preserving non-tracking query parameters and fragments.
        /// </summary>
        public static string CleanTrackingParams(string url, out int removedCount)
        {
            removedCount = 0;
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
                return url;

            string query = uri.Query;
            if (string.IsNullOrEmpty(query) || query == "?")
                return url;

            string rawQuery = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            string[] parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var keptParts = new List<string>();

            foreach (string part in parts)
            {
                int eqIdx = part.IndexOf('=');
                string rawKey = eqIdx >= 0 ? part.Substring(0, eqIdx) : part;
                string key = Uri.UnescapeDataString(rawKey);

                if (IsTrackingParameter(key))
                {
                    removedCount++;
                }
                else
                {
                    keptParts.Add(part);
                }
            }

            if (removedCount == 0)
                return url;

            var builder = new UriBuilder(uri)
            {
                Query = keptParts.Count > 0 ? string.Join("&", keptParts) : string.Empty,
            };
            return builder.Uri.AbsoluteUri;
        }

        private static bool IsTrackingParameter(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (TrackingParamNames.Contains(key))
                return true;
            if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("pk_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("mtm_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("matomo_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("hsa_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Upgrades an http:// URL to https://.
        /// </summary>
        public static string UpgradeToHttps(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;
            string trimmed = url.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("http://".Length);
            return trimmed;
        }

        /// <summary>
        /// Follows HTTP redirects step-by-step to reveal the final destination of a shortened URL.
        /// </summary>
        public static async Task<string> UnshortenUrlAsync(string url, int maxHops = 8)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            string current = url.Trim();
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
            };

            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");

                for (int hop = 0; hop < maxHops; hop++)
                {
                    if (!Uri.TryCreate(current, UriKind.Absolute, out Uri currentUri))
                        break;

                    HttpResponseMessage response = null;
                    try
                    {
                        using var headReq = new HttpRequestMessage(HttpMethod.Head, currentUri);
                        response = await client.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);
                        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                            response.StatusCode == HttpStatusCode.BadRequest)
                        {
                            response.Dispose();
                            using var getReq = new HttpRequestMessage(HttpMethod.Get, currentUri);
                            response = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
                        }

                        int code = (int)response.StatusCode;
                        if (code >= 300 && code < 400 && response.Headers.Location != null)
                        {
                            Uri nextUri = response.Headers.Location.IsAbsoluteUri
                                ? response.Headers.Location
                                : new Uri(currentUri, response.Headers.Location);
                            current = nextUri.AbsoluteUri;
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        response?.Dispose();
                    }
                    break;
                }
            }
            return current;
        }

        /// <summary>
        /// Inspects a URL and builds a short security/domain summary string.
        /// </summary>
        public static string BuildDomainSummary(string url, out bool hasWarning)
        {
            hasWarning = false;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
            {
                hasWarning = true;
                return "Invalid URL";
            }

            var warnings = new List<string>();
            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (!isHttps)
            {
                hasWarning = true;
                warnings.Add("Unencrypted HTTP");
            }

            string host = uri.IdnHost ?? uri.Host ?? string.Empty;
            if (host.Contains("xn--", StringComparison.OrdinalIgnoreCase))
            {
                hasWarning = true;
                warnings.Add("Punycode / IDN domain (" + host + ")");
            }

            if (IPAddress.TryParse(uri.Host, out _))
            {
                hasWarning = true;
                warnings.Add("Direct IP address");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                hasWarning = true;
                warnings.Add("Contains userinfo (@)");
            }

            string prefix = isHttps ? "🔒 " : "⚠️ ";
            string summary = prefix + uri.Host;
            if (warnings.Count > 0)
                summary += " · " + string.Join(" · ", warnings);
            return summary;
        }
    }
}
