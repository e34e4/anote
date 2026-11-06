// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IAbroAiChatLauncher"/>.
    /// </summary>
    internal class AbroAiChatLauncher : IAbroAiChatLauncher
    {
        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public Task AskAiAboutAsync(string subject, string contextText)
        {
            string prompt = AbroAiAskText.FormatInitialPrompt(subject, contextText);
            return MainThread.InvokeOnMainThreadAsync(() => AbroAiChatPage.PresentFromAnoteAsync(prompt));
        }
    }
}
