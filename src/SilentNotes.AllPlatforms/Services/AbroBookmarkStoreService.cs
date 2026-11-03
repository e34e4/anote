// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroBookmarkStoreService : IAbroBookmarkStoreService
    {
        private readonly object _lock = new object();
        private AbroBookmarkStoreModel _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBookmarkStoreService"/> class.
        /// </summary>
        public AbroBookmarkStoreService()
        {
            string root = AbroAiStoreService.ResolveRootDirectory();
            StoreFilePath = Path.Combine(root, "bookmarks", "store.json");
            LegacyTsvPath = Path.Combine(root, "bookmarks", "bookmarks.txt");
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        private string LegacyTsvPath { get; }

        /// <inheritdoc/>
        public AbroBookmarkStoreModel LoadOrDefault()
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
                        AbroBookmarkStoreModel parsed = AbroBookmarkStoreFormat.Parse(json);
                        if (parsed != null)
                        {
                            _cached = parsed;
                            return _cached;
                        }
                    }

                    if (File.Exists(LegacyTsvPath))
                    {
                        AbroBookmarkStoreModel migrated = MigrateFromTsv(LegacyTsvPath);
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

                _cached = new AbroBookmarkStoreModel();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroBookmarkStoreModel store)
        {
            if (store == null)
                return false;

            lock (_lock)
            {
                return TrySaveUnlocked(store);
            }
        }

        /// <inheritdoc/>
        public bool MergeFromRemote(AbroBookmarkStoreModel remote)
        {
            if (remote == null)
                return false;

            lock (_lock)
            {
                AbroBookmarkStoreModel local = LoadOrDefault();
                bool changed = false;

                foreach (AbroBookmarkItemModel remoteItem in remote.Items ?? new System.Collections.Generic.List<AbroBookmarkItemModel>())
                {
                    if (string.IsNullOrWhiteSpace(remoteItem.Url))
                        continue;

                    AbroBookmarkItemModel localById = local.Items.Find(i => i.Id == remoteItem.Id);
                    if (localById != null)
                    {
                        if (remoteItem.ModifiedAt > localById.ModifiedAt)
                        {
                            remoteItem.CloneTo(localById);
                            changed = true;
                        }
                        continue;
                    }

                    AbroBookmarkItemModel localByUrl = local.Items.Find(i =>
                        string.Equals(i.Url, remoteItem.Url, StringComparison.OrdinalIgnoreCase));
                    if (localByUrl == null)
                    {
                        local.Items.Add(remoteItem);
                        changed = true;
                    }
                    else if (remoteItem.ModifiedAt > localByUrl.ModifiedAt)
                    {
                        remoteItem.CloneTo(localByUrl);
                        changed = true;
                    }
                }

                if (!changed)
                    return false;

                return TrySaveUnlocked(local);
            }
        }

        /// <inheritdoc/>
        public bool AddOrUpdate(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            lock (_lock)
            {
                AbroBookmarkStoreModel store = LoadOrDefault();
                AbroBookmarkItemModel existing = store.Items.Find(i =>
                    string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                        existing.Title = title.Trim();
                    existing.ModifiedAt = DateTime.UtcNow;
                }
                else
                {
                    store.Items.Insert(0, new AbroBookmarkItemModel
                    {
                        Url = url.Trim(),
                        Title = (title ?? string.Empty).Trim(),
                        ModifiedAt = DateTime.UtcNow,
                    });
                }

                return TrySaveUnlocked(store);
            }
        }

        private bool TrySaveUnlocked(AbroBookmarkStoreModel store)
        {
            try
            {
                store.Touch();
                string directory = Path.GetDirectoryName(StoreFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = AbroBookmarkStoreFormat.Serialize(store);
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

        private void WriteLegacyTsv(AbroBookmarkStoreModel store)
        {
            try
            {
                string directory = Path.GetDirectoryName(LegacyTsvPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                foreach (AbroBookmarkItemModel item in store.Items)
                {
                    builder.Append(item.Url ?? string.Empty);
                    builder.Append('\t');
                    builder.Append((item.Title ?? string.Empty).Replace('\t', ' '));
                    builder.AppendLine();
                }
                File.WriteAllText(LegacyTsvPath, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static AbroBookmarkStoreModel MigrateFromTsv(string path)
        {
            var store = new AbroBookmarkStoreModel();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]) || !seen.Add(parts[0].Trim()))
                    continue;
                store.Items.Add(new AbroBookmarkItemModel
                {
                    Url = parts[0].Trim(),
                    Title = parts.Length > 1 ? parts[1].Trim() : string.Empty,
                    ModifiedAt = DateTime.UtcNow,
                });
            }
            return store;
        }
    }
}
