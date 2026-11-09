// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Reads and writes Abro browser settings JSON.
    /// </summary>
    public static class AbroBrowserSettingsFormat
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Parses settings JSON. Returns defaults for blank input, or null if invalid.
        /// </summary>
        public static AbroBrowserSettingsModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                var empty = new AbroBrowserSettingsModel();
                empty.EnsureDefaultEngines();
                return empty;
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            if (root is not JsonObject obj)
                return null;

            var settings = new AbroBrowserSettingsModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                settings.Version = obj["version"].GetValue<int>();
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                settings.UpdatedAt = updatedAt;
            if (obj["searchSuggestionsEnabled"] != null && obj["searchSuggestionsEnabled"].GetValueKind() == JsonValueKind.False)
                settings.SearchSuggestionsEnabled = false;
            if (TryGetGuid(obj, "defaultEngineId", out Guid defaultId))
                settings.DefaultEngineId = defaultId;
            if (obj["useCustomFont"] != null && obj["useCustomFont"].GetValueKind() == JsonValueKind.True)
                settings.UseCustomFont = true;
            settings.CustomFontFileName = GetString(obj, "customFontFileName") ?? string.Empty;
            settings.BlackMode = ParseBlackMode(GetString(obj, "blackMode"));
            if (obj["blackModeDomains"] is JsonArray domains)
            {
                foreach (JsonNode node in domains)
                {
                    if (node == null || node.GetValueKind() != JsonValueKind.String)
                        continue;
                    string domain = AbroBrowserSettingsModel.NormalizeDomain(node.GetValue<string>());
                    if (string.IsNullOrEmpty(domain))
                        continue;
                    if (settings.BlackModeDomains.Exists(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    settings.BlackModeDomains.Add(domain);
                }
            }

            if (obj["blockImages"] != null && obj["blockImages"].GetValueKind() == JsonValueKind.True)
                settings.BlockImages = true;
            if (obj["socks5Enabled"] != null && obj["socks5Enabled"].GetValueKind() == JsonValueKind.True)
                settings.Socks5Enabled = true;
            settings.Socks5Host = GetString(obj, "socks5Host") ?? string.Empty;
            if (obj["socks5Port"] != null && obj["socks5Port"].GetValueKind() == JsonValueKind.Number)
                settings.Socks5Port = obj["socks5Port"].GetValue<int>();
            settings.Socks5Username = GetString(obj, "socks5Username") ?? string.Empty;
            settings.Socks5Password = GetString(obj, "socks5Password") ?? string.Empty;

            if (obj["engines"] is JsonArray engines)
            {
                foreach (JsonNode node in engines)
                {
                    if (node is not JsonObject item)
                        continue;
                    var engine = new AbroSearchEngineModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        engine.Id = id;
                    engine.Name = GetString(item, "name") ?? string.Empty;
                    engine.SearchUrl = GetString(item, "searchUrl") ?? string.Empty;
                    engine.SuggestUrl = GetString(item, "suggestUrl") ?? string.Empty;
                    settings.Engines.Add(engine);
                }
            }

            settings.EnsureDefaultEngines();
            return settings;
        }

        /// <summary>
        /// Serializes settings to JSON.
        /// </summary>
        public static string Serialize(AbroBrowserSettingsModel settings)
        {
            settings ??= new AbroBrowserSettingsModel();
            settings.EnsureDefaultEngines();
            var root = new JsonObject
            {
                ["version"] = settings.Version,
                ["updatedAt"] = settings.UpdatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["searchSuggestionsEnabled"] = settings.SearchSuggestionsEnabled,
                ["useCustomFont"] = settings.UseCustomFont,
                ["customFontFileName"] = settings.CustomFontFileName ?? string.Empty,
                ["blackMode"] = FormatBlackMode(settings.BlackMode),
                ["blockImages"] = settings.BlockImages,
                ["socks5Enabled"] = settings.Socks5Enabled,
                ["socks5Host"] = settings.Socks5Host ?? string.Empty,
                ["socks5Port"] = settings.Socks5Port,
                ["socks5Username"] = settings.Socks5Username ?? string.Empty,
                ["socks5Password"] = settings.Socks5Password ?? string.Empty,
            };
            if (settings.DefaultEngineId.HasValue)
                root["defaultEngineId"] = settings.DefaultEngineId.Value.ToString("D");

            var blackDomains = new JsonArray();
            foreach (string domain in settings.BlackModeDomains ?? new List<string>())
            {
                string normalized = AbroBrowserSettingsModel.NormalizeDomain(domain);
                if (!string.IsNullOrEmpty(normalized))
                    blackDomains.Add(normalized);
            }

            root["blackModeDomains"] = blackDomains;

            var engines = new JsonArray();
            foreach (AbroSearchEngineModel engine in settings.Engines ?? new List<AbroSearchEngineModel>())
            {
                engines.Add(new JsonObject
                {
                    ["id"] = engine.Id.ToString("D"),
                    ["name"] = engine.Name ?? string.Empty,
                    ["searchUrl"] = engine.SearchUrl ?? string.Empty,
                    ["suggestUrl"] = engine.SuggestUrl ?? string.Empty,
                });
            }

            root["engines"] = engines;
            return root.ToJsonString(WriteOptions);
        }

        private static AbroBlackMode ParseBlackMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return AbroBlackMode.Disable;
            if (string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
                return AbroBlackMode.Enable;
            if (string.Equals(value, "selectively", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "selective", StringComparison.OrdinalIgnoreCase))
                return AbroBlackMode.Selectively;
            return AbroBlackMode.Disable;
        }

        private static string FormatBlackMode(AbroBlackMode mode)
        {
            switch (mode)
            {
                case AbroBlackMode.Enable:
                    return "enable";
                case AbroBlackMode.Selectively:
                    return "selectively";
                default:
                    return "disable";
            }
        }

        private static string GetString(JsonObject obj, string name)
        {
            JsonNode node = obj[name];
            if (node == null || node.GetValueKind() != JsonValueKind.String)
                return null;
            return node.GetValue<string>();
        }

        private static bool TryGetGuid(JsonObject obj, string name, out Guid value)
        {
            value = Guid.Empty;
            string text = GetString(obj, name);
            return !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text, out value);
        }

        private static bool TryGetUtcDateTime(JsonObject obj, string name, out DateTime value)
        {
            value = default;
            string text = GetString(obj, name);
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return false;
            value = parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
            return true;
        }
    }
}
