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
    /// Reads and writes the Abro history store JSON used locally and on WebDAV.
    /// </summary>
    public static class AbroHistoryStoreFormat
    {
        /// <summary>
        /// Parses store JSON. Returns an empty store for blank input, or null if invalid.
        /// </summary>
        public static AbroHistoryStoreModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroHistoryStoreModel();

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

            var store = new AbroHistoryStoreModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                store.Version = obj["version"].GetValue<int>();
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                store.UpdatedAt = updatedAt;

            if (obj["items"] is JsonArray items)
            {
                foreach (JsonNode node in items)
                {
                    if (node is not JsonObject item)
                        continue;
                    var history = new AbroHistoryItemModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        history.Id = id;
                    history.Url = GetString(item, "url") ?? string.Empty;
                    history.Title = GetString(item, "title") ?? string.Empty;
                    if (TryGetUtcDateTime(item, "visitedAt", out DateTime visitedAt))
                        history.VisitedAt = visitedAt;
                    if (string.IsNullOrWhiteSpace(history.Url))
                        continue;
                    store.Items.Add(history);
                }
            }

            return store;
        }

        /// <summary>
        /// Serializes the store to pretty-printed UTF-8 JSON.
        /// </summary>
        public static string Serialize(AbroHistoryStoreModel store)
        {
            store ??= new AbroHistoryStoreModel();
            store.TrimItems();

            var items = new JsonArray();
            foreach (AbroHistoryItemModel history in store.Items ?? new List<AbroHistoryItemModel>())
            {
                if (string.IsNullOrWhiteSpace(history.Url))
                    continue;
                items.Add(new JsonObject
                {
                    ["id"] = history.Id.ToString(),
                    ["url"] = history.Url ?? string.Empty,
                    ["title"] = history.Title ?? string.Empty,
                    ["visitedAt"] = ToIso(history.VisitedAt),
                });
            }

            var root = new JsonObject
            {
                ["version"] = store.Version > 0 ? store.Version : 1,
                ["updatedAt"] = ToIso(store.UpdatedAt == default ? DateTime.UtcNow : store.UpdatedAt),
                ["items"] = items,
            };

            return root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        private static string ToIso(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static string GetString(JsonObject obj, string name)
        {
            if (obj != null && obj[name] is JsonNode node && node.GetValueKind() == JsonValueKind.String)
                return node.GetValue<string>();
            return null;
        }

        private static bool TryGetGuid(JsonObject obj, string name, out Guid value)
        {
            value = Guid.Empty;
            string text = GetString(obj, name);
            return text != null && Guid.TryParse(text, out value);
        }

        private static bool TryGetUtcDateTime(JsonObject obj, string name, out DateTime value)
        {
            value = default;
            string text = GetString(obj, name);
            if (text == null)
                return false;
            if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed))
            {
                value = parsed.UtcDateTime;
                return true;
            }
            return false;
        }
    }
}
