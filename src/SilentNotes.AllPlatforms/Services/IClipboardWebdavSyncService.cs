// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Uploads a plain-text clipboard history snapshot to WebDAV for read-only consumers such as
    /// the desktop clipmon service. Never downloads or merges remote content.
    /// </summary>
    public interface IClipboardWebdavSyncService
    {
        /// <summary>Remote folder under the WebDAV root that holds the sync file.</summary>
        public const string RemoteFolderName = "anote";

        /// <summary>File name of the clipboard sync snapshot.</summary>
        public const string RemoteFileName = "clipboard_sync.json";

        /// <summary>Gets the last upload error message, or null when the last attempt succeeded.</summary>
        string LastError { get; }

        /// <summary>
        /// Gets a value indicating whether the sync toggle is on and WebDAV credentials resolve.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Schedules a debounced overwrite of the remote sync file from the local clipboard history.
        /// Safe to call from background threads; failures are swallowed into <see cref="LastError"/>.
        /// </summary>
        void ScheduleUpload();
    }
}
