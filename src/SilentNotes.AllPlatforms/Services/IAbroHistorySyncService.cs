// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Synchronizes Abro browsing history with a WebDAV JSON file, reusing Booky sync credentials.
    /// </summary>
    public interface IAbroHistorySyncService
    {
        /// <summary>Gets a value indicating whether history sync is enabled and Booky WebDAV is configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Gets the message of the last error, empty after success.</summary>
        string LastError { get; }

        /// <summary>Downloads the remote store, merges into local, and uploads the result.</summary>
        Task<AbroBrowserSyncResult> SyncAsync();

        /// <summary>Uploads the local store without merging remote first.</summary>
        Task<AbroBrowserSyncResult> OverwriteRemoteAsync();
    }

    /// <summary>
    /// Result of an Abro bookmarks / history synchronization.
    /// </summary>
    public enum AbroBrowserSyncResult
    {
        /// <summary>Sync is disabled in settings.</summary>
        Disabled,

        /// <summary>Sync is enabled but Booky WebDAV url is missing.</summary>
        NotConfigured,

        /// <summary>Sync succeeded.</summary>
        Success,

        /// <summary>Sync failed.</summary>
        Failed,
    }
}
