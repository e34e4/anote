// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroHistoryStoreService : IAbroHistoryStoreService
    {
        private readonly object _lock = new object();
        private AbroHistoryStoreModel _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroHistoryStoreService"/> class.
        /// </summary>
        public AbroHistoryStoreService()
        {
            string root = AbroAiStoreService.ResolveRootDirectory();
            StoreFilePath = Path.Combine(root, "history", "store.json");
            LegacyTsvPath = Path.Combine(root, "history", "history.txt");
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        private string LegacyTsvPath { get; }

        /// <inheritdoc/>
        public AbroHistoryStoreModel LoadOrDefault()
        {
            lock (_lock)
            {
                if (_cached != null)
                    return _cached;

                try
                {
                    if (File.Exists(StoreFilePath))
                    {
                        string json = File.ReadAllText(StoreFilePath, Encoding.UTF8);
                        AbroHistoryStoreModel parsed = AbroHistoryStoreFormat.Parse(json);
                        if (parsed != null)
                        {
                            _cached = parsed;
                            return _cached;
                        }
                    }

                    if (File.Exists(LegacyTsvPath))
                    {
                        AbroHistoryStoreModel migrated = MigrateFromTsv(LegacyTsvPath);
                        if (migrated.Items.Count > 0)
                        {
                            TrySaveUnlocked(migrated);
                            return _cached;
                        }
                    }
                }
                catch (Exception)
                {
                }

                _cached = new AbroHistoryStoreModel();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroHistoryStoreModel store)
        {
            if (store == null)
                return false;

            lock (_lock)
            {
                return TrySaveUnlocked(store);
            }
        }

        /// <inheritdoc/>
        public bool MergeFromRemote(AbroHistoryStoreModel remote)
        {
            if (remote == null)
                return false;

            lock (_lock)
            {
                AbroHistoryStoreModel local = LoadOrDefault();
                bool changed = false;

                foreach (AbroHistoryItemModel remoteItem in remote.Items ?? new System.Collections.Generic.List<AbroHistoryItemModel>())
                {
                    if (string.IsNullOrWhiteSpace(remoteItem.Url))
                        continue;

                    // One history row per URL: if the link already exists locally, never add another.
                    AbroHistoryItemModel localMatch = local.Items.Find(i =>
                        i.Id == remoteItem.Id
                        || string.Equals(i.Url, remoteItem.Url, StringComparison.OrdinalIgnoreCase));
                    if (localMatch != null)
                    {
                        if (remoteItem.VisitedAt > localMatch.VisitedAt)
                        {
                            Guid keepId = localMatch.Id;
                            remoteItem.CloneTo(localMatch);
                            localMatch.Id = keepId;
                            localMatch.Url = remoteItem.Url?.Trim() ?? localMatch.Url;
                            changed = true;
                        }
                        continue;
                    }

                    local.Items.Add(remoteItem);
                    changed = true;
                }

                if (DeduplicateByUrl(local))
                    changed = true;

                if (!changed)
                    return false;

                return TrySaveUnlocked(local);
            }
        }

        /// <inheritdoc/>
        public bool AppendVisit(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            lock (_lock)
            {
                AbroHistoryStoreModel store = LoadOrDefault();
                AbroHistoryItemModel existing = store.Items.Find(i =>
                    string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                        existing.Title = title.Trim();
                    existing.VisitedAt = DateTime.UtcNow;
                }
                else
                {
                    store.Items.Add(new AbroHistoryItemModel
                    {
                        Url = url.Trim(),
                        Title = (title ?? string.Empty).Trim(),
                        VisitedAt = DateTime.UtcNow,
                    });
                }

                return TrySaveUnlocked(store);
            }
        }

        /// <inheritdoc/>
        public bool RemoveByUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            lock (_lock)
            {
                AbroHistoryStoreModel store = LoadOrDefault();
                int removed = store.Items.RemoveAll(i =>
                    string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    return false;
                return TrySaveUnlocked(store);
            }
        }

        /// <inheritdoc/>
        public bool ClearAll()
        {
            lock (_lock)
            {
                var store = new AbroHistoryStoreModel();
                return TrySaveUnlocked(store);
            }
        }

        private static bool DeduplicateByUrl(AbroHistoryStoreModel store)
        {
            if (store?.Items == null || store.Items.Count < 2)
                return false;

            bool changed = false;
            var kept = new System.Collections.Generic.List<AbroHistoryItemModel>();
            var seen = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AbroHistoryItemModel item in store.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                {
                    changed = true;
                    continue;
                }

                string key = item.Url.Trim();
                if (seen.TryGetValue(key, out int index))
                {
                    changed = true;
                    AbroHistoryItemModel existing = kept[index];
                    if (item.VisitedAt > existing.VisitedAt)
                        item.CloneTo(existing);
                    continue;
                }

                item.Url = key;
                seen[key] = kept.Count;
                kept.Add(item);
            }

            if (changed)
                store.Items = kept;
            return changed;
        }

        private bool TrySaveUnlocked(AbroHistoryStoreModel store)
        {
            try
            {
                DeduplicateByUrl(store);
                store.TrimItems();
                store.Touch();
                string directory = Path.GetDirectoryName(StoreFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = AbroHistoryStoreFormat.Serialize(store);
                string temp = StoreFilePath + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                if (File.Exists(StoreFilePath))
                    File.Delete(StoreFilePath);
                File.Move(temp, StoreFilePath);
                WriteLegacyTsv(store);
                _cached = store;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void WriteLegacyTsv(AbroHistoryStoreModel store)
        {
            try
            {
                string directory = Path.GetDirectoryName(LegacyTsvPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                foreach (AbroHistoryItemModel item in store.Items.OrderBy(i => i.VisitedAt))
                {
                    builder.Append(item.VisitedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    builder.Append('\t');
                    builder.Append(item.Url ?? string.Empty);
                    builder.Append('\t');
                    builder.Append((item.Title ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                    builder.AppendLine();
                }
                File.WriteAllText(LegacyTsvPath, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static AbroHistoryStoreModel MigrateFromTsv(string path)
        {
            var store = new AbroHistoryStoreModel();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach (string line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    continue;

                DateTime visitedAt = DateTime.UtcNow;
                if (DateTime.TryParseExact(
                    parts[0],
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out DateTime localVisit))
                {
                    visitedAt = localVisit.ToUniversalTime();
                }

                store.Items.Add(new AbroHistoryItemModel
                {
                    Url = parts[1].Trim(),
                    Title = parts.Length > 2 ? parts[2].Trim() : string.Empty,
                    VisitedAt = visitedAt,
                });
            }

            store.TrimItems();
            return store;
        }
    }
}
