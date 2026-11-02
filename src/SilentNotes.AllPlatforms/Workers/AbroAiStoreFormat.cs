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
    /// Reads and writes the Abro AI store JSON used locally and on WebDAV.
    /// </summary>
    public static class AbroAiStoreFormat
    {
        /// <summary>
        /// Parses store JSON. Returns an empty store for blank input, or null if invalid.
        /// </summary>
        public static AbroAiStoreModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroAiStoreModel();

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

            var store = new AbroAiStoreModel();
            if (obj["version"] != null && obj["version"].GetValueKind() == JsonValueKind.Number)
                store.Version = obj["version"].GetValue<int>();
            if (TryGetUtcDateTime(obj, "updatedAt", out DateTime updatedAt))
                store.UpdatedAt = updatedAt;
            if (TryGetGuid(obj, "defaultProviderId", out Guid defaultId))
                store.DefaultProviderId = defaultId;

            if (obj["providers"] is JsonArray providers)
            {
                foreach (JsonNode node in providers)
                {
                    if (node is not JsonObject item)
                        continue;
                    var provider = new AbroAiProviderModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        provider.Id = id;
                    provider.Name = GetString(item, "name") ?? string.Empty;
                    provider.Url = GetString(item, "url") ?? string.Empty;
                    provider.ModelId = GetString(item, "modelId") ?? string.Empty;
                    provider.ApiKey = GetString(item, "apiKey") ?? string.Empty;
                    if (TryGetUtcDateTime(item, "modifiedAt", out DateTime modifiedAt))
                        provider.ModifiedAt = modifiedAt;
                    store.Providers.Add(provider);
                }
            }

            if (obj["conversations"] is JsonArray conversations)
            {
                foreach (JsonNode node in conversations)
                {
                    if (node is not JsonObject item)
                        continue;
                    var conversation = new AbroAiConversationModel();
                    if (TryGetGuid(item, "id", out Guid id))
                        conversation.Id = id;
                    conversation.Title = GetString(item, "title") ?? string.Empty;
                    if (TryGetUtcDateTime(item, "createdAt", out DateTime createdAt))
                        conversation.CreatedAt = createdAt;
                    if (TryGetUtcDateTime(item, "modifiedAt", out DateTime modifiedAt))
                        conversation.ModifiedAt = modifiedAt;
                    if (TryGetGuid(item, "providerId", out Guid providerId))
                        conversation.ProviderId = providerId;

                    if (item["messages"] is JsonArray messages)
                    {
                        conversation.Messages = new List<AbroAiMessageModel>();
                        foreach (JsonNode messageNode in messages)
                        {
                            if (messageNode is not JsonObject messageObj)
                                continue;
                            var message = new AbroAiMessageModel
                            {
                                Role = GetString(messageObj, "role") ?? "user",
                                Content = GetString(messageObj, "content") ?? string.Empty,
                            };
                            if (TryGetUtcDateTime(messageObj, "at", out DateTime at))
                                message.At = at;
                            conversation.Messages.Add(message);
                        }
                    }

                    store.Conversations.Add(conversation);
                }
            }

            return store;
        }

        /// <summary>
        /// Serializes the store to pretty-printed UTF-8 JSON.
        /// </summary>
        public static string Serialize(AbroAiStoreModel store)
        {
            store ??= new AbroAiStoreModel();
            store.TrimConversations();

            var providers = new JsonArray();
            foreach (AbroAiProviderModel provider in store.Providers ?? new List<AbroAiProviderModel>())
            {
                providers.Add(new JsonObject
                {
                    ["id"] = provider.Id.ToString(),
                    ["name"] = provider.Name ?? string.Empty,
                    ["url"] = provider.Url ?? string.Empty,
                    ["modelId"] = provider.ModelId ?? string.Empty,
                    ["apiKey"] = provider.ApiKey ?? string.Empty,
                    ["modifiedAt"] = ToIso(provider.ModifiedAt),
                });
            }

            var conversations = new JsonArray();
            foreach (AbroAiConversationModel conversation in store.Conversations ?? new List<AbroAiConversationModel>())
            {
                var messages = new JsonArray();
                foreach (AbroAiMessageModel message in conversation.Messages ?? new List<AbroAiMessageModel>())
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = message.Role ?? "user",
                        ["content"] = message.Content ?? string.Empty,
                        ["at"] = ToIso(message.At),
                    });
                }

                var item = new JsonObject
                {
                    ["id"] = conversation.Id.ToString(),
                    ["title"] = conversation.Title ?? string.Empty,
                    ["createdAt"] = ToIso(conversation.CreatedAt),
                    ["modifiedAt"] = ToIso(conversation.ModifiedAt),
                    ["messages"] = messages,
                };
                if (conversation.ProviderId.HasValue)
                    item["providerId"] = conversation.ProviderId.Value.ToString();
                conversations.Add(item);
            }

            var root = new JsonObject
            {
                ["version"] = store.Version > 0 ? store.Version : 1,
                ["updatedAt"] = ToIso(store.UpdatedAt == default ? DateTime.UtcNow : store.UpdatedAt),
                ["providers"] = providers,
                ["conversations"] = conversations,
            };
            if (store.DefaultProviderId.HasValue)
                root["defaultProviderId"] = store.DefaultProviderId.Value.ToString();

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
