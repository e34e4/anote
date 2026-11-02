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
    /// Reads and writes the Abro ad-blocking store JSON.
    /// </summary>
    public static class AbroAdBlockStoreFormat
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Parses store JSON. Returns an empty store for blank input, or null if invalid.
        /// </summary>
        public static AbroAdBlockStoreModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroAdBlockStoreModel();

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

            var store = new AbroAdBlockStoreModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                store.Version = obj["version"].GetValue<int>();
            if (obj["enabled"] != null && obj["enabled"].GetValueKind() == JsonValueKind.True)
                store.Enabled = true;
            else if (obj["enabled"] != null && obj["enabled"].GetValueKind() == JsonValueKind.False)
                store.Enabled = false;
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                store.UpdatedAt = updatedAt;
            if (TryGetUtcDateTime(obj, "compiledAt", out DateTime compiledAt))
                store.CompiledAt = compiledAt;
            if (obj["compiledDomainCount"] != null && obj["compiledDomainCount"].GetValueKind() == JsonValueKind.Number)
                store.CompiledDomainCount = obj["compiledDomainCount"].GetValue<int>();

            if (obj["subscriptions"] is JsonArray subscriptions)
            {
                foreach (JsonNode node in subscriptions)
                {
                    if (node is not JsonObject item)
                        continue;
                    var sub = new AbroAdBlockSubscriptionModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        sub.Id = id;
                    sub.Url = GetString(item, "url") ?? string.Empty;
                    sub.Title = GetString(item, "title") ?? string.Empty;
                    if (item["enabled"] != null && item["enabled"].GetValueKind() == JsonValueKind.False)
                        sub.Enabled = false;
                    if (TryGetUtcDateTime(item, "createdAt", out DateTime createdAt))
                        sub.CreatedAt = createdAt;
                    if (TryGetUtcDateTime(item, "updatedAt", out DateTime subUpdated))
                        sub.UpdatedAt = subUpdated;
                    if (item["domainCount"] != null && item["domainCount"].GetValueKind() == JsonValueKind.Number)
                        sub.DomainCount = item["domainCount"].GetValue<int>();
                    sub.LastError = GetString(item, "lastError");
                    store.Subscriptions.Add(sub);
                }
            }

            return store;
        }

        /// <summary>
        /// Serializes the store to JSON.
        /// </summary>
        public static string Serialize(AbroAdBlockStoreModel store)
        {
            store ??= new AbroAdBlockStoreModel();
            var root = new JsonObject
            {
                ["version"] = store.Version,
                ["enabled"] = store.Enabled,
                ["updatedAt"] = store.UpdatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["compiledDomainCount"] = store.CompiledDomainCount,
            };
            if (store.CompiledAt.HasValue)
                root["compiledAt"] = store.CompiledAt.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

            var subscriptions = new JsonArray();
            foreach (AbroAdBlockSubscriptionModel sub in store.Subscriptions ?? new List<AbroAdBlockSubscriptionModel>())
            {
                var item = new JsonObject
                {
                    ["id"] = sub.Id.ToString("D"),
                    ["url"] = sub.Url ?? string.Empty,
                    ["title"] = sub.Title ?? string.Empty,
                    ["enabled"] = sub.Enabled,
                    ["createdAt"] = sub.CreatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ["domainCount"] = sub.DomainCount,
                };
                if (sub.UpdatedAt.HasValue)
                    item["updatedAt"] = sub.UpdatedAt.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(sub.LastError))
                    item["lastError"] = sub.LastError;
                subscriptions.Add(item);
            }

            root["subscriptions"] = subscriptions;
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
