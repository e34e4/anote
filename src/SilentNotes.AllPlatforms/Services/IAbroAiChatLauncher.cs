// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Opens the Abro AI chat UI with optional prefilled context (Android).
    /// </summary>
    public interface IAbroAiChatLauncher
    {
        /// <summary>Gets a value indicating whether Ask AI is available on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Opens AI chat with a draft user message built from <paramref name="subject"/> and
        /// <paramref name="contextText"/>.
        /// </summary>
        Task AskAiAboutAsync(string subject, string contextText);
    }
}
