// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SilentNotes.Workers
{
    /// <summary>
    /// VirusTotal API v3 URL reputation checks for URLCheck.
    /// </summary>
    public static class VirusTotalUrlChecker
    {
        private const string BaseUrl = "https://www.virustotal.com/api/v3/";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private const int MaxPollAttempts = 15;
        private static readonly TimeSpan StaleReportThreshold = TimeSpan.FromDays(7);

        /// <summary>
        /// Submits or retrieves a VirusTotal report for the given URL.
        /// </summary>
        public static Task<VirusTotalUrlCheckResult> CheckUrlAsync(string url, string apiKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Task.FromResult(VirusTotalUrlCheckResult.Error("URL is empty"));
            if (string.IsNullOrWhiteSpace(apiKey))
                return Task.FromResult(VirusTotalUrlCheckResult.Error("API key is missing"));

            return CheckUrlCoreAsync(url.Trim(), apiKey.Trim(), cancellationToken);
        }

        private static async Task<VirusTotalUrlCheckResult> CheckUrlCoreAsync(string url, string apiKey, CancellationToken cancellationToken)
        {
            using var client = CreateClient(apiKey);
            string urlId = ToUrlId(url);

            try
            {
                using HttpResponseMessage getResponse = await client.GetAsync("urls/" + urlId, cancellationToken).ConfigureAwait(false);
                if (getResponse.IsSuccessStatusCode)
                {
                    string json = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (TryParseStats(json, out VirusTotalUrlCheckResult parsed)
                        && !IsReportStale(json))
                    {
                        return parsed;
                    }
                }
                else if (getResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    return VirusTotalUrlCheckResult.Error(await ReadErrorAsync(getResponse, cancellationToken).ConfigureAwait(false));
                }

                using HttpResponseMessage postResponse = await PostUrlAsync(client, url, cancellationToken).ConfigureAwait(false);
                if (!postResponse.IsSuccessStatusCode)
                {
                    return VirusTotalUrlCheckResult.Error(await ReadErrorAsync(postResponse, cancellationToken).ConfigureAwait(false));
                }

                string postJson = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string analysisId = TryReadAnalysisId(postJson);
                if (!string.IsNullOrEmpty(analysisId))
                {
                    VirusTotalUrlCheckResult polled = await PollAnalysisAsync(client, analysisId, cancellationToken).ConfigureAwait(false);
                    if (polled != null)
                        return polled;
                }

                using HttpResponseMessage refetch = await client.GetAsync("urls/" + urlId, cancellationToken).ConfigureAwait(false);
                if (refetch.IsSuccessStatusCode)
                {
                    string refetchJson = await refetch.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (TryParseStats(refetchJson, out VirusTotalUrlCheckResult refetched))
                        return refetched;
                }

                return VirusTotalUrlCheckResult.Error("Could not read VirusTotal report");
            }
            catch (Exception ex)
            {
                return VirusTotalUrlCheckResult.Error(ex.Message);
            }
        }

        private static HttpClient CreateClient(string apiKey)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(45),
            };
            client.DefaultRequestHeaders.Add("x-apikey", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static async Task<HttpResponseMessage> PostUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("url", url) });
            return await client.PostAsync("urls", content, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<VirusTotalUrlCheckResult> PollAnalysisAsync(HttpClient client, string analysisId, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using HttpResponseMessage response = await client.GetAsync("analyses/" + analysisId, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string status = TryReadAnalysisStatus(json);
                if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseStats(json, out VirusTotalUrlCheckResult result))
                        return result;
                    return null;
                }

                if (!string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(status, "in-progress", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(status))
                {
                    return null;
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private static string ToUrlId(string url)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(url);
            string b64 = Convert.ToBase64String(bytes);
            return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool IsReportStale(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
                    return true;
                if (!data.TryGetProperty("attributes", out JsonElement attributes))
                    return true;
                if (!attributes.TryGetProperty("last_analysis_date", out JsonElement dateProp))
                    return true;
                if (dateProp.ValueKind != JsonValueKind.Number)
                    return true;

                long unix = dateProp.GetInt64();
                var analyzedAt = DateTimeOffset.FromUnixTimeSeconds(unix);
                return DateTimeOffset.UtcNow - analyzedAt > StaleReportThreshold;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string TryReadAnalysisId(string postJson)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(postJson);
                if (doc.RootElement.TryGetProperty("data", out JsonElement data)
                    && data.TryGetProperty("id", out JsonElement id))
                {
                    return id.GetString();
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static string TryReadAnalysisStatus(string analysisJson)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(analysisJson);
                if (doc.RootElement.TryGetProperty("data", out JsonElement data)
                    && data.TryGetProperty("attributes", out JsonElement attributes)
                    && attributes.TryGetProperty("status", out JsonElement status))
                {
                    return status.GetString();
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static bool TryParseStats(string json, out VirusTotalUrlCheckResult result)
        {
            result = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
                    return false;
                if (!data.TryGetProperty("attributes", out JsonElement attributes))
                    return false;
                if (!attributes.TryGetProperty("last_analysis_stats", out JsonElement stats)
                    && !attributes.TryGetProperty("stats", out stats))
                {
                    return false;
                }

                int malicious = ReadStat(stats, "malicious");
                int suspicious = ReadStat(stats, "suspicious");
                int harmless = ReadStat(stats, "harmless");
                int undetected = ReadStat(stats, "undetected");

                string summary = BuildSummary(malicious, suspicious, harmless, undetected);
                result = new VirusTotalUrlCheckResult
                {
                    MaliciousCount = malicious,
                    SuspiciousCount = suspicious,
                    HarmlessCount = harmless,
                    UndetectedCount = undetected,
                    Summary = summary,
                };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int ReadStat(JsonElement stats, string name)
        {
            if (stats.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
                return value.GetInt32();
            return 0;
        }

        private static string BuildSummary(int malicious, int suspicious, int harmless, int undetected)
        {
            if (malicious > 0)
                return $"{malicious} malicious, {suspicious} suspicious, {harmless} harmless, {undetected} undetected";
            if (suspicious > 0)
                return $"{suspicious} suspicious, {harmless} harmless, {undetected} undetected";
            return $"{harmless} harmless, {undetected} undetected (0 malicious)";
        }

        private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out JsonElement error)
                        && error.TryGetProperty("message", out JsonElement message))
                    {
                        return message.GetString() ?? response.ReasonPhrase;
                    }
                }
            }
            catch (Exception)
            {
            }

            return response.ReasonPhrase ?? "VirusTotal request failed";
        }
    }

    /// <summary>
    /// Result of a VirusTotal URL scan.
    /// </summary>
    public sealed class VirusTotalUrlCheckResult
    {
        public int MaliciousCount { get; set; }

        public int SuspiciousCount { get; set; }

        public int HarmlessCount { get; set; }

        public int UndetectedCount { get; set; }

        public string Summary { get; set; }

        public string ErrorMessage { get; set; }

        public static VirusTotalUrlCheckResult Error(string message)
        {
            return new VirusTotalUrlCheckResult { ErrorMessage = message };
        }
    }
}
