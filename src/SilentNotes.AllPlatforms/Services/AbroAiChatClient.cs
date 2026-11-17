// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
    /// <remarks>
    /// Requests are streamed (server-sent events). The gateway in front of Command Code answers
    /// a non-streaming request that takes longer than ~120 s with HTTP 524, which is easy to hit
    /// for picture questions; with streaming the bytes keep flowing and the answer arrives.
    /// </remarks>
    public class AbroAiChatClient
    {
        /// <summary>Usage mode sent to Command Code / compatible gateways.</summary>
        public const string DefaultMode = "chat";

        /// <summary>Number of newest picture turns which keep their image bytes in a request.</summary>
        public const int MaxImageTurns = 3;

        /// <summary>Maximum number of images sent in one request.</summary>
        public const int MaxImagesPerRequest = 8;

        /// <summary>Total base64 budget for all images of one request.</summary>
        public const int MaxInlineImageBytes = 3 * 1024 * 1024;

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(9);
        private static readonly TimeSpan TotalBudget = TimeSpan.FromMinutes(8);
        private static readonly int[] RetryStatusCodes = { 502, 503, 504, 524 };
        private static readonly int[] NoStreamStatusCodes = { 400, 404, 405, 422, 501 };
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
        /// Sends the conversation messages to the provider and returns the assistant reply text.
        /// </summary>
        /// <param name="mode">
        /// Optional Command Code usage mode (<c>chat</c>, <c>dialog</c>, <c>agent</c>). Defaults to
        /// <see cref="DefaultMode"/>.
        /// </param>
        /// <param name="threadId">Optional conversation id for multi-turn metering/session grouping.</param>
        /// <param name="onDelta">
        /// Optional callback receiving the answer text piece by piece while it is generated.
        /// </param>
        public async Task<string> CompleteAsync(
            AbroAiProviderModel provider,
            IList<AbroAiMessageModel> messages,
            CancellationToken cancellationToken = default,
            string mode = null,
            string threadId = null,
            Action<string> onDelta = null)
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

            List<object> payloadMessages = BuildPayloadMessages(messages);
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

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TotalBudget);
            CancellationToken token = deadline.Token;

            HttpClient httpClient = CreateHttpClient();
            bool disposeClient = !ReferenceEquals(httpClient, SharedHttpClient);
            try
            {
                bool useStream = true;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        return await SendOnceAsync(
                            httpClient, endpoint, provider, payload, useStream, onDelta, token)
                            .ConfigureAwait(false);
                    }
                    catch (HttpStatusException ex)
                    {
                        if (useStream && Array.IndexOf(NoStreamStatusCodes, (int)ex.StatusCode) >= 0)
                        {
                            // Provider does not implement SSE; retry as a plain request.
                            useStream = false;
                            continue;
                        }

                        if (Array.IndexOf(RetryStatusCodes, (int)ex.StatusCode) >= 0 && attempt < 2)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2 + (3 * attempt)), token)
                                .ConfigureAwait(false);
                            continue;
                        }

                        throw new InvalidOperationException(
                            "API error " + (int)ex.StatusCode + ": " + ex.Snippet, ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (attempt < 2)
                        {
                            // Transient network hiccup; one more try is cheap compared to
                            // losing an already attached picture.
                            await Task.Delay(TimeSpan.FromSeconds(2 + (3 * attempt)), token)
                                .ConfigureAwait(false);
                            continue;
                        }

                        throw new InvalidOperationException("Network error: " + ex.Message, ex);
                    }
                }

                throw new InvalidOperationException("API request failed after retries.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "No answer from the provider after " + (int)TotalBudget.TotalSeconds +
                    "s. Try a smaller picture or a faster model.");
            }
            finally
            {
                if (disposeClient)
                    httpClient.Dispose();
            }
        }

        /// <summary>
        /// Builds the wire messages, deciding which images actually travel with the request.
        /// </summary>
        /// <remarks>
        /// Every turn re-sends the whole history, so old pictures would be uploaded again and
        /// again. Only the newest <see cref="MaxImageTurns"/> picture turns keep their bytes,
        /// duplicate pictures are sent once, and the total inline size is capped.
        /// </remarks>
        private static List<object> BuildPayloadMessages(IList<AbroAiMessageModel> messages)
        {
            Dictionary<int, HashSet<string>> include = SelectImagesToSend(messages);
            var payloadMessages = new List<object>();

            for (int index = 0; index < messages.Count; index++)
            {
                AbroAiMessageModel message = messages[index];
                if (message == null)
                    continue;

                bool hasText = !string.IsNullOrWhiteSpace(message.Content);
                include.TryGetValue(index, out HashSet<string> allowed);
                var images = new List<AbroAiImageAttachment>();
                if (message.Images != null)
                {
                    foreach (AbroAiImageAttachment image in message.Images)
                    {
                        if (image == null || string.IsNullOrWhiteSpace(image.Base64Data))
                            continue;
                        if (allowed != null && allowed.Contains(image.Base64Data))
                            images.Add(image);
                    }
                }

                if (!hasText && images.Count == 0)
                    continue;

                string role = string.IsNullOrWhiteSpace(message.Role) ? "user" : message.Role;
                if (images.Count > 0)
                {
                    var parts = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "text",
                            ["text"] = hasText ? message.Content : "Describe and explain what is in the image(s).",
                        },
                    };

                    foreach (AbroAiImageAttachment image in images)
                    {
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

            return payloadMessages;
        }

        /// <summary>
        /// Returns the base64 payloads to send per message index, newest turns first.
        /// </summary>
        private static Dictionary<int, HashSet<string>> SelectImagesToSend(IList<AbroAiMessageModel> messages)
        {
            var result = new Dictionary<int, HashSet<string>>();
            var taken = new HashSet<string>();
            int budget = MaxInlineImageBytes;
            int slots = MaxImagesPerRequest;
            int pictureTurnsSeen = 0;

            for (int index = messages.Count - 1; index >= 0 && slots > 0; index--)
            {
                AbroAiMessageModel message = messages[index];
                if (message?.Images == null || message.Images.Count == 0)
                    continue;
                if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                    continue;

                pictureTurnsSeen++;
                if (pictureTurnsSeen > MaxImageTurns)
                    break;

                foreach (AbroAiImageAttachment image in message.Images)
                {
                    if (slots <= 0)
                        break;
                    if (image == null || string.IsNullOrWhiteSpace(image.Base64Data))
                        continue;
                    if (!taken.Add(image.Base64Data))
                        continue;
                    if (image.Base64Data.Length > budget)
                        continue;

                    budget -= image.Base64Data.Length;
                    slots--;
                    if (!result.TryGetValue(index, out HashSet<string> forMessage))
                    {
                        forMessage = new HashSet<string>();
                        result[index] = forMessage;
                    }

                    forMessage.Add(image.Base64Data);
                }
            }

            return result;
        }

        /// <summary>
        /// Performs one request, either streaming or plain JSON.
        /// </summary>
        private static async Task<string> SendOnceAsync(
            HttpClient httpClient,
            string endpoint,
            AbroAiProviderModel provider,
            Dictionary<string, object> payload,
            bool useStream,
            Action<string> onDelta,
            CancellationToken cancellationToken)
        {
            var requestPayload = new Dictionary<string, object>(payload);
            if (useStream)
            {
                requestPayload["stream"] = true;
                requestPayload["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true,
                };
            }

            string body = JsonSerializer.Serialize(requestPayload);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            // A plain agent string is required; browser-like headers get 403 from the CDN.
            request.Headers.TryAddWithoutValidation("User-Agent", "anote-abro/1.3");
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());

            using HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string snippet = errorText;
                if (snippet != null && snippet.Length > 400)
                    snippet = snippet.Substring(0, 400) + "…";
                throw new HttpStatusException(response.StatusCode, snippet ?? response.ReasonPhrase);
            }

            string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (useStream && mediaType.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return await ReadEventStreamAsync(response, onDelta, cancellationToken).ConfigureAwait(false);
            }

            // Provider ignored stream=true, or we deliberately asked for plain JSON.
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseCompletion(responseText);
        }

        /// <summary>
        /// Collects an OpenAI-style SSE stream into the answer text.
        /// </summary>
        private static async Task<string> ReadEventStreamAsync(
            HttpResponseMessage response,
            Action<string> onDelta,
            CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            try
            {
                while (true)
                {
                    string line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null)
                        break;
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith(":", StringComparison.Ordinal))
                        continue;
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                        continue;

                    string chunk = line.Substring(5).Trim();
                    if (chunk.Length == 0)
                        continue;
                    if (string.Equals(chunk, "[DONE]", StringComparison.Ordinal))
                        break;

                    string piece = ExtractDeltaText(chunk);
                    if (!string.IsNullOrEmpty(piece))
                    {
                        text.Append(piece);
                        onDelta?.Invoke(piece);
                    }
                }
            }
            catch (Exception ex) when (
                text.Length > 0
                && !cancellationToken.IsCancellationRequested
                && (ex is IOException || ex is HttpRequestException))
            {
                // A connection dropped mid-answer. A partial reply is more useful than an
                // error, so keep what arrived and say that it was cut off.
                text.Append("\n\n_(response cut off: " + ex.Message + ")_");
            }

            if (text.Length == 0)
                throw new InvalidOperationException("API response did not contain a chat message.");

            return text.ToString();
        }

        /// <summary>
        /// Extracts the text of one stream chunk, tolerating gateways that send whole messages.
        /// </summary>
        private static string ExtractDeltaText(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
                    throw new InvalidOperationException("API error: " + error.ToString());

                if (!root.TryGetProperty("choices", out JsonElement choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return null;
                }

                JsonElement first = choices[0];
                if (first.TryGetProperty("delta", out JsonElement delta)
                    && delta.TryGetProperty("content", out JsonElement deltaContent)
                    && deltaContent.ValueKind == JsonValueKind.String)
                {
                    return deltaContent.GetString();
                }

                if (first.TryGetProperty("message", out JsonElement message)
                    && message.TryGetProperty("content", out JsonElement messageContent)
                    && messageContent.ValueKind == JsonValueKind.String)
                {
                    return messageContent.GetString();
                }

                if (first.TryGetProperty("text", out JsonElement text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
            catch (JsonException)
            {
                // Ignore malformed keep-alive chunks.
            }

            return null;
        }

        /// <summary>
        /// Reads the assistant text out of a plain (non-streamed) chat completion document.
        /// </summary>
        private static string ParseCompletion(string responseText)
        {
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

        /// <summary>
        /// A non-success response which the retry logic can act upon.
        /// </summary>
        private sealed class HttpStatusException : Exception
        {
            public HttpStatusException(HttpStatusCode statusCode, string snippet)
                : base("API error " + (int)statusCode + ": " + snippet)
            {
                StatusCode = statusCode;
                Snippet = snippet;
            }

            public HttpStatusCode StatusCode { get; }

            public string Snippet { get; }
        }
    }
}
