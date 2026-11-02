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
    /// Reads and writes the Abro download store JSON.
    /// </summary>
    public static class AbroDownloadStoreFormat
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Parses store JSON. Returns an empty store for blank input, or null if invalid.
        /// </summary>
        public static AbroDownloadStoreModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroDownloadStoreModel();

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

            var store = new AbroDownloadStoreModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                store.Version = obj["version"].GetValue<int>();
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                store.UpdatedAt = updatedAt;
            store.DownloadDirectory = GetString(obj, "downloadDirectory") ?? string.Empty;
            if (obj["threadCount"] != null && obj["threadCount"].GetValueKind() == JsonValueKind.Number)
                store.ThreadCount = obj["threadCount"].GetValue<int>();

            if (obj["history"] is JsonArray history)
            {
                foreach (JsonNode node in history)
                {
                    if (node is not JsonObject item)
                        continue;
                    var entry = new AbroDownloadHistoryItemModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        entry.Id = id;
                    entry.Url = GetString(item, "url") ?? string.Empty;
                    entry.FileName = GetString(item, "fileName") ?? string.Empty;
                    entry.FilePath = GetString(item, "filePath") ?? string.Empty;
                    if (TryGetUtcDateTime(item, "startedAt", out DateTime startedAt))
                        entry.StartedAt = startedAt;
                    if (TryGetUtcDateTime(item, "completedAt", out DateTime completedAt))
                        entry.CompletedAt = completedAt;
                    if (item["bytes"] != null && item["bytes"].GetValueKind() == JsonValueKind.Number)
                        entry.Bytes = item["bytes"].GetValue<long>();
                    entry.Status = GetString(item, "status") ?? AbroDownloadHistoryItemModel.StatusCompleted;
                    entry.Error = GetString(item, "error");
                    if (item["threadCount"] != null && item["threadCount"].GetValueKind() == JsonValueKind.Number)
                        entry.ThreadCount = item["threadCount"].GetValue<int>();
                    store.History.Add(entry);
                }
            }

            return store;
        }

        /// <summary>
        /// Serializes the store to JSON.
        /// </summary>
        public static string Serialize(AbroDownloadStoreModel store)
        {
            store ??= new AbroDownloadStoreModel();
            var root = new JsonObject
            {
                ["version"] = store.Version,
                ["updatedAt"] = store.UpdatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["downloadDirectory"] = store.DownloadDirectory ?? string.Empty,
                ["threadCount"] = store.GetClampedThreadCount(),
            };

            var history = new JsonArray();
            foreach (AbroDownloadHistoryItemModel entry in store.History ?? new List<AbroDownloadHistoryItemModel>())
            {
                var item = new JsonObject
                {
                    ["id"] = entry.Id.ToString("D"),
                    ["url"] = entry.Url ?? string.Empty,
                    ["fileName"] = entry.FileName ?? string.Empty,
                    ["filePath"] = entry.FilePath ?? string.Empty,
                    ["startedAt"] = entry.StartedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ["bytes"] = entry.Bytes,
                    ["status"] = entry.Status ?? AbroDownloadHistoryItemModel.StatusCompleted,
                    ["threadCount"] = entry.ThreadCount,
                };
                if (entry.CompletedAt.HasValue)
                    item["completedAt"] = entry.CompletedAt.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(entry.Error))
                    item["error"] = entry.Error;
                history.Add(item);
            }

            root["history"] = history;
            return root.ToJsonString(WriteOptions);
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
