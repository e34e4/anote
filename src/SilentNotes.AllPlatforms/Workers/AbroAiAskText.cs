// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Workers
{
    /// <summary>
    /// Builds prefilled Abro AI chat prompts from aNote content.
    /// </summary>
    public static class AbroAiAskText
    {
        private const int MaxContextChars = 12000;

        /// <summary>
        /// Formats the initial chat input for a content item.
        /// </summary>
        public static string FormatInitialPrompt(string subject, string contextText)
        {
            string title = string.IsNullOrWhiteSpace(subject) ? "Content" : subject.Trim();
            string body = contextText?.Trim() ?? string.Empty;
            if (body.Length > MaxContextChars)
                body = body.Substring(0, MaxContextChars) + "\n…";

            if (string.IsNullOrEmpty(body))
            {
                return "Please help me with: " + title;
            }

            return "I'm asking about: " + title + "\n\n---\n" + body + "\n---\n\n"
                + "Please summarize, explain, or help me with this content.";
        }
    }
}
