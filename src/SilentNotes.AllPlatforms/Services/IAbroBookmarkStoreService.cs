// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Loads and saves the Abro bookmark store under the Abro root directory.
    /// </summary>
    public interface IAbroBookmarkStoreService
    {
        /// <summary>Gets the absolute path of the local store file.</summary>
        string StoreFilePath { get; }

        /// <summary>Loads the store, migrating legacy TSV if needed, or returns an empty store.</summary>
        AbroBookmarkStoreModel LoadOrDefault();

        /// <summary>Persists the store. Returns false on I/O failure.</summary>
        bool TrySave(AbroBookmarkStoreModel store);

        /// <summary>
        /// Merges remote bookmarks into the local store without duplicating URLs.
        /// Existing links are skipped (metadata updated only when remote is newer).
        /// </summary>
        bool MergeFromRemote(AbroBookmarkStoreModel remote);

        /// <summary>Adds or updates a bookmark for the given URL.</summary>
        bool AddOrUpdate(string url, string title);

        /// <summary>
        /// Replaces local bookmarks from a linqs-style portable JSON payload
        /// (<c>groups</c> / <c>bookmarks</c> / <c>isMoveToTopEnabled</c>).
        /// </summary>
        bool ReplaceFromPortableJson(string portableJson);

        /// <summary>Exports the current store as linqs-style JSON to the portable Abro bookmarks file.</summary>
        bool TryExportPortableFile();

        /// <summary>Imports linqs-style JSON from the portable Abro bookmarks file.</summary>
        bool TryImportPortableFile();

        /// <summary>Gets the absolute path of the portable export file.</summary>
        string PortableFilePath { get; }
    }
}
