// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Cross-platform implementation of <see cref="IPhoneSpamService"/> providing:
    /// 1. Wildcard/mask blocklist (<c>+7903*</c>, <c>8903*</c>, <c>*1234</c>, <c>?</c>).
    /// 2. AGContacts / Чей номер+ (<c>callerid.mbagc.ru</c>) offline database sync (LZMA + Protobuf
    ///    shards and accumulative deltas) plus Russian spam feeds (<c>zvonili.me</c>, <c>whocalls.ru</c>,
    ///    community Russian lists, and custom URL / file imports).
    /// 3. Live Russian AON &amp; MNP lookup (<c>callerid.mbagc.ru</c>, <c>zvonili.me</c>, <c>whocalls.ru</c>,
    ///    <c>voprosoff.net</c>, <c>zvonili.com</c>, <c>callfilter.app</c>, <c>num.voxlink.ru</c>, <c>htmlweb.ru</c>).
    /// </summary>
    internal class PhoneSpamService : IPhoneSpamService
    {
        private const string BlocklistFileName = "phone_blocklist.json";
        private const string SpamDbFileName = "phone_spam_db.json";
        private const string SpamMetaFileName = "phone_spam_meta.json";
        private const string OcidbShardFolderName = "ocidb_shards";

        private const string MbagcBaseUrl = "https://callerid.mbagc.ru/reviews";
        private const string CommunityRuSpamGistUrl =
            "https://gist.githubusercontent.com/SergeyDjam/35f6c82980ee35b7eb00e3133d02486b/raw/c9d14fc78c6b142fd36208efb13f42b787d8a739/phone-spam.txt";

        // Key Russian prefixes to prefetch during bulk update so offline coverage works immediately
        private static readonly string[] DefaultRussianShards = new[]
        {
            "+70", "+71", "+72", "+74950", "+749510", "+74952", "+74957", "+74990", "+74995", "+74999",
            "+780", "+7812", "+790", "+791", "+792", "+793", "+795", "+7960", "+7963", "+7965", "+7968", "+798", "+7999"
        };

        private readonly ISettingsService _settingsService;
        private readonly object _syncRoot = new object();
        private readonly HttpClient _httpClient;

        private List<PhoneBlockRuleModel> _blockRules;
        private Dictionary<string, PhoneSpamEntryModel> _spamEntries;
        private List<string> _knownShardPrefixes = new List<string>();
        private PhoneSpamDatabaseStatus _status;

        public PhoneSpamService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5),
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36");

            EnsureLoaded();
        }

        /// <inheritdoc/>
        public bool IsBlocklistEnabled
        {
            get => _settingsService?.LoadSettingsOrDefault()?.PhoneBlocklistEnabled ?? true;
            set
            {
                SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
                if (settings != null && settings.PhoneBlocklistEnabled != value)
                {
                    settings.PhoneBlocklistEnabled = value;
                    _settingsService.TrySaveSettingsToLocalDevice(settings);
                }
            }
        }

        /// <inheritdoc/>
        public bool AutoBlockSpam
        {
            get => _settingsService?.LoadSettingsOrDefault()?.PhoneAutoBlockSpam ?? false;
            set
            {
                SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
                if (settings != null && settings.PhoneAutoBlockSpam != value)
                {
                    settings.PhoneAutoBlockSpam = value;
                    _settingsService.TrySaveSettingsToLocalDevice(settings);
                }
            }
        }

        /// <inheritdoc/>
        public bool SkipContactsWhenBlocking
        {
            get => _settingsService?.LoadSettingsOrDefault()?.PhoneSkipContactsWhenBlocking ?? true;
            set
            {
                SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
                if (settings != null && settings.PhoneSkipContactsWhenBlocking != value)
                {
                    settings.PhoneSkipContactsWhenBlocking = value;
                    _settingsService.TrySaveSettingsToLocalDevice(settings);
                }
            }
        }

        /// <inheritdoc/>
        public string CustomSpamSourceUrl
        {
            get => _settingsService?.LoadSettingsOrDefault()?.PhoneCustomSpamUrl ?? string.Empty;
            set
            {
                SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
                if (settings != null)
                {
                    settings.PhoneCustomSpamUrl = value?.Trim() ?? string.Empty;
                    _settingsService.TrySaveSettingsToLocalDevice(settings);
                }
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<PhoneBlockRuleModel> GetBlockRules()
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                return _blockRules
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .ToList();
            }
        }

        /// <inheritdoc/>
        public PhoneBlockRuleModel AddBlockRule(string pattern, string label = null)
        {
            string normalized = PhoneBlockRuleModel.NormalizeMask(pattern);
            if (string.IsNullOrEmpty(normalized))
                return null;

            EnsureLoaded();
            lock (_syncRoot)
            {
                PhoneBlockRuleModel existing = _blockRules.FirstOrDefault(
                    r => string.Equals(PhoneBlockRuleModel.NormalizeMask(r.Pattern), normalized, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Pattern = pattern.Trim();
                    if (!string.IsNullOrWhiteSpace(label))
                        existing.Label = label.Trim();
                    existing.IsEnabled = true;
                    SaveBlockRulesUnsafe();
                    return existing;
                }

                var rule = new PhoneBlockRuleModel
                {
                    Id = Guid.NewGuid(),
                    Pattern = pattern.Trim(),
                    Label = label?.Trim() ?? string.Empty,
                    IsEnabled = true,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _blockRules.Insert(0, rule);
                SaveBlockRulesUnsafe();
                return rule;
            }
        }

        /// <inheritdoc/>
        public void SetBlockRuleEnabled(Guid id, bool isEnabled)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                PhoneBlockRuleModel rule = _blockRules.FirstOrDefault(r => r.Id == id);
                if (rule != null)
                {
                    rule.IsEnabled = isEnabled;
                    SaveBlockRulesUnsafe();
                }
            }
        }

        /// <inheritdoc/>
        public void DeleteBlockRule(Guid id)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_blockRules.RemoveAll(r => r.Id == id) > 0)
                    SaveBlockRulesUnsafe();
            }
        }

        /// <inheritdoc/>
        public PhoneBlockRuleModel FindMatchingBlockRule(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return null;

            EnsureLoaded();
            lock (_syncRoot)
            {
                return _blockRules.FirstOrDefault(r => r.IsEnabled && r.Matches(phoneNumber));
            }
        }

        /// <inheritdoc/>
        public PhoneSpamDatabaseStatus GetDatabaseStatus()
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                _status.LocalRecordCount = _spamEntries.Count;
                _status.CachedShardCount = CountCachedShardFiles();
                return new PhoneSpamDatabaseStatus
                {
                    StableVersion = _status.StableVersion,
                    AccumulativeVersion = _status.AccumulativeVersion,
                    RemoteTotalRecords = _status.RemoteTotalRecords,
                    LocalRecordCount = _status.LocalRecordCount,
                    CachedShardCount = _status.CachedShardCount,
                    LastUpdatedUtc = _status.LastUpdatedUtc,
                };
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<PhoneSpamEntryModel> SearchLocalSpamEntries(string query = null, int maxResults = 150)
        {
            EnsureLoaded();
            string trimmed = query?.Trim();
            string digitsQuery = PhoneBlockRuleModel.NormalizeCanonicalDigits(trimmed);

            lock (_syncRoot)
            {
                IEnumerable<PhoneSpamEntryModel> items = _spamEntries.Values;
                if (!string.IsNullOrEmpty(trimmed))
                {
                    bool hasWildcard = trimmed.Contains('*') || trimmed.Contains('?');
                    items = items.Where(e =>
                        (hasWildcard && PhoneBlockRuleModel.MatchesPattern(trimmed, e.Number)) ||
                        (!string.IsNullOrEmpty(digitsQuery) && e.Number.Contains(digitsQuery, StringComparison.Ordinal)) ||
                        e.Number.Contains(trimmed.TrimStart('+'), StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(e.Category) && e.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(e.Title) && e.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(e.Comment) && e.Comment.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(e.Source) && e.Source.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));
                }

                return items
                    .OrderByDescending(e => e.UpdatedAtUtc)
                    .ThenByDescending(e => e.NegativeCount)
                    .Take(maxResults)
                    .ToList();
            }
        }

        /// <inheritdoc/>
        public PhoneSpamEntryModel LookupLocal(string phoneNumber)
        {
            string canonical = PhoneBlockRuleModel.NormalizeCanonicalDigits(phoneNumber);
            if (string.IsNullOrEmpty(canonical))
                return null;

            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_spamEntries.TryGetValue(canonical, out PhoneSpamEntryModel found))
                    return found;
            }

            // Also check cached AGContacts ocidb shard file on disk if present
            PhoneSpamEntryModel shardHit = TryLookupInCachedShard(canonical);
            if (shardHit != null)
            {
                UpsertSpamEntry(shardHit);
                return shardHit;
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task<PhoneSpamEntryModel> LookupOnlineAsync(string phoneNumber)
        {
            string canonical = PhoneBlockRuleModel.NormalizeCanonicalDigits(phoneNumber);
            if (string.IsNullOrEmpty(canonical) || canonical.Length < 5)
                return null;

            PhoneSpamEntryModel local = LookupLocal(canonical);

            // Query Russian AON & MNP services concurrently
            Task<PhoneSpamEntryModel> mbagcTask = QueryMbagcInfo2Async(canonical);
            Task<PhoneSpamEntryModel> mbagcShardTask = QueryOrFetchMbagcShardAsync(canonical);
            Task<PhoneSpamEntryModel> mbagcPageTask = QueryMbagcWebPageAsync(canonical);
            Task<PhoneSpamEntryModel> zvoniliMeTask = QueryZvoniliMeAsync(canonical);
            Task<PhoneSpamEntryModel> whoCallsTask = QueryWhoCallsRuAsync(canonical);
            Task<PhoneSpamEntryModel> voprosoffTask = QueryVoprosoffAsync(canonical);
            Task<PhoneSpamEntryModel> zvoniliComTask = QueryZvoniliComAsync(canonical);
            Task<PhoneSpamEntryModel> callFilterTask = QueryCallFilterAppAsync(canonical);
            Task<(string oper, string region)> mnpTask = QueryRussianMnpAsync(canonical);

            try
            {
                await Task.WhenAll(
                    mbagcTask,
                    mbagcShardTask,
                    mbagcPageTask,
                    zvoniliMeTask,
                    whoCallsTask,
                    voprosoffTask,
                    zvoniliComTask,
                    callFilterTask,
                    mnpTask);
            }
            catch (Exception)
            {
            }

            var candidates = new List<PhoneSpamEntryModel>();
            if (local != null) candidates.Add(local);
            if (mbagcTask.Status == TaskStatus.RanToCompletion && mbagcTask.Result != null) candidates.Add(mbagcTask.Result);
            if (mbagcShardTask.Status == TaskStatus.RanToCompletion && mbagcShardTask.Result != null) candidates.Add(mbagcShardTask.Result);
            if (mbagcPageTask.Status == TaskStatus.RanToCompletion && mbagcPageTask.Result != null) candidates.Add(mbagcPageTask.Result);
            if (zvoniliMeTask.Status == TaskStatus.RanToCompletion && zvoniliMeTask.Result != null) candidates.Add(zvoniliMeTask.Result);
            if (whoCallsTask.Status == TaskStatus.RanToCompletion && whoCallsTask.Result != null) candidates.Add(whoCallsTask.Result);
            if (voprosoffTask.Status == TaskStatus.RanToCompletion && voprosoffTask.Result != null) candidates.Add(voprosoffTask.Result);
            if (zvoniliComTask.Status == TaskStatus.RanToCompletion && zvoniliComTask.Result != null) candidates.Add(zvoniliComTask.Result);
            if (callFilterTask.Status == TaskStatus.RanToCompletion && callFilterTask.Result != null) candidates.Add(callFilterTask.Result);

            (string mnpOperator, string mnpRegion) = mnpTask.Status == TaskStatus.RanToCompletion
                ? mnpTask.Result
                : (null, null);

            if (candidates.Count == 0 && string.IsNullOrEmpty(mnpOperator) && string.IsNullOrEmpty(mnpRegion))
                return null;

            var merged = new PhoneSpamEntryModel
            {
                Number = canonical,
                Rating = PhoneSpamRating.Neutral,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            var sources = new List<string>();
            foreach (PhoneSpamEntryModel c in candidates)
            {
                if (c.Rating == PhoneSpamRating.Negative)
                    merged.Rating = PhoneSpamRating.Negative;
                else if (c.Rating == PhoneSpamRating.Positive && merged.Rating == PhoneSpamRating.Neutral)
                    merged.Rating = PhoneSpamRating.Positive;

                merged.PositiveCount = Math.Max(merged.PositiveCount, c.PositiveCount);
                merged.NegativeCount = Math.Max(merged.NegativeCount, c.NegativeCount);
                merged.NeutralCount = Math.Max(merged.NeutralCount, c.NeutralCount);

                if (string.IsNullOrWhiteSpace(merged.Category) && !string.IsNullOrWhiteSpace(c.Category) &&
                    !string.Equals(c.Category, "Без категории", StringComparison.OrdinalIgnoreCase))
                {
                    merged.Category = c.Category;
                }
                if (string.IsNullOrWhiteSpace(merged.Title) && !string.IsNullOrWhiteSpace(c.Title))
                    merged.Title = c.Title;
                if (string.IsNullOrWhiteSpace(merged.Comment) && !string.IsNullOrWhiteSpace(c.Comment))
                    merged.Comment = c.Comment;
                if (string.IsNullOrWhiteSpace(merged.Operator) && !string.IsNullOrWhiteSpace(c.Operator))
                    merged.Operator = c.Operator;
                if (string.IsNullOrWhiteSpace(merged.Region) && !string.IsNullOrWhiteSpace(c.Region))
                    merged.Region = c.Region;
                if (!string.IsNullOrWhiteSpace(c.Source) && !sources.Contains(c.Source))
                    sources.Add(c.Source);
            }

            if (string.IsNullOrWhiteSpace(merged.Operator) && !string.IsNullOrWhiteSpace(mnpOperator))
            {
                merged.Operator = mnpOperator;
                if (!sources.Contains("voxlink/htmlweb"))
                    sources.Add("voxlink/htmlweb");
            }
            if (string.IsNullOrWhiteSpace(merged.Region) && !string.IsNullOrWhiteSpace(mnpRegion))
                merged.Region = mnpRegion;

            merged.Source = string.Join(", ", sources);

            // Cache into local database if it has meaningful category/title/reviews
            if (merged.IsSpam || !string.IsNullOrWhiteSpace(merged.Category) || !string.IsNullOrWhiteSpace(merged.Title))
                UpsertSpamEntry(merged);

            return merged;
        }

        /// <inheritdoc/>
        public async Task<(int totalRecords, int updatedRecords, string statusText)> UpdateSpamDatabaseAsync(IProgress<string> progress = null)
        {
            EnsureLoaded();
            int updated = 0;
            var sourcesReached = new List<string>();

            // 1. AGContacts / Чей номер+ (callerid.mbagc.ru) version & index check
            progress?.Report("callerid.mbagc.ru: checking version…");
            string stableVer = null;
            int remoteTotal = 0;
            try
            {
                byte[] stVerBytes = await _httpClient.GetByteArrayAsync($"{MbagcBaseUrl}/getstableversion");
                ParseMbagcVersionProtobuf(stVerBytes, out stableVer, out int stableNumbers);
                remoteTotal += stableNumbers;

                byte[] accVerBytes = await _httpClient.GetByteArrayAsync($"{MbagcBaseUrl}/getaccumulativeversion");
                ParseMbagcVersionProtobuf(accVerBytes, out string accVer, out int accNumbers);
                remoteTotal = Math.Max(remoteTotal, accNumbers);

                lock (_syncRoot)
                {
                    if (!string.IsNullOrEmpty(stableVer))
                        _status.StableVersion = stableVer;
                    if (!string.IsNullOrEmpty(accVer))
                        _status.AccumulativeVersion = accVer;
                    if (remoteTotal > 0)
                        _status.RemoteTotalRecords = remoteTotal;
                }

                // Fetch accumulative index and download latest 25 Russian delta batches
                progress?.Report($"callerid.mbagc.ru: fetching deltas ({accVer})…");
                byte[] accIdxBytes = await _httpClient.GetByteArrayAsync($"{MbagcBaseUrl}/getaccumulativeindex");
                List<string> deltas = ParseMbagcIndexProtobuf(accIdxBytes);
                int deltaTake = Math.Min(25, deltas.Count);
                for (int i = deltas.Count - deltaTake; i < deltas.Count; i++)
                {
                    string deltaId = deltas[i];
                    try
                    {
                        byte[] lzmaData = await _httpClient.GetByteArrayAsync(
                            $"{MbagcBaseUrl}/getaccumulativefile?file={Uri.EscapeDataString(deltaId)}");
                        if (lzmaData != null && lzmaData.Length >= 13)
                        {
                            byte[] rawPb = LzmaDecoder.Decompress(lzmaData);
                            List<PhoneSpamEntryModel> parsed = ParseMbagcRecordsProtobuf(rawPb, "callerid.mbagc.ru");
                            updated += MergeEntriesBatch(parsed);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                // Fetch stable index & download key Russian shards (+ cache shard files on disk)
                if (!string.IsNullOrEmpty(stableVer))
                {
                    byte[] stIdxBytes = await _httpClient.GetByteArrayAsync($"{MbagcBaseUrl}/getstableindex");
                    List<string> shards = ParseMbagcIndexProtobuf(stIdxBytes);
                    lock (_syncRoot)
                    {
                        _knownShardPrefixes = shards;
                    }

                    string shardDir = GetShardDirectoryPath();
                    Directory.CreateDirectory(shardDir);

                    int shardIdx = 0;
                    foreach (string prefix in DefaultRussianShards)
                    {
                        shardIdx++;
                        progress?.Report($"callerid.mbagc.ru: shard {prefix} ({shardIdx}/{DefaultRussianShards.Length})…");
                        try
                        {
                            string url = $"{MbagcBaseUrl}/getstablefile?file={Uri.EscapeDataString(prefix)}&version={Uri.EscapeDataString(stableVer)}";
                            byte[] lzmaShard = await _httpClient.GetByteArrayAsync(url);
                            if (lzmaShard != null && lzmaShard.Length >= 13)
                            {
                                string shardFile = Path.Combine(shardDir, SanitizeShardFileName(prefix) + ".lzma");
                                File.WriteAllBytes(shardFile, lzmaShard);

                                byte[] rawPb = LzmaDecoder.Decompress(lzmaShard);
                                List<PhoneSpamEntryModel> parsed = ParseMbagcRecordsProtobuf(rawPb, "callerid.mbagc.ru");
                                // Store entries with negative rating or title in main fast dictionary
                                updated += MergeEntriesBatch(parsed.Where(e => e.IsSpam || !string.IsNullOrEmpty(e.Title)), maxPerShard: 400);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                sourcesReached.Add("callerid.mbagc.ru");
            }
            catch (Exception)
            {
            }

            // 2. Also import from local AGContacts installation on Android if available via readable path
            updated += TryImportDeviceAgcShards();

            // 3. Harvest recent Russian spam numbers from zvonili.me
            progress?.Report("zvonili.me: fetching recent spam reports…");
            try
            {
                int zvoniliCount = await FetchRecentFromZvoniliMeAsync();
                if (zvoniliCount > 0)
                {
                    updated += zvoniliCount;
                    sourcesReached.Add("zvonili.me");
                }
            }
            catch (Exception)
            {
            }

            // 4. Harvest recent Russian spam numbers from whocalls.ru
            progress?.Report("whocalls.ru: fetching recent spam reports…");
            try
            {
                int whoCallsCount = await FetchRecentFromWhoCallsRuAsync();
                if (whoCallsCount > 0)
                {
                    updated += whoCallsCount;
                    sourcesReached.Add("whocalls.ru");
                }
            }
            catch (Exception)
            {
            }

            // 5. Community Russian spam list (phone-spam.txt)
            progress?.Report("Russian spam list: downloading…");
            try
            {
                string txt = await _httpClient.GetStringAsync(CommunityRuSpamGistUrl);
                int gistCount = ImportSpamData(txt, "ru-community-list");
                if (gistCount > 0)
                {
                    updated += gistCount;
                    sourcesReached.Add("ru-community");
                }
            }
            catch (Exception)
            {
            }

            // 6. Optional user-configured custom spam database URL
            string customUrl = CustomSpamSourceUrl;
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                progress?.Report("Custom URL: downloading…");
                try
                {
                    string customContent = await _httpClient.GetStringAsync(customUrl);
                    int customCount = ImportSpamData(customContent, "custom-url");
                    if (customCount > 0)
                    {
                        updated += customCount;
                        sourcesReached.Add("custom-url");
                    }
                }
                catch (Exception)
                {
                }
            }

            int total;
            lock (_syncRoot)
            {
                _status.LastUpdatedUtc = DateTime.UtcNow;
                _status.LocalRecordCount = _spamEntries.Count;
                _status.CachedShardCount = CountCachedShardFiles();
                total = _spamEntries.Count;
                SaveSpamDatabaseUnsafe();
            }

            string summary = sourcesReached.Count > 0
                ? string.Join(", ", sourcesReached)
                : "offline";
            return (total, updated, summary);
        }

        /// <inheritdoc/>
        public int ImportSpamData(string content, string sourceName = "import")
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            EnsureLoaded();
            var parsed = new List<PhoneSpamEntryModel>();

            // Check if XML (e.g. voprosoff.net / AGContacts export)
            if (content.TrimStart().StartsWith("<"))
            {
                foreach (Match m in Regex.Matches(content, @"(?:number|phone|tel)[""'=\s>]+([+\d][\d\s()-]{6,16})", RegexOptions.IgnoreCase))
                {
                    string num = PhoneBlockRuleModel.NormalizeCanonicalDigits(m.Groups[1].Value);
                    if (num.Length >= 7)
                    {
                        parsed.Add(new PhoneSpamEntryModel
                        {
                            Number = num,
                            Rating = PhoneSpamRating.Negative,
                            NegativeCount = 1,
                            Category = "Спам / Нежелательный",
                            Source = sourceName,
                            UpdatedAtUtc = DateTime.UtcNow,
                        });
                    }
                }
            }

            // Line-based TXT / CSV: "+74950211749;marketing_telecom" or "+79031234567,Мошенники,Название"
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                string[] parts = line.Split(new[] { ';', ',', '\t', '|' }, StringSplitOptions.None);
                string num = PhoneBlockRuleModel.NormalizeCanonicalDigits(parts[0]);
                if (num.Length < 7 || num.Length > 15)
                    continue;

                string category = parts.Length > 1 ? TranslateCategoryTag(parts[1].Trim()) : "Спам / Нежелательный";
                string title = parts.Length > 2 ? parts[2].Trim() : string.Empty;

                parsed.Add(new PhoneSpamEntryModel
                {
                    Number = num,
                    Rating = PhoneSpamRating.Negative,
                    NegativeCount = 1,
                    Category = category,
                    Title = title,
                    Source = sourceName,
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            }

            int count = MergeEntriesBatch(parsed);
            lock (_syncRoot)
            {
                _status.LastUpdatedUtc = DateTime.UtcNow;
                SaveSpamDatabaseUnsafe();
            }
            return count;
        }

        /// <inheritdoc/>
        public void UpsertSpamEntry(PhoneSpamEntryModel entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Number))
                return;

            string canonical = PhoneBlockRuleModel.NormalizeCanonicalDigits(entry.Number);
            if (string.IsNullOrEmpty(canonical))
                return;

            entry.Number = canonical;
            entry.UpdatedAtUtc = DateTime.UtcNow;

            EnsureLoaded();
            lock (_syncRoot)
            {
                _spamEntries[canonical] = entry;
                SaveSpamDatabaseUnsafe();
            }
        }

        /// <inheritdoc/>
        public void RemoveSpamEntry(string phoneNumber)
        {
            string canonical = PhoneBlockRuleModel.NormalizeCanonicalDigits(phoneNumber);
            if (string.IsNullOrEmpty(canonical))
                return;

            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_spamEntries.Remove(canonical))
                    SaveSpamDatabaseUnsafe();
            }
        }

        /// <inheritdoc/>
        public void ClearSpamDatabase()
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                _spamEntries.Clear();
                SaveSpamDatabaseUnsafe();
            }
        }

        /// <inheritdoc/>
        public IncomingCallDecision EvaluateIncomingCall(string phoneNumber, bool isKnownContact)
        {
            var decision = new IncomingCallDecision();
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return decision;

            if (isKnownContact && SkipContactsWhenBlocking)
                return decision;

            EnsureLoaded();

            // 1. Check user Blocklist rules / masks (e.g. +7903*)
            if (IsBlocklistEnabled)
            {
                lock (_syncRoot)
                {
                    PhoneBlockRuleModel matched = _blockRules.FirstOrDefault(r => r.IsEnabled && r.Matches(phoneNumber));
                    if (matched != null)
                    {
                        matched.BlockedCount++;
                        matched.LastBlockedAtUtc = DateTime.UtcNow;
                        SaveBlockRulesUnsafe();

                        decision.ShouldBlock = true;
                        decision.MatchedRule = matched;
                        decision.BlockReason = string.IsNullOrWhiteSpace(matched.Label)
                            ? $"Mask {matched.Pattern}"
                            : $"{matched.Pattern} ({matched.Label})";
                        return decision;
                    }
                }
            }

            // 2. Check local Spam DB
            PhoneSpamEntryModel spam = LookupLocal(phoneNumber);
            decision.SpamEntry = spam;
            if (AutoBlockSpam && spam != null && spam.IsSpam)
            {
                decision.ShouldBlock = true;
                decision.BlockReason = $"Spam DB: {spam.SummaryText}";
            }

            return decision;
        }

        private async Task<PhoneSpamEntryModel> QueryMbagcInfo2Async(string canonical)
        {
            try
            {
                string url = $"{MbagcBaseUrl}/info2/+{canonical}";
                string json = await _httpClient.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                int ratingRaw = root.TryGetProperty("rating", out JsonElement rEl) ? rEl.GetInt32() : 0;
                int pos = root.TryGetProperty("positive", out JsonElement pEl) ? pEl.GetInt32() : 0;
                int neg = root.TryGetProperty("negative", out JsonElement nEl) ? nEl.GetInt32() : 0;
                int neu = root.TryGetProperty("neutral", out JsonElement uEl) ? uEl.GetInt32() : 0;
                string cat = root.TryGetProperty("category", out JsonElement cEl) ? cEl.GetString() : string.Empty;
                string title = root.TryGetProperty("title", out JsonElement tEl) ? tEl.GetString() : string.Empty;
                string comment = root.TryGetProperty("comment", out JsonElement mEl) ? mEl.GetString() : string.Empty;

                if (pos == 0 && neg == 0 && neu == 0 && string.IsNullOrWhiteSpace(cat) && string.IsNullOrWhiteSpace(title))
                    return null;

                PhoneSpamRating rating = ratingRaw == 2 || neg > pos
                    ? PhoneSpamRating.Negative
                    : (pos > neg ? PhoneSpamRating.Positive : PhoneSpamRating.Neutral);

                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = rating,
                    PositiveCount = pos,
                    NegativeCount = neg,
                    NeutralCount = neu,
                    Category = cat ?? string.Empty,
                    Title = title ?? string.Empty,
                    Comment = comment ?? string.Empty,
                    Source = "callerid.mbagc.ru",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryMbagcWebPageAsync(string canonical)
        {
            try
            {
                string html = await _httpClient.GetStringAsync($"https://callerid.mbagc.ru/{canonical}?lang=ru");
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                Match metaMatch = Regex.Match(html, @"<p class=""text-muted""[^>]*>([^<]+)</p>", RegexOptions.IgnoreCase);
                Match catMatch = Regex.Match(html, @"<h6 class=""card-subtitle[^>]*>([^<]+)</h6>", RegexOptions.IgnoreCase);
                Match swMatch = Regex.Match(html, @"ng-switch on=""(\d)""", RegexOptions.IgnoreCase);

                string meta = metaMatch.Success ? WebUtility.HtmlDecode(metaMatch.Groups[1].Value).Trim() : string.Empty;
                string cat = catMatch.Success ? WebUtility.HtmlDecode(catMatch.Groups[1].Value).Trim() : string.Empty;
                int sw = swMatch.Success && int.TryParse(swMatch.Groups[1].Value, out int v) ? v : 3;

                if (string.IsNullOrEmpty(meta) && (string.IsNullOrEmpty(cat) || cat == "Без категории"))
                    return null;

                string oper = string.Empty;
                string region = meta;
                string[] parts = meta.Split(',');
                if (parts.Length >= 2)
                {
                    oper = parts[0].Trim();
                    region = string.Join(", ", parts.Skip(1).Select(p => p.Trim()));
                }

                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = sw == 2 ? PhoneSpamRating.Negative : (sw == 1 ? PhoneSpamRating.Positive : PhoneSpamRating.Neutral),
                    Category = cat == "Без категории" ? string.Empty : cat,
                    Operator = oper,
                    Region = region,
                    Source = "callerid.mbagc.ru",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryZvoniliMeAsync(string canonical)
        {
            try
            {
                string html = await _httpClient.GetStringAsync($"https://zvonili.me/phone/{canonical}");
                if (string.IsNullOrWhiteSpace(html) || html.Contains("404 - Страница не найдена"))
                    return null;

                Match statusMatch = Regex.Match(html, @"<span class=""review-status[^""]*"">([^<]+)</span>", RegexOptions.IgnoreCase);
                Match regionMatch = Regex.Match(html, @"<span itemprop=""address"">([^<]+)</span>", RegexOptions.IgnoreCase);
                Match dangerMatch = Regex.Match(html, @"<span class=""badge badge-danger"">(\d+)</span>", RegexOptions.IgnoreCase);

                string category = statusMatch.Success ? WebUtility.HtmlDecode(statusMatch.Groups[1].Value).Trim() : string.Empty;
                string region = regionMatch.Success ? WebUtility.HtmlDecode(regionMatch.Groups[1].Value).Trim() : string.Empty;
                int neg = dangerMatch.Success && int.TryParse(dangerMatch.Groups[1].Value, out int n) ? n : 0;

                if (string.IsNullOrEmpty(category) && string.IsNullOrEmpty(region) && neg == 0)
                    return null;

                bool isSpam = neg > 0 || IsSpamCategoryText(category);
                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = isSpam ? PhoneSpamRating.Negative : PhoneSpamRating.Neutral,
                    NegativeCount = neg,
                    Category = category,
                    Region = region,
                    Source = "zvonili.me",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryWhoCallsRuAsync(string canonical)
        {
            try
            {
                string tenDigits = canonical.Length == 11 && canonical.StartsWith("7")
                    ? canonical.Substring(1)
                    : canonical;
                string html = await _httpClient.GetStringAsync($"https://whocalls.ru/{tenDigits}");
                if (string.IsNullOrWhiteSpace(html) || html.Contains("Страница не найдена"))
                    return null;

                Match catMatch = Regex.Match(html, @"<span itemprop=""name"" class=""name"">([^<]+)</span>", RegexOptions.IgnoreCase);
                if (!catMatch.Success)
                    catMatch = Regex.Match(html, @"<div class=""call_type_tag"">([^<]+)</div>", RegexOptions.IgnoreCase);

                string category = catMatch.Success ? WebUtility.HtmlDecode(catMatch.Groups[1].Value).Trim() : string.Empty;
                if (string.IsNullOrEmpty(category))
                    return null;

                bool isSpam = IsSpamCategoryText(category);
                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = isSpam ? PhoneSpamRating.Negative : PhoneSpamRating.Neutral,
                    NegativeCount = isSpam ? 1 : 0,
                    Category = category,
                    Source = "whocalls.ru",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryVoprosoffAsync(string canonical)
        {
            try
            {
                string html = await _httpClient.GetStringAsync($"https://voprosoff.net/kto-zvonil/{canonical}");
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                Match negMatch = Regex.Match(html, @"(\d+)x\s*отрицательн", RegexOptions.IgnoreCase);
                Match posMatch = Regex.Match(html, @"(\d+)x\s*положительн", RegexOptions.IgnoreCase);
                Match catMatch = Regex.Match(html, @"\d+x\s*(мошенники/обман|реклама/предложение услуг|коллекторы|опрос|спам|хулиганы)", RegexOptions.IgnoreCase);

                int neg = negMatch.Success && int.TryParse(negMatch.Groups[1].Value, out int n) ? n : 0;
                int pos = posMatch.Success && int.TryParse(posMatch.Groups[1].Value, out int p) ? p : 0;
                string cat = catMatch.Success ? catMatch.Groups[1].Value : string.Empty;

                if (neg == 0 && pos == 0 && string.IsNullOrEmpty(cat))
                    return null;

                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = neg >= pos ? PhoneSpamRating.Negative : PhoneSpamRating.Positive,
                    NegativeCount = neg,
                    PositiveCount = pos,
                    Category = cat,
                    Source = "voprosoff.net",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryZvoniliComAsync(string canonical)
        {
            try
            {
                string eightDigits = canonical.Length == 11 && canonical.StartsWith("7")
                    ? "8" + canonical.Substring(1)
                    : canonical;
                string html = await _httpClient.GetStringAsync($"https://zvonili.com/phone/{eightDigits}");
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                Match catMatch = Regex.Match(html, @"(Мошенники|Реклама|Коллекторы|Спам|Опрос|Финансовые услуги|Нежелательный звонок)", RegexOptions.IgnoreCase);
                if (!catMatch.Success)
                    return null;

                string cat = catMatch.Groups[1].Value;
                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = PhoneSpamRating.Negative,
                    NegativeCount = 1,
                    Category = cat,
                    Source = "zvonili.com",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<PhoneSpamEntryModel> QueryCallFilterAppAsync(string canonical)
        {
            try
            {
                string resp = await _httpClient.GetStringAsync($"https://callfilter.app/api_for_mbagc/{canonical}");
                if (string.IsNullOrWhiteSpace(resp))
                    return null;

                using JsonDocument doc = JsonDocument.Parse(resp);
                JsonElement root = doc.RootElement;
                string cat = root.TryGetProperty("category", out JsonElement cEl) ? cEl.GetString() : string.Empty;
                string title = root.TryGetProperty("title", out JsonElement tEl) ? tEl.GetString() : string.Empty;
                int neg = root.TryGetProperty("negative", out JsonElement nEl) ? nEl.GetInt32() : 0;

                if (string.IsNullOrEmpty(cat) && string.IsNullOrEmpty(title) && neg == 0)
                    return null;

                return new PhoneSpamEntryModel
                {
                    Number = canonical,
                    Rating = neg > 0 || IsSpamCategoryText(cat) ? PhoneSpamRating.Negative : PhoneSpamRating.Neutral,
                    NegativeCount = neg,
                    Category = cat ?? string.Empty,
                    Title = title ?? string.Empty,
                    Source = "callfilter.app",
                    UpdatedAtUtc = DateTime.UtcNow,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<(string oper, string region)> QueryRussianMnpAsync(string canonical)
        {
            if (canonical.Length != 11 || !canonical.StartsWith("7"))
                return (null, null);

            string oper = null;
            string region = null;

            try
            {
                string voxJson = await _httpClient.GetStringAsync($"http://num.voxlink.ru/get/?num={canonical}");
                if (!string.IsNullOrWhiteSpace(voxJson))
                {
                    using JsonDocument doc = JsonDocument.Parse(voxJson);
                    if (doc.RootElement.TryGetProperty("operator", out JsonElement opEl))
                        oper = opEl.GetString();
                    if (doc.RootElement.TryGetProperty("region", out JsonElement regEl))
                    {
                        string r = regEl.GetString();
                        if (!string.IsNullOrWhiteSpace(r) && r != "-")
                            region = r;
                    }
                }
            }
            catch (Exception)
            {
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                try
                {
                    string hwJson = await _httpClient.GetStringAsync($"https://htmlweb.ru/geo/api.php?json&telcod={canonical}");
                    if (!string.IsNullOrWhiteSpace(hwJson))
                    {
                        using JsonDocument doc = JsonDocument.Parse(hwJson);
                        if (doc.RootElement.TryGetProperty("region", out JsonElement regObj) &&
                            regObj.ValueKind == JsonValueKind.Object &&
                            regObj.TryGetProperty("name", out JsonElement regName))
                        {
                            region = regName.GetString();
                        }
                        if (string.IsNullOrWhiteSpace(oper) &&
                            doc.RootElement.TryGetProperty("0", out JsonElement opObj) &&
                            opObj.ValueKind == JsonValueKind.Object &&
                            opObj.TryGetProperty("oper", out JsonElement opName))
                        {
                            oper = opName.GetString();
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return (oper, region);
        }

        private async Task<int> FetchRecentFromZvoniliMeAsync()
        {
            string html = await _httpClient.GetStringAsync("https://zvonili.me/");
            var entries = new List<PhoneSpamEntryModel>();
            foreach (Match m in Regex.Matches(html, @"href=""https://zvonili\.me/phone/(7\d{10})"""))
            {
                string num = m.Groups[1].Value;
                entries.Add(new PhoneSpamEntryModel
                {
                    Number = num,
                    Rating = PhoneSpamRating.Negative,
                    NegativeCount = 1,
                    Category = "Нежелательный / проверка",
                    Source = "zvonili.me",
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            }
            return MergeEntriesBatch(entries);
        }

        private async Task<int> FetchRecentFromWhoCallsRuAsync()
        {
            string html = await _httpClient.GetStringAsync("https://whocalls.ru/");
            var entries = new List<PhoneSpamEntryModel>();
            foreach (Match m in Regex.Matches(html, @"href=""https://whocalls\.ru/(\d{10})"""))
            {
                string num = "7" + m.Groups[1].Value;
                entries.Add(new PhoneSpamEntryModel
                {
                    Number = num,
                    Rating = PhoneSpamRating.Negative,
                    NegativeCount = 1,
                    Category = "Спам / Жалоба",
                    Source = "whocalls.ru",
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            }
            return MergeEntriesBatch(entries);
        }

        private PhoneSpamEntryModel TryLookupInCachedShard(string canonical)
        {
            try
            {
                if (!long.TryParse(canonical, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return null;

                string shardDir = GetShardDirectoryPath();
                if (!Directory.Exists(shardDir))
                    return null;

                // Try longest matching prefix from 6 down to 2 digits (e.g. +749510, +74950, +7963, +790, +70)
                string withPlus = "+" + canonical;
                for (int len = Math.Min(7, withPlus.Length); len >= 3; len--)
                {
                    string prefix = withPlus.Substring(0, len);
                    string file = Path.Combine(shardDir, SanitizeShardFileName(prefix) + ".lzma");
                    if (File.Exists(file))
                    {
                        byte[] lzma = File.ReadAllBytes(file);
                        byte[] raw = LzmaDecoder.Decompress(lzma);
                        List<PhoneSpamEntryModel> records = ParseMbagcRecordsProtobuf(raw, "callerid.mbagc.ru");
                        PhoneSpamEntryModel hit = records.FirstOrDefault(r => r.Number == canonical);
                        if (hit != null)
                            return hit;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private async Task<PhoneSpamEntryModel> QueryOrFetchMbagcShardAsync(string canonical)
        {
            try
            {
                if (canonical.Length != 11 || !canonical.StartsWith("7"))
                    return null;

                PhoneSpamEntryModel cached = TryLookupInCachedShard(canonical);
                if (cached != null)
                    return cached;

                string stableVer;
                lock (_syncRoot)
                {
                    stableVer = _status.StableVersion;
                }
                if (string.IsNullOrEmpty(stableVer))
                {
                    byte[] stVerBytes = await _httpClient.GetByteArrayAsync($"{MbagcBaseUrl}/getstableversion");
                    ParseMbagcVersionProtobuf(stVerBytes, out stableVer, out _);
                }
                if (string.IsNullOrEmpty(stableVer))
                    return null;

                // Determine candidate shard prefix (+790..+795 use 3-digit, +7960..+7969 use 4-digit, etc.)
                string withPlus = "+" + canonical;
                string[] candidatePrefixes = new[]
                {
                    withPlus.Substring(0, Math.Min(5, withPlus.Length)),
                    withPlus.Substring(0, Math.Min(4, withPlus.Length)),
                    withPlus.Substring(0, Math.Min(3, withPlus.Length)),
                };

                string shardDir = GetShardDirectoryPath();
                Directory.CreateDirectory(shardDir);

                foreach (string prefix in candidatePrefixes.Distinct())
                {
                    try
                    {
                        string url = $"{MbagcBaseUrl}/getstablefile?file={Uri.EscapeDataString(prefix)}&version={Uri.EscapeDataString(stableVer)}";
                        HttpResponseMessage resp = await _httpClient.GetAsync(url);
                        if (!resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NoContent)
                            continue;

                        byte[] lzma = await resp.Content.ReadAsByteArrayAsync();
                        if (lzma != null && lzma.Length >= 13)
                        {
                            string shardFile = Path.Combine(shardDir, SanitizeShardFileName(prefix) + ".lzma");
                            File.WriteAllBytes(shardFile, lzma);

                            byte[] raw = LzmaDecoder.Decompress(lzma);
                            List<PhoneSpamEntryModel> records = ParseMbagcRecordsProtobuf(raw, "callerid.mbagc.ru");
                            return records.FirstOrDefault(r => r.Number == canonical);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private int TryImportDeviceAgcShards()
        {
            int imported = 0;
            try
            {
                string shardDir = GetShardDirectoryPath();
                Directory.CreateDirectory(shardDir);

                // On rooted Android devices where AGContacts or Чей номер+ is installed,
                // sync its offline ocidb (+7* LZMA shards and version metadata) into ocidb_shards.
                TrySyncAgcDataDirectoryViaSu(shardDir);

                foreach (string file in Directory.GetFiles(shardDir, "*.lzma"))
                {
                    try
                    {
                        byte[] raw = LzmaDecoder.Decompress(File.ReadAllBytes(file));
                        List<PhoneSpamEntryModel> parsed = ParseMbagcRecordsProtobuf(raw, "callerid.mbagc.ru");
                        imported += MergeEntriesBatch(parsed.Where(e => e.IsSpam), maxPerShard: 120);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return imported;
        }

        private void TrySyncAgcDataDirectoryViaSu(string shardDir)
        {
            if (!global::System.OperatingSystem.IsAndroid())
                return;

            try
            {
                string markerFile = Path.Combine(shardDir, "agc_lastdate.txt");
                var psi = new System.Diagnostics.ProcessStartInfo("su")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(
                    "for pkg in ru.agc.acontactnextdonateedition ru.agc.acontactnext ru.mbagc.whosenumberplus ru.mbagc.whosenumber; do " +
                    "d=\"/data/data/$pkg/files/ocidb\"; " +
                    "if [ -d \"$d/accumulative\" ]; then " +
                    "cat \"$d/lastdate\" 2>/dev/null > \"" + markerFile + "\"; " +
                    "for f in \"$d/accumulative/\"+7*; do " +
                    "bn=$(basename \"$f\" | sed 's/+/plus_/g'); " +
                    "cp \"$f\" \"" + shardDir + "/${bn}.lzma\" 2>/dev/null; " +
                    "done; " +
                    "chmod 600 \"" + shardDir + "/\"*.lzma \"" + markerFile + "\" 2>/dev/null; " +
                    "chown $(stat -c '%u:%g' \"" + shardDir + "\") \"" + shardDir + "/\"*.lzma \"" + markerFile + "\" 2>/dev/null; " +
                    "break; " +
                    "fi; done");

                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);

                if (File.Exists(markerFile))
                {
                    string ver = File.ReadAllText(markerFile).Trim();
                    lock (_syncRoot)
                    {
                        if (!string.IsNullOrEmpty(ver))
                            _status.AccumulativeVersion = ver;
                        if (_status.RemoteTotalRecords == 0)
                            _status.RemoteTotalRecords = 946912;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private int MergeEntriesBatch(IEnumerable<PhoneSpamEntryModel> entries, int maxPerShard = int.MaxValue)
        {
            int count = 0;
            lock (_syncRoot)
            {
                foreach (PhoneSpamEntryModel e in entries)
                {
                    if (count >= maxPerShard)
                        break;
                    if (e == null || string.IsNullOrWhiteSpace(e.Number))
                        continue;
                    _spamEntries[e.Number] = e;
                    count++;
                }
            }
            return count;
        }

        private static void ParseMbagcVersionProtobuf(byte[] buf, out string version, out int totalNumbers)
        {
            version = string.Empty;
            totalNumbers = 0;
            int pos = 0;
            while (pos < buf.Length)
            {
                ulong tag = ReadVarint(buf, ref pos);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (wire == 0)
                {
                    ulong val = ReadVarint(buf, ref pos);
                    if (field == 2)
                        totalNumbers = (int)val;
                }
                else if (wire == 2)
                {
                    int len = (int)ReadVarint(buf, ref pos);
                    if (field == 1 && pos + len <= buf.Length)
                        version = Encoding.UTF8.GetString(buf, pos, len);
                    pos += len;
                }
                else
                {
                    break;
                }
            }
        }

        private static List<string> ParseMbagcIndexProtobuf(byte[] buf)
        {
            var list = new List<string>();
            int pos = 0;
            while (pos < buf.Length)
            {
                ulong tag = ReadVarint(buf, ref pos);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (wire == 0)
                {
                    ReadVarint(buf, ref pos);
                }
                else if (wire == 2)
                {
                    int len = (int)ReadVarint(buf, ref pos);
                    if (field == 2 && pos + len <= buf.Length)
                        list.Add(Encoding.UTF8.GetString(buf, pos, len));
                    pos += len;
                }
                else
                {
                    break;
                }
            }
            return list;
        }

        private static List<PhoneSpamEntryModel> ParseMbagcRecordsProtobuf(byte[] buf, string sourceName)
        {
            var rawRecords = new List<(long num, int pos, int neg, int neu, int catId, int titleIdx, int commentIdx, long tsMs)>();
            var stringTable = new List<string>();

            int pos = 0;
            while (pos < buf.Length)
            {
                ulong tag = ReadVarint(buf, ref pos);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (wire == 2)
                {
                    int len = (int)ReadVarint(buf, ref pos);
                    if (pos + len > buf.Length)
                        break;

                    if (field == 1)
                    {
                        int subEnd = pos + len;
                        int subPos = pos;
                        while (subPos < subEnd)
                        {
                            ulong eTag = ReadVarint(buf, ref subPos);
                            int eWire = (int)(eTag & 7);
                            if (eWire == 2)
                            {
                                int eLen = (int)ReadVarint(buf, ref subPos);
                                ParseSingleMapEntry(buf, subPos, subPos + eLen, rawRecords);
                                subPos += eLen;
                            }
                            else if (eWire == 0)
                            {
                                ReadVarint(buf, ref subPos);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else if (field == 2)
                    {
                        stringTable.Add(Encoding.UTF8.GetString(buf, pos, len));
                    }
                    pos += len;
                }
                else if (wire == 0)
                {
                    ReadVarint(buf, ref pos);
                }
                else
                {
                    break;
                }
            }

            var results = new List<PhoneSpamEntryModel>(rawRecords.Count);
            foreach (var r in rawRecords)
            {
                string title = (r.titleIdx >= 0 && r.titleIdx < stringTable.Count) ? stringTable[r.titleIdx] : string.Empty;
                string comment = (r.commentIdx >= 0 && r.commentIdx < stringTable.Count) ? stringTable[r.commentIdx] : string.Empty;
                string category = MapMbagcCategoryId(r.catId);
                PhoneSpamRating rating = r.neg > r.pos
                    ? PhoneSpamRating.Negative
                    : (r.pos > r.neg ? PhoneSpamRating.Positive : PhoneSpamRating.Neutral);

                DateTime updatedUtc = r.tsMs > 0 && r.tsMs < 4102444800000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(r.tsMs).UtcDateTime
                    : DateTime.UtcNow;

                results.Add(new PhoneSpamEntryModel
                {
                    Number = r.num.ToString(CultureInfo.InvariantCulture),
                    Rating = rating,
                    PositiveCount = r.pos,
                    NegativeCount = r.neg,
                    NeutralCount = r.neu,
                    Category = category,
                    Title = title,
                    Comment = comment,
                    Source = sourceName,
                    UpdatedAtUtc = updatedUtc,
                });
            }
            return results;
        }

        private static void ParseSingleMapEntry(
            byte[] buf,
            int start,
            int end,
            List<(long num, int pos, int neg, int neu, int catId, int titleIdx, int commentIdx, long tsMs)> output)
        {
            int p = start;
            long num = 0;
            while (p < end)
            {
                ulong tag = ReadVarint(buf, ref p);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (wire == 0)
                {
                    long v = (long)ReadVarint(buf, ref p);
                    if (field == 1)
                        num = v;
                }
                else if (wire == 2)
                {
                    int len = (int)ReadVarint(buf, ref p);
                    if (field == 2 && p + len <= end)
                    {
                        int rp = p;
                        int rEnd = p + len;
                        int posCount = 0, negCount = 0, neuCount = 0, catId = 0;
                        int titleIdx = 0, commentIdx = 0;
                        long tsMs = 0;
                        while (rp < rEnd)
                        {
                            ulong rTag = ReadVarint(buf, ref rp);
                            int rField = (int)(rTag >> 3);
                            int rWire = (int)(rTag & 7);
                            if (rWire == 0)
                            {
                                long rv = (long)ReadVarint(buf, ref rp);
                                switch (rField)
                                {
                                    case 1: num = rv; break;
                                    case 2: posCount = (int)rv; break;
                                    case 3: negCount = (int)rv; break;
                                    case 4: neuCount = (int)rv; break;
                                    case 5: catId = (int)rv; break;
                                    case 6: titleIdx = (int)rv; break;
                                    case 7: commentIdx = (int)rv; break;
                                    case 8: tsMs = rv; break;
                                }
                            }
                            else if (rWire == 2)
                            {
                                int sLen = (int)ReadVarint(buf, ref rp);
                                rp += sLen;
                            }
                            else
                            {
                                break;
                            }
                        }
                        if (num > 0)
                            output.Add((num, posCount, negCount, neuCount, catId, titleIdx, commentIdx, tsMs));
                    }
                    p += len;
                }
                else
                {
                    break;
                }
            }
        }

        private static ulong ReadVarint(byte[] buf, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (pos < buf.Length)
            {
                byte b = buf[pos++];
                result |= ((ulong)(b & 0x7F)) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
            }
            return result;
        }

        private static string MapMbagcCategoryId(int id)
        {
            return id switch
            {
                1 => "Товары/маркетинг",
                2 => "Сбор задолженностей",
                3 => "Немой вызов",
                4 => "Злонамеренный",
                5 => "Нежелательный",
                6 => "Call-центр",
                7 => "Магазин",
                8 => "Некоммерческая организация",
                9 => "Политика",
                10 => "Мошенничество",
                11 => "Хулиганство",
                12 => "Медицина",
                13 => "Опрос",
                14 => "Другое",
                15 => "Финансовые услуги",
                16 => "Компания",
                17 => "Сервис/услуги",
                18 => "Робот/автомат",
                100 => "Безопасный личный",
                _ => "Спам / Нежелательный",
            };
        }

        private static string TranslateCategoryTag(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Спам";
            string lower = raw.ToLowerInvariant();
            if (lower.Contains("fraud") || lower.Contains("scam")) return "Мошенничество";
            if (lower.Contains("collector")) return "Коллекторы";
            if (lower.Contains("finance")) return "Финансовые услуги";
            if (lower.Contains("medic")) return "Медицина / реклама";
            if (lower.Contains("telecom") || lower.Contains("sipnet")) return "Телемаркетинг";
            if (lower.Contains("lawyer")) return "Юридический спам";
            if (lower.Contains("marketing")) return "Реклама / маркетинг";
            return raw;
        }

        private static bool IsSpamCategoryText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string lower = text.ToLowerInvariant();
            return lower.Contains("мошенн") ||
                   lower.Contains("спам") ||
                   lower.Contains("реклам") ||
                   lower.Contains("коллект") ||
                   lower.Contains("нежелат") ||
                   lower.Contains("опрос") ||
                   lower.Contains("робот") ||
                   lower.Contains("финанс") ||
                   lower.Contains("звон") ||
                   lower.Contains("долг") ||
                   lower.Contains("хулиган");
        }

        private void EnsureLoaded()
        {
            lock (_syncRoot)
            {
                if (_blockRules != null && _spamEntries != null && _status != null)
                    return;

                _blockRules = new List<PhoneBlockRuleModel>();
                _spamEntries = new Dictionary<string, PhoneSpamEntryModel>(StringComparer.Ordinal);
                _status = new PhoneSpamDatabaseStatus();

                try
                {
                    string rulesPath = Path.Combine(FileSystem.AppDataDirectory, BlocklistFileName);
                    if (File.Exists(rulesPath))
                    {
                        var loaded = JsonSerializer.Deserialize<List<PhoneBlockRuleModel>>(File.ReadAllText(rulesPath));
                        if (loaded != null)
                            _blockRules = loaded;
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    string dbPath = Path.Combine(FileSystem.AppDataDirectory, SpamDbFileName);
                    if (File.Exists(dbPath))
                    {
                        var loaded = JsonSerializer.Deserialize<List<PhoneSpamEntryModel>>(File.ReadAllText(dbPath));
                        if (loaded != null)
                        {
                            foreach (PhoneSpamEntryModel item in loaded)
                            {
                                if (!string.IsNullOrEmpty(item?.Number))
                                    _spamEntries[item.Number] = item;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    string metaPath = Path.Combine(FileSystem.AppDataDirectory, SpamMetaFileName);
                    if (File.Exists(metaPath))
                    {
                        var loadedMeta = JsonSerializer.Deserialize<PhoneSpamDatabaseStatus>(File.ReadAllText(metaPath));
                        if (loadedMeta != null)
                            _status = loadedMeta;
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private void SaveBlockRulesUnsafe()
        {
            try
            {
                string rulesPath = Path.Combine(FileSystem.AppDataDirectory, BlocklistFileName);
                File.WriteAllText(rulesPath, JsonSerializer.Serialize(_blockRules));
            }
            catch (Exception)
            {
            }
        }

        private void SaveSpamDatabaseUnsafe()
        {
            try
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, SpamDbFileName);
                List<PhoneSpamEntryModel> list = _spamEntries.Values
                    .OrderByDescending(e => e.UpdatedAtUtc)
                    .Take(25000)
                    .ToList();
                File.WriteAllText(dbPath, JsonSerializer.Serialize(list));

                string metaPath = Path.Combine(FileSystem.AppDataDirectory, SpamMetaFileName);
                File.WriteAllText(metaPath, JsonSerializer.Serialize(_status));
            }
            catch (Exception)
            {
            }
        }

        private static string GetShardDirectoryPath()
        {
            return Path.Combine(FileSystem.AppDataDirectory, OcidbShardFolderName);
        }

        private static string SanitizeShardFileName(string prefix)
        {
            return prefix.Replace("+", "plus_").Replace("/", "_");
        }

        private static int CountCachedShardFiles()
        {
            try
            {
                string dir = GetShardDirectoryPath();
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.lzma").Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
