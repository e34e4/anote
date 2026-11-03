// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Loads and saves the Abro browsing-history store under the Abro root directory.
    /// </summary>
    public interface IAbroHistoryStoreService
    {
        /// <summary>Gets the absolute path of the local store file.</summary>
        string StoreFilePath { get; }

        /// <summary>Loads the store, migrating legacy TSV if needed, or returns an empty store.</summary>
        AbroHistoryStoreModel LoadOrDefault();

        /// <summary>Persists the store. Returns false on I/O failure.</summary>
        bool TrySave(AbroHistoryStoreModel store);

        /// <summary>
        /// Merges remote history into the local store without duplicating URLs.
        /// Existing links are skipped (visit time/title updated only when remote is newer).
        /// </summary>
        bool MergeFromRemote(AbroHistoryStoreModel remote);

        /// <summary>Appends a visit (or bumps VisitedAt when the same URL already exists).</summary>
        bool AppendVisit(string url, string title);

        /// <summary>Removes all visits matching the URL.</summary>
        bool RemoveByUrl(string url);

        /// <summary>Clears all history entries.</summary>
        bool ClearAll();
    }
}
