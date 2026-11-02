// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Loads and saves the Abro AI store (providers + conversations) on the local device.
    /// </summary>
    public interface IAbroAiStoreService
    {
        /// <summary>
        /// Gets the absolute path of the local store file.
        /// </summary>
        string StoreFilePath { get; }

        /// <summary>
        /// Loads the store from disk, or returns a new empty store when missing/invalid.
        /// </summary>
        AbroAiStoreModel LoadOrDefault();

        /// <summary>
        /// Saves the store to disk. Returns true on success.
        /// </summary>
        bool TrySave(AbroAiStoreModel store);

        /// <summary>
        /// Merges <paramref name="remote"/> into the local store by entity id and modifiedAt.
        /// Saves when local data changed. Returns true when the local store was modified.
        /// </summary>
        bool MergeFromRemote(AbroAiStoreModel remote);
    }
}
