// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Imports a list of contacts from the file format used by the standalone contacts app. The
    /// exported file is a self contained HTML page which embeds the contacts as a JavaScript array
    /// <c>const SEED = [...];</c>. A plain JSON array (as stored in the app's contacts.json) is
    /// supported as well.
    /// </summary>
    public static class ContactsImporter
    {
        private const string SeedMarker = "const SEED = ";

        /// <summary>
        /// Parses the given text and creates a list of contacts from it.
        /// </summary>
        /// <param name="text">The content of a contacts.html or contacts.json file.</param>
        /// <returns>The imported contacts, or null if the text does not contain a valid contact
        /// list.</returns>
        public static ContactListModel Import(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Prefer the JavaScript marker of the exported HTML page.
            string json = null;
            int markerIndex = text.IndexOf(SeedMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
                json = ExtractJsonArray(text, markerIndex);

            // Fall back to a plain JSON array (e.g. a contacts.json file).
            if (json == null)
                json = ExtractJsonArray(text, 0);

            if (json == null)
                return null;

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                        return null;

                    var result = new ContactListModel();
                    foreach (JsonElement element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object)
                            continue;

                        var contact = new ContactModel();
                        if (element.TryGetProperty("name", out JsonElement nameElement) &&
                            (nameElement.ValueKind == JsonValueKind.String))
                        {
                            contact.Name = nameElement.GetString();
                        }

                        if (element.TryGetProperty("phones", out JsonElement phonesElement) &&
                            (phonesElement.ValueKind == JsonValueKind.Array))
                        {
                            foreach (JsonElement phoneElement in phonesElement.EnumerateArray())
                            {
                                if ((phoneElement.ValueKind == JsonValueKind.String) ||
                                    (phoneElement.ValueKind == JsonValueKind.Number))
                                {
                                    contact.Phones.Add(phoneElement.ToString());
                                }
                            }
                        }

                        result.Add(contact);
                    }
                    return result;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a balanced JSON array from the text, starting at the first '[' after
        /// <paramref name="from"/>. Bracket depth and string escaping are respected, so brackets
        /// inside string values do not end the array prematurely.
        /// </summary>
        /// <param name="text">The text to scan.</param>
        /// <param name="from">Start searching for the array at this index.</param>
        /// <returns>The JSON array as string, or null if no balanced array was found.</returns>
        internal static string ExtractJsonArray(string text, int from)
        {
            int start = text.IndexOf('[', from);
            if (start < 0)
                return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (ch == '\\')
                        escaped = true;
                    else if (ch == '"')
                        inString = false;
                    continue;
                }

                if (ch == '"')
                    inString = true;
                else if (ch == '[')
                    depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }
    }
}
