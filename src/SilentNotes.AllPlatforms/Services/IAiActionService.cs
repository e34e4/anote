// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>Explicit AI actions which can be applied to selected text or a whole item.</summary>
    public enum AiTextAction
    {
        /// <summary>Create a short summary.</summary>
        Summarize,

        /// <summary>Translate the text into another language.</summary>
        Translate,

        /// <summary>Rewrite the text more clearly and concisely.</summary>
        Rewrite,

        /// <summary>Run a free-form prompt.</summary>
        Custom,
    }

    /// <summary>
    /// Runs explicit AI actions (summarize, translate, rewrite) with the configured
    /// OpenAI-compatible provider, without opening the chat page.
    /// </summary>
    public interface IAiActionService
    {
        /// <summary>Gets a value indicating whether at least one usable AI provider is configured.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Runs an action and returns the generated text.
        /// </summary>
        /// <param name="action">The action to run.</param>
        /// <param name="text">The text to work on.</param>
        /// <param name="targetLanguage">Language for <see cref="AiTextAction.Translate"/>.</param>
        /// <param name="customPrompt">Prompt for <see cref="AiTextAction.Custom"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<string> RunAsync(AiTextAction action, string text, string targetLanguage = null, string customPrompt = null, CancellationToken cancellationToken = default);
    }
}
