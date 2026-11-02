// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Calls an OpenAI-compatible chat completions endpoint.
    /// </summary>
    public class AbroAiChatClient
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2),
        };

        /// <summary>
        /// Sends the conversation messages to the provider and returns the assistant reply text.
        /// </summary>
        public async Task<string> CompleteAsync(
            AbroAiProviderModel provider,
            IList<AbroAiMessageModel> messages,
            CancellationToken cancellationToken = default)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.Url))
                throw new InvalidOperationException("Provider URL is empty.");
            if (string.IsNullOrWhiteSpace(provider.ModelId))
                throw new InvalidOperationException("Provider model id is empty.");
            if (messages == null || messages.Count == 0)
                throw new InvalidOperationException("No messages to send.");

            string baseUrl = provider.Url.Trim().TrimEnd('/');
            string endpoint = baseUrl + "/chat/completions";

            var payloadMessages = new List<object>();
            foreach (AbroAiMessageModel message in messages)
            {
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;
                payloadMessages.Add(new
                {
                    role = string.IsNullOrWhiteSpace(message.Role) ? "user" : message.Role,
                    content = message.Content,
                });
            }

            string body = JsonSerializer.Serialize(new
            {
                model = provider.ModelId,
                messages = payloadMessages,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());

            using HttpResponseMessage response = await SharedHttpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string snippet = responseText;
                if (snippet != null && snippet.Length > 400)
                    snippet = snippet.Substring(0, 400) + "…";
                throw new InvalidOperationException(
                    "API error " + (int)response.StatusCode + ": " + (snippet ?? response.ReasonPhrase));
            }

            using JsonDocument document = JsonDocument.Parse(responseText);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                JsonElement first = choices[0];
                if (first.TryGetProperty("message", out JsonElement message)
                    && message.TryGetProperty("content", out JsonElement content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }

                // Some providers put the reply in text instead of message.content
                if (first.TryGetProperty("text", out JsonElement text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }

            throw new InvalidOperationException("API response did not contain a chat message.");
        }
    }
}
