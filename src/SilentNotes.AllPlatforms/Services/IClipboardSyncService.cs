// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Bridges the local clipboard history (a separate file on the device) with the synchronized
    /// note repository, so clipboard items are uploaded and downloaded together with the notes.
    /// </summary>
    public interface IClipboardSyncService
    {
        /// <summary>
        /// Copies the local clipboard history into the transfer collection of the repository, so it
        /// can be encrypted and uploaded.
        /// </summary>
        /// <param name="repository">The repository to fill the transfer collection of.</param>
        void Export(NoteRepositoryModel repository);

        /// <summary>
        /// Merges the clipboard items of a downloaded repository with the local clipboard history,
        /// writes the merged result back to the local history and returns the merged items for
        /// uploading.
        /// </summary>
        /// <param name="cloudItems">The clipboard items of the downloaded repository.</param>
        /// <returns>The merged clipboard items, to be uploaded.</returns>
        IReadOnlyList<ClipboardTransferItemModel> MergeAndImport(IReadOnlyList<ClipboardTransferItemModel> cloudItems);

        /// <summary>
        /// Removes the transient transfer collection from a repository, so it is not persisted
        /// to the local device.
        /// </summary>
        /// <param name="repository">The repository to clean.</param>
        void ClearTransfer(NoteRepositoryModel repository);

        /// <summary>
        /// Gets a fingerprint of the local clipboard history, used to detect changes for
        /// synchronization.
        /// </summary>
        /// <returns>A fingerprint representing the modification state.</returns>
        long GetLocalFingerprint();
    }
}
