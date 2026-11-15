// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Synchronizes the open Abro tabs with a WebDAV JSON file, reusing the Booky sync
    /// credentials. This hands tabs over between devices.
    /// </summary>
    public interface IAbroTabsSyncService
    {
        /// <summary>Gets a value indicating whether tab sync is enabled and Booky WebDAV is configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Gets the message of the last error, empty after success.</summary>
        string LastError { get; }

        /// <summary>Merges the local and remote tab lists and uploads the result.</summary>
        Task<AbroBrowserSyncResult> SyncAsync();

        /// <summary>Uploads the local tab list without merging remote first.</summary>
        Task<AbroBrowserSyncResult> OverwriteRemoteAsync();
    }
}
