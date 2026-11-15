// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AiActionService : IAiActionService
    {
        private const int MaxTextChars = 48000;
        private const string SystemPrompt =
            "You are a precise assistant embedded in a private note app. " +
            "Answer only with the requested result, without introductions, explanations or markdown fences.";

        private readonly AbroAiChatClient _chatClient;
        private readonly IAbroAiStoreService _storeService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AiActionService"/> class.
        /// </summary>
        public AiActionService(AbroAiChatClient chatClient, IAbroAiStoreService storeService)
        {
            _chatClient = chatClient;
            _storeService = storeService;
        }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                AbroAiProviderModel provider = GetProvider();
                return provider != null
                    && !string.IsNullOrWhiteSpace(provider.Url)
                    && !string.IsNullOrWhiteSpace(provider.ModelId);
            }
        }

        /// <inheritdoc/>
        public async Task<string> RunAsync(AiTextAction action, string text, string targetLanguage = null, string customPrompt = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("There is no text to work on.");

            AbroAiProviderModel provider = GetProvider();
            if (provider == null || string.IsNullOrWhiteSpace(provider.Url) || string.IsNullOrWhiteSpace(provider.ModelId))
                throw new InvalidOperationException("No AI provider is configured.");

            string body = text.Trim();
            if (body.Length > MaxTextChars)
                body = body.Substring(0, MaxTextChars) + "\n…";

            string prompt = BuildPrompt(action, targetLanguage, customPrompt);
            var messages = new List<AbroAiMessageModel>
            {
                new AbroAiMessageModel { Role = "system", Content = SystemPrompt },
                new AbroAiMessageModel { Role = "user", Content = prompt + "\n\n" + body },
            };

            string result = await _chatClient.CompleteAsync(provider, messages, cancellationToken).ConfigureAwait(false);
            return (result ?? string.Empty).Trim();
        }

        private AbroAiProviderModel GetProvider()
        {
            try
            {
                return _storeService?.LoadOrDefault()?.GetDefaultProvider();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string BuildPrompt(AiTextAction action, string targetLanguage, string customPrompt)
        {
            string language = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
            switch (action)
            {
                case AiTextAction.Summarize:
                    return "Summarize the following text in a few short sentences or bullet points. Keep the language of the original text.";
                case AiTextAction.Translate:
                    return "Translate the following text into " + language + ". Return only the translation, keep the formatting as far as possible.";
                case AiTextAction.Rewrite:
                    return "Rewrite the following text so it is clearer and more concise. Keep the meaning, facts and language of the original.";
                case AiTextAction.Custom:
                    return string.IsNullOrWhiteSpace(customPrompt) ? "Process the following text." : customPrompt.Trim();
                default:
                    return "Process the following text.";
            }
        }
    }
}
