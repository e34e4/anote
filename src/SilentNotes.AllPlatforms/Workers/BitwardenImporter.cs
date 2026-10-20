// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Imports login items from an unencrypted Bitwarden JSON or CSV export.
    /// </summary>
    public static class BitwardenImporter
    {
        /// <summary>
        /// Parses Bitwarden export text (JSON or CSV) into password entries.
        /// </summary>
        /// <param name="text">File contents.</param>
        /// <returns>Imported entries, or null if the format is not recognized.</returns>
        public static List<PasswordEntryModel> Import(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                return ImportJson(trimmed);
            if (trimmed.Contains("login_username", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("login_password", StringComparison.OrdinalIgnoreCase))
                return ImportCsv(trimmed);
            return null;
        }

        private static List<PasswordEntryModel> ImportJson(string text)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(text))
                {
                    JsonElement root = document.RootElement;
                    JsonElement items;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out items))
                    {
                        // Bitwarden export root
                    }
                    else if (root.ValueKind == JsonValueKind.Array)
                    {
                        items = root;
                    }
                    else
                    {
                        return null;
                    }

                    if (items.ValueKind != JsonValueKind.Array)
                        return null;

                    var result = new List<PasswordEntryModel>();
                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        // type 1 = login; omit if present and not login
                        if (item.TryGetProperty("type", out JsonElement typeElement) &&
                            typeElement.ValueKind == JsonValueKind.Number &&
                            typeElement.GetInt32() != 1)
                        {
                            continue;
                        }

                        if (!item.TryGetProperty("login", out JsonElement login) ||
                            login.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var entry = new PasswordEntryModel();
                        if (item.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                            entry.Name = name.GetString();
                        if (item.TryGetProperty("notes", out JsonElement notes) && notes.ValueKind == JsonValueKind.String)
                            entry.Notes = notes.GetString();
                        if (login.TryGetProperty("username", out JsonElement username) && username.ValueKind == JsonValueKind.String)
                            entry.Username = username.GetString();
                        if (login.TryGetProperty("password", out JsonElement password) && password.ValueKind == JsonValueKind.String)
                            entry.Password = password.GetString();
                        if (login.TryGetProperty("totp", out JsonElement totp) && totp.ValueKind == JsonValueKind.String)
                            entry.TotpSecret = totp.GetString();
                        if (login.TryGetProperty("uris", out JsonElement uris) && uris.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement uriItem in uris.EnumerateArray())
                            {
                                string uri = null;
                                if (uriItem.ValueKind == JsonValueKind.Object &&
                                    uriItem.TryGetProperty("uri", out JsonElement uriValue) &&
                                    uriValue.ValueKind == JsonValueKind.String)
                                {
                                    uri = uriValue.GetString();
                                }
                                else if (uriItem.ValueKind == JsonValueKind.String)
                                {
                                    uri = uriItem.GetString();
                                }
                                if (!string.IsNullOrWhiteSpace(uri))
                                    entry.Uris.Add(uri.Trim());
                            }
                        }

                        if (string.IsNullOrWhiteSpace(entry.Name) &&
                            string.IsNullOrWhiteSpace(entry.Username) &&
                            string.IsNullOrWhiteSpace(entry.Password))
                        {
                            continue;
                        }

                        result.Add(entry);
                    }
                    return result;
                }
            }
            catch
            {
                return null;
            }
        }

        private static List<PasswordEntryModel> ImportCsv(string text)
        {
            try
            {
                string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                if (lines.Length < 2)
                    return null;

                List<string> headers = ParseCsvLine(lines[0]);
                int nameIdx = IndexOfHeader(headers, "name");
                int notesIdx = IndexOfHeader(headers, "notes");
                int usernameIdx = IndexOfHeader(headers, "login_username", "username");
                int passwordIdx = IndexOfHeader(headers, "login_password", "password");
                int totpIdx = IndexOfHeader(headers, "login_totp", "totp");
                int uriIdx = IndexOfHeader(headers, "login_uri", "uri");

                if (usernameIdx < 0 && passwordIdx < 0)
                    return null;

                var result = new List<PasswordEntryModel>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;
                    List<string> cols = ParseCsvLine(lines[i]);
                    var entry = new PasswordEntryModel
                    {
                        Name = GetCol(cols, nameIdx),
                        Notes = GetCol(cols, notesIdx),
                        Username = GetCol(cols, usernameIdx),
                        Password = GetCol(cols, passwordIdx),
                        TotpSecret = GetCol(cols, totpIdx),
                    };
                    string uri = GetCol(cols, uriIdx);
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        foreach (string part in uri.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            entry.Uris.Add(part.Trim());
                    }

                    if (string.IsNullOrWhiteSpace(entry.Name) &&
                        string.IsNullOrWhiteSpace(entry.Username) &&
                        string.IsNullOrWhiteSpace(entry.Password))
                    {
                        continue;
                    }
                    result.Add(entry);
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static int IndexOfHeader(List<string> headers, params string[] names)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i]?.Trim();
                if (string.IsNullOrEmpty(h))
                    continue;
                foreach (string name in names)
                {
                    if (string.Equals(h, name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        private static string GetCol(List<string> cols, int index)
        {
            if (index < 0 || index >= cols.Count)
                return null;
            string value = cols[index]?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null)
                return result;

            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        /// <summary>
        /// Returns true when an existing entry looks like a duplicate of the candidate.
        /// </summary>
        public static bool IsDuplicate(PasswordEntryModel existing, PasswordEntryModel candidate)
        {
            if (existing == null || candidate == null)
                return false;
            bool sameName = string.Equals(existing.Name ?? string.Empty, candidate.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            bool sameUser = string.Equals(existing.Username ?? string.Empty, candidate.Username ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            string existingUri = existing.Uris.FirstOrDefault() ?? string.Empty;
            string candidateUri = candidate.Uris.FirstOrDefault() ?? string.Empty;
            bool sameUri = string.Equals(existingUri, candidateUri, StringComparison.OrdinalIgnoreCase);
            return sameName && sameUser && sameUri;
        }
    }
}
