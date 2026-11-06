// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Builds prefilled Abro AI chat prompts from aNote content.
    /// </summary>
    public static class AbroAiAskText
    {
        private const int MaxContextChars = 48000;

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

        /// <summary>
        /// Formats a file attachment block that can be appended to a chat message.
        /// </summary>
        public static string FormatAttachedFile(string fileName, string fileText)
        {
            string name = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim();
            string body = fileText ?? string.Empty;
            if (body.Length > MaxContextChars)
                body = body.Substring(0, MaxContextChars) + "\n…";

            return "Attached file: " + name + "\n\n---\n" + body + "\n---";
        }

        /// <summary>
        /// Tries to decode bytes as UTF-8 text. Returns false for likely-binary content.
        /// </summary>
        public static bool TryDecodeText(byte[] bytes, out string text)
        {
            text = string.Empty;
            if (bytes == null || bytes.Length == 0)
            {
                text = string.Empty;
                return true;
            }

            // Heuristic: too many NUL bytes → binary.
            int sample = Math.Min(bytes.Length, 4096);
            int nulCount = 0;
            for (int i = 0; i < sample; i++)
            {
                if (bytes[i] == 0)
                    nulCount++;
            }

            if (nulCount > 2)
                return false;

            try
            {
                text = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
