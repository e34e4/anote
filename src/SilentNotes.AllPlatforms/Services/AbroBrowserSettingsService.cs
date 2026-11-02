// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroBrowserSettingsService : IAbroBrowserSettingsService
    {
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient;
        private readonly string _rootDir;
        private AbroBrowserSettingsModel _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBrowserSettingsService"/> class.
        /// </summary>
        public AbroBrowserSettingsService()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance with an optional shared <see cref="HttpClient"/>.
        /// </summary>
        public AbroBrowserSettingsService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? CreateDefaultHttpClient();
            _rootDir = AbroAiStoreService.ResolveRootDirectory();
            StoreFilePath = Path.Combine(_rootDir, "browser-settings.json");
            FontsDirectory = Path.Combine(_rootDir, "fonts");
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        /// <inheritdoc/>
        public string FontsDirectory { get; }

        /// <inheritdoc/>
        public bool SearchSuggestionsEnabled => LoadOrDefault().SearchSuggestionsEnabled;

        /// <inheritdoc/>
        public bool UseCustomFont
        {
            get
            {
                AbroBrowserSettingsModel settings = LoadOrDefault();
                return settings.UseCustomFont && !string.IsNullOrWhiteSpace(settings.CustomFontFileName)
                    && File.Exists(GetCustomFontPath());
            }
        }

        /// <inheritdoc/>
        public AbroBrowserSettingsModel LoadOrDefault()
        {
            lock (_lock)
            {
                if (_cached != null)
                    return _cached;

                try
                {
                    if (File.Exists(StoreFilePath))
                    {
                        string json = File.ReadAllText(StoreFilePath, Encoding.UTF8);
                        AbroBrowserSettingsModel parsed = AbroBrowserSettingsFormat.Parse(json);
                        if (parsed != null)
                        {
                            _cached = parsed;
                            return _cached;
                        }
                    }
                }
                catch (Exception)
                {
                }

                _cached = new AbroBrowserSettingsModel();
                _cached.EnsureDefaultEngines();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroBrowserSettingsModel settings)
        {
            if (settings == null)
                return false;

            lock (_lock)
            {
                try
                {
                    settings.EnsureDefaultEngines();
                    settings.Touch();
                    Directory.CreateDirectory(_rootDir);
                    string json = AbroBrowserSettingsFormat.Serialize(settings);
                    string temp = StoreFilePath + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    if (File.Exists(StoreFilePath))
                        File.Delete(StoreFilePath);
                    File.Move(temp, StoreFilePath);
                    _cached = settings;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public string BuildSearchUrl(string query)
        {
            AbroSearchEngineModel engine = LoadOrDefault().GetDefaultEngine();
            string template = engine?.SearchUrl;
            if (string.IsNullOrWhiteSpace(template))
                template = "https://duckduckgo.com/?q={query}";
            return template.Replace("{query}", Uri.EscapeDataString(query ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            query = query?.Trim() ?? string.Empty;
            if (query.Length < 2)
                return Array.Empty<string>();

            AbroBrowserSettingsModel settings = LoadOrDefault();
            if (!settings.SearchSuggestionsEnabled)
                return Array.Empty<string>();

            AbroSearchEngineModel engine = settings.GetDefaultEngine();
            if (engine == null || string.IsNullOrWhiteSpace(engine.SuggestUrl))
                return Array.Empty<string>();

            string url = engine.SuggestUrl.Replace("{query}", Uri.EscapeDataString(query), StringComparison.OrdinalIgnoreCase);
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return ParseSuggestions(body, max: 8);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <inheritdoc/>
        public string GetCustomFontPath()
        {
            AbroBrowserSettingsModel settings = LoadOrDefault();
            if (string.IsNullOrWhiteSpace(settings.CustomFontFileName))
                return null;
            return Path.Combine(FontsDirectory, settings.CustomFontFileName);
        }

        /// <inheritdoc/>
        public bool TryInstallCustomFont(byte[] fontBytes, string originalFileName)
        {
            if (fontBytes == null || fontBytes.Length < 16)
                return false;

            string safeName = SanitizeFontFileName(originalFileName);
            try
            {
                Directory.CreateDirectory(FontsDirectory);
                string path = Path.Combine(FontsDirectory, safeName);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, fontBytes);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temp, path);

                // Remove other previously installed fonts to avoid clutter.
                foreach (string existing in Directory.GetFiles(FontsDirectory))
                {
                    if (!string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(existing); }
                        catch (Exception) { }
                    }
                }

                AbroBrowserSettingsModel settings = LoadOrDefault();
                settings.UseCustomFont = true;
                settings.CustomFontFileName = safeName;
                return TrySave(settings);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool ClearCustomFont()
        {
            AbroBrowserSettingsModel settings = LoadOrDefault();
            try
            {
                if (Directory.Exists(FontsDirectory))
                {
                    foreach (string existing in Directory.GetFiles(FontsDirectory))
                    {
                        try { File.Delete(existing); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception)
            {
            }

            settings.UseCustomFont = false;
            settings.CustomFontFileName = string.Empty;
            return TrySave(settings);
        }

        /// <summary>Parses OpenSearch / DuckDuckGo suggestion JSON payloads.</summary>
        public static IReadOnlyList<string> ParseSuggestions(string body, int max)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
                return results;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        // OpenSearch / Google / Bing: ["query", ["s1","s2",...], ...]
                        if (root.GetArrayLength() >= 2 && root[1].ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in root[1].EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                    AddSuggestion(results, item.GetString(), max);
                                if (results.Count >= max)
                                    break;
                            }

                            return results;
                        }

                        // DuckDuckGo: [{"phrase":"..."}, ...]
                        foreach (JsonElement item in root.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object
                                && item.TryGetProperty("phrase", out JsonElement phrase)
                                && phrase.ValueKind == JsonValueKind.String)
                            {
                                AddSuggestion(results, phrase.GetString(), max);
                            }
                            else if (item.ValueKind == JsonValueKind.String)
                            {
                                AddSuggestion(results, item.GetString(), max);
                            }

                            if (results.Count >= max)
                                break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            return results;
        }

        private static void AddSuggestion(List<string> results, string value, int max)
        {
            if (results.Count >= max || string.IsNullOrWhiteSpace(value))
                return;
            value = value.Trim();
            if (results.Exists(r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase)))
                return;
            results.Add(value);
        }

        private static string SanitizeFontFileName(string originalFileName)
        {
            string name = Path.GetFileName(originalFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                name = "custom.ttf";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            string ext = Path.GetExtension(name);
            if (!string.Equals(ext, ".ttf", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".otf", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name) + ".ttf";
            }

            if (name.Length > 80)
                name = name.Substring(0, 76) + Path.GetExtension(name);
            return name;
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("aNote-Abro/1.0");
            return client;
        }
    }
}
