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
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Calls an OpenAI-compatible chat completions endpoint.
    /// </summary>
    public class AbroAiChatClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
        private static readonly HttpClient SharedHttpClient = new HttpClient
        {
            Timeout = DefaultTimeout,
        };

        private readonly ISettingsService _settingsService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiChatClient"/> class.
        /// </summary>
        public AbroAiChatClient(ISettingsService settingsService = null)
        {
            _settingsService = settingsService;
        }

        /// <summary>
        /// Usage mode sent to Command Code / compatible gateways so Studio meters the call as
        /// chat rather than an internal feature like title-gen. Matches the CLI <c>mode</c> field.
        /// </summary>
        public const string DefaultMode = "chat";

        /// <summary>
        /// Sends the conversation messages to the provider and returns the assistant reply text.
        /// </summary>
        /// <param name="mode">
        /// Optional Command Code usage mode (<c>chat</c>, <c>dialog</c>, <c>agent</c>). Defaults to
        /// <see cref="DefaultMode"/>.
        /// </param>
        /// <param name="threadId">Optional conversation id for multi-turn metering/session grouping.</param>
        public async Task<string> CompleteAsync(
            AbroAiProviderModel provider,
            IList<AbroAiMessageModel> messages,
            CancellationToken cancellationToken = default,
            string mode = null,
            string threadId = null)
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
                if (message == null)
                    continue;

                bool hasText = !string.IsNullOrWhiteSpace(message.Content);
                bool hasImages = message.Images != null && message.Images.Count > 0;
                if (!hasText && !hasImages)
                    continue;

                string role = string.IsNullOrWhiteSpace(message.Role) ? "user" : message.Role;
                if (hasImages)
                {
                    var parts = new List<object>();
                    if (hasText)
                    {
                        parts.Add(new Dictionary<string, object>
                        {
                            ["type"] = "text",
                            ["text"] = message.Content,
                        });
                    }
                    else
                    {
                        parts.Add(new Dictionary<string, object>
                        {
                            ["type"] = "text",
                            ["text"] = "Describe and explain what is in the image(s).",
                        });
                    }

                    foreach (AbroAiImageAttachment image in message.Images)
                    {
                        if (image == null || string.IsNullOrWhiteSpace(image.Base64Data))
                            continue;
                        string mime = string.IsNullOrWhiteSpace(image.MimeType) ? "image/jpeg" : image.MimeType.Trim();
                        string dataUrl = "data:" + mime + ";base64," + image.Base64Data;
                        parts.Add(new Dictionary<string, object>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object>
                            {
                                ["url"] = dataUrl,
                            },
                        });
                    }

                    payloadMessages.Add(new Dictionary<string, object>
                    {
                        ["role"] = role,
                        ["content"] = parts,
                    });
                }
                else
                {
                    payloadMessages.Add(new Dictionary<string, object>
                    {
                        ["role"] = role,
                        ["content"] = message.Content,
                    });
                }
            }

            if (payloadMessages.Count == 0)
                throw new InvalidOperationException("No messages to send.");

            // Include Command Code's usage "mode" so Studio does not label Abro chats as title-gen.
            var payload = new Dictionary<string, object>
            {
                ["model"] = provider.ModelId,
                ["messages"] = payloadMessages,
                ["mode"] = string.IsNullOrWhiteSpace(mode) ? DefaultMode : mode.Trim(),
            };
            if (!string.IsNullOrWhiteSpace(threadId))
                payload["threadId"] = threadId.Trim();

            string body = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", "anote-abro/1.3");
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());

            HttpClient httpClient = CreateHttpClient();
            bool disposeClient = !ReferenceEquals(httpClient, SharedHttpClient);
            try
            {
                using HttpResponseMessage response = await httpClient
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
            finally
            {
                if (disposeClient)
                    httpClient.Dispose();
            }
        }

        private HttpClient CreateHttpClient()
        {
            SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
            if (settings != null && settings.Socks5Enabled && settings.AiUseProxy && settings.HasSocks5Config)
            {
                HttpMessageHandler handler = Socks5Helper.CreateHttpHandler(settings);
                return new HttpClient(handler, disposeHandler: true) { Timeout = DefaultTimeout };
            }

            return SharedHttpClient;
        }
    }
}
