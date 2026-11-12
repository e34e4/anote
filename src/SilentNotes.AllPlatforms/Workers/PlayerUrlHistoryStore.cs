// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Persists recently used Player-tab URLs (newest first) under the anote storage root.
    /// </summary>
    public static class PlayerUrlHistoryStore
    {
        private const int MaxEntries = 40;
        private static readonly object Gate = new object();

        public static string HistoryFile => Path.Combine(AnotePaths.GetRootDirectory(), "player-url-history.json");

        public static IReadOnlyList<string> Load()
        {
            lock (Gate)
                return LoadUnlocked();
        }

        public static void Add(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            string trimmed = url.Trim();
            lock (Gate)
            {
                try
                {
                    AnotePaths.EnsureDirectories();
                    var list = LoadUnlocked().ToList();
                    list.RemoveAll(u => string.Equals(u, trimmed, StringComparison.OrdinalIgnoreCase));
                    list.Insert(0, trimmed);
                    if (list.Count > MaxEntries)
                        list = list.Take(MaxEntries).ToList();
                    SaveLocked(list);
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Warn("Player", "URL history save failed: " + ex.Message);
                }
            }
        }

        private static IReadOnlyList<string> LoadUnlocked()
        {
            try
            {
                string path = HistoryFile;
                if (!File.Exists(path))
                    return Array.Empty<string>();

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return Array.Empty<string>();

                List<string> items = JsonSerializer.Deserialize<List<string>>(json);
                if (items == null || items.Count == 0)
                    return Array.Empty<string>();

                return items
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxEntries)
                    .ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                try
                {
                    string path = HistoryFile;
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Warn("Player", "URL history clear failed: " + ex.Message);
                }
            }
        }

        private static void SaveLocked(List<string> items)
        {
            string path = HistoryFile;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
