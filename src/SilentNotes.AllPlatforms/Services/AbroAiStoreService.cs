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
    public class AbroAiStoreService : IAbroAiStoreService
    {
        private readonly object _lock = new object();
        private AbroAiStoreModel _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiStoreService"/> class.
        /// </summary>
        public AbroAiStoreService()
        {
            StoreFilePath = Path.Combine(ResolveRootDirectory(), "ai", "store.json");
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        /// <inheritdoc/>
        public AbroAiStoreModel LoadOrDefault()
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
                        AbroAiStoreModel parsed = AbroAiStoreFormat.Parse(json);
                        if (parsed != null)
                        {
                            _cached = parsed;
                            return _cached;
                        }
                    }
                }
                catch (Exception)
                {
                }

                _cached = new AbroAiStoreModel();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroAiStoreModel store)
        {
            if (store == null)
                return false;

            lock (_lock)
            {
                try
                {
                    store.TrimConversations();
                    store.Touch();
                    string directory = Path.GetDirectoryName(StoreFilePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    string json = AbroAiStoreFormat.Serialize(store);
                    string temp = StoreFilePath + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    if (File.Exists(StoreFilePath))
                        File.Delete(StoreFilePath);
                    File.Move(temp, StoreFilePath);
                    _cached = store;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public bool MergeFromRemote(AbroAiStoreModel remote)
        {
            if (remote == null)
                return false;

            lock (_lock)
            {
                AbroAiStoreModel local = LoadOrDefault();
                bool changed = false;

                foreach (AbroAiProviderModel remoteProvider in remote.Providers ?? new System.Collections.Generic.List<AbroAiProviderModel>())
                {
                    AbroAiProviderModel localProvider = local.Providers.Find(p => p.Id == remoteProvider.Id);
                    if (localProvider == null)
                    {
                        local.Providers.Add(remoteProvider);
                        changed = true;
                    }
                    else if (remoteProvider.ModifiedAt > localProvider.ModifiedAt)
                    {
                        remoteProvider.CloneTo(localProvider);
                        changed = true;
                    }
                }

                foreach (AbroAiConversationModel remoteConversation in remote.Conversations ?? new System.Collections.Generic.List<AbroAiConversationModel>())
                {
                    AbroAiConversationModel localConversation = local.Conversations.Find(c => c.Id == remoteConversation.Id);
                    if (localConversation == null)
                    {
                        local.Conversations.Add(remoteConversation);
                        changed = true;
                    }
                    else if (remoteConversation.ModifiedAt > localConversation.ModifiedAt)
                    {
                        remoteConversation.CloneTo(localConversation);
                        changed = true;
                    }
                }

                // Prefer the remote default when the local one is missing or remote store is newer.
                if (remote.DefaultProviderId.HasValue
                    && (!local.DefaultProviderId.HasValue || remote.UpdatedAt > local.UpdatedAt))
                {
                    if (local.DefaultProviderId != remote.DefaultProviderId)
                    {
                        local.DefaultProviderId = remote.DefaultProviderId;
                        changed = true;
                    }
                }

                if (!changed)
                    return false;

                return TrySave(local);
            }
        }

        /// <summary>
        /// Resolves the Abro root directory the same way as the Android browser page.
        /// </summary>
        internal static string ResolveRootDirectory()
        {
            string preferred = Path.Combine("/sdcard", "abro");
            if (IsWritableDirectory(preferred))
                return preferred;

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                local = Path.GetTempPath();
            string fallback = Path.Combine(local, "abro");
            try
            {
                Directory.CreateDirectory(fallback);
            }
            catch (Exception)
            {
            }
            return fallback;
        }

        private static bool IsWritableDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
