// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Represents a remote file or directory entry on a WebDAV server.
    /// </summary>
    public class WebdavFileItemModel
    {
        /// <summary>Gets or sets the display name of the file or folder.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets a value indicating whether the entry is a directory (collection).</summary>
        public bool IsDirectory { get; set; }

        /// <summary>Gets or sets the size in bytes for files.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the last modified timestamp in UTC, if provided by the server.</summary>
        public DateTime? LastModifiedUtc { get; set; }
    }
}
