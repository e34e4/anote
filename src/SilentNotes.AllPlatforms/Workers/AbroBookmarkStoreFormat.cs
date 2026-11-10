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
    /// Reads and writes the Abro bookmark store JSON used locally and on WebDAV.
    /// </summary>
    public static class AbroBookmarkStoreFormat
    {
        /// <summary>
        /// Parses store JSON. Returns an empty store for blank input, or null if invalid.
        /// </summary>
        public static AbroBookmarkStoreModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroBookmarkStoreModel();

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

            var store = new AbroBookmarkStoreModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                store.Version = obj["version"].GetValue<int>();
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                store.UpdatedAt = updatedAt;

            if (obj["isMoveToTopEnabled"] != null && obj["isMoveToTopEnabled"].GetValueKind() == JsonValueKind.True)
                store.IsMoveToTopEnabled = true;
            else if (obj["isMoveToTopEnabled"] != null && obj["isMoveToTopEnabled"].GetValueKind() == JsonValueKind.False)
                store.IsMoveToTopEnabled = false;

            if (obj["groups"] is JsonObject groupsObj)
            {
                foreach (KeyValuePair<string, JsonNode> pair in groupsObj)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;
                    string color = pair.Value?.GetValueKind() == JsonValueKind.String
                        ? pair.Value.GetValue<string>()
                        : "#2481cc";
                    store.Groups[pair.Key] = color ?? "#2481cc";
                }
            }

            if (obj["items"] is JsonArray items)
            {
                foreach (JsonNode node in items)
                {
                    if (node is not JsonObject item)
                        continue;
                    var bookmark = new AbroBookmarkItemModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        bookmark.Id = id;
                    bookmark.Url = GetString(item, "url") ?? string.Empty;
                    bookmark.Title = GetString(item, "title") ?? string.Empty;
                    bookmark.Group = GetString(item, "group") ?? string.Empty;
                    if (TryGetUtcDateTime(item, "modifiedAt", out DateTime modifiedAt))
                        bookmark.ModifiedAt = modifiedAt;
                    if (string.IsNullOrWhiteSpace(bookmark.Url))
                        continue;
                    store.Items.Add(bookmark);
                }
            }

            return store;
        }

        /// <summary>
        /// Serializes the store to pretty-printed UTF-8 JSON.
        /// </summary>
        public static string Serialize(AbroBookmarkStoreModel store)
        {
            store ??= new AbroBookmarkStoreModel();

            var items = new JsonArray();
            foreach (AbroBookmarkItemModel bookmark in store.Items ?? new List<AbroBookmarkItemModel>())
            {
                if (string.IsNullOrWhiteSpace(bookmark.Url))
                    continue;
                items.Add(new JsonObject
                {
                    ["id"] = bookmark.Id.ToString(),
                    ["url"] = bookmark.Url ?? string.Empty,
                    ["title"] = bookmark.Title ?? string.Empty,
                    ["group"] = bookmark.Group ?? string.Empty,
                    ["modifiedAt"] = ToIso(bookmark.ModifiedAt),
                });
            }

            var groups = new JsonObject();
            foreach (KeyValuePair<string, string> pair in store.Groups ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    groups[pair.Key] = pair.Value ?? "#2481cc";
            }

            var root = new JsonObject
            {
                ["version"] = store.Version > 0 ? store.Version : 1,
                ["updatedAt"] = ToIso(store.UpdatedAt == default ? DateTime.UtcNow : store.UpdatedAt),
                ["isMoveToTopEnabled"] = store.IsMoveToTopEnabled,
                ["groups"] = groups,
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
