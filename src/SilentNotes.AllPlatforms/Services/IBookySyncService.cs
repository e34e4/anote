// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Synchronizes the bookmarks of the Booky tab with the shared bookmarks file, which is also
    /// used by the Booky browser extension and the Booky Android app. The file is a plain JSON
    /// document stored on a WebDAV server.
    /// </summary>
    public interface IBookySyncService
    {
        /// <summary>
        /// Gets a value indicating whether the Booky sync is enabled and configured.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Downloads the shared bookmarks file, merges it into the bookmark list of the repository
        /// and uploads the merged list back to the shared file.
        /// </summary>
        /// <returns>The result of the synchronization, so the caller can show feedback.</returns>
        Task<BookySyncResult> SyncAsync();

        /// <summary>
        /// Uploads the local bookmarks to the shared file, without merging the remote bookmarks
        /// into the local list first. Bookmarks which only exist on the server are removed. This
        /// is used to push a deliberate deletion to the server.
        /// </summary>
        /// <returns>The result of the synchronization, so the caller can show feedback.</returns>
        Task<BookySyncResult> OverwriteRemoteAsync();

        /// <summary>
        /// Synchronizes after a local bookmark was deleted. It still merges new bookmarks of the
        /// shared file into the local list, but keeps the deleted bookmark removed, so it does not
        /// reappear.
        /// </summary>
        /// <param name="deletedBookmarkId">The id of the bookmark which was deleted locally.</param>
        /// <returns>The result of the synchronization, so the caller can show feedback.</returns>
        Task<BookySyncResult> SyncAfterDeleteAsync(Guid deletedBookmarkId);
    }

    /// <summary>
    /// Result of a Booky bookmarks synchronization.
    /// </summary>
    public enum BookySyncResult
    {
        /// <summary>The sync is disabled in the settings, nothing was done.</summary>
        Disabled,

        /// <summary>The sync is enabled but the remote url is not configured.</summary>
        NotConfigured,

        /// <summary>The bookmarks were synchronized successfully.</summary>
        Success,

        /// <summary>The synchronization failed, e.g. the server was not reachable.</summary>
        Failed,
    }
}
