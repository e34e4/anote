// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
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
                    // One provider per API endpoint+model: skip adding when it already exists.
                    AbroAiProviderModel localProvider = local.Providers.Find(p =>
                        p.Id == remoteProvider.Id
                        || SameProviderEndpoint(p, remoteProvider));
                    if (localProvider != null)
                    {
                        if (remoteProvider.ModifiedAt > localProvider.ModifiedAt)
                        {
                            Guid keepId = localProvider.Id;
                            remoteProvider.CloneTo(localProvider);
                            localProvider.Id = keepId;
                            changed = true;
                        }
                        continue;
                    }

                    local.Providers.Add(remoteProvider);
                    changed = true;
                }

                if (DeduplicateProviders(local))
                    changed = true;

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
        /// Resolves the Abro root directory (<see cref="AnotePaths.Abro"/>).
        /// </summary>
        internal static string ResolveRootDirectory() => AnotePaths.Abro;

        private static bool SameProviderEndpoint(AbroAiProviderModel a, AbroAiProviderModel b)
        {
            if (a == null || b == null)
                return false;
            return string.Equals(NormalizeEndpoint(a.Url), NormalizeEndpoint(b.Url), StringComparison.OrdinalIgnoreCase)
                && string.Equals((a.ModelId ?? string.Empty).Trim(), (b.ModelId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeEndpoint(string url)
        {
            return (url ?? string.Empty).Trim().TrimEnd('/');
        }

        private static bool DeduplicateProviders(AbroAiStoreModel store)
        {
            if (store?.Providers == null || store.Providers.Count < 2)
                return false;

            bool changed = false;
            var kept = new System.Collections.Generic.List<AbroAiProviderModel>();
            foreach (AbroAiProviderModel provider in store.Providers)
            {
                AbroAiProviderModel existing = kept.Find(p =>
                    p.Id == provider.Id || SameProviderEndpoint(p, provider));
                if (existing != null)
                {
                    changed = true;
                    if (provider.ModifiedAt > existing.ModifiedAt)
                    {
                        Guid keepId = existing.Id;
                        provider.CloneTo(existing);
                        existing.Id = keepId;
                    }
                    continue;
                }
                kept.Add(provider);
            }

            if (changed)
            {
                // Remap default id if the kept provider replaced a duplicate id.
                Guid? defaultId = store.DefaultProviderId;
                if (defaultId.HasValue && kept.Find(p => p.Id == defaultId.Value) == null)
                {
                    AbroAiProviderModel match = kept.Find(p =>
                        store.Providers.Exists(orig =>
                            orig.Id == defaultId.Value && SameProviderEndpoint(orig, p)));
                    store.DefaultProviderId = match?.Id ?? kept.FirstOrDefault()?.Id;
                }
                store.Providers = kept;
            }
            return changed;
        }

    }
}
