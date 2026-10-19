// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Kind of media entry shown in the Player tab (audio/video now; images prepared for a
    /// future gallery viewer in the same tab).
    /// </summary>
    public enum MediaKind
    {
        Folder,
        Audio,
        Video,
        Image,
        Other,
    }

    /// <summary>
    /// A file or folder entry discovered while browsing device storage in the Player tab.
    /// </summary>
    public class MediaItemModel
    {
        /// <summary>Gets or sets the absolute filesystem path, or a content URI string.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the media kind.</summary>
        public MediaKind Kind { get; set; }

        /// <summary>Gets or sets the file size in bytes (0 for folders).</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the last modified time in local time.</summary>
        public DateTime ModifiedLocal { get; set; }

        /// <summary>Gets or sets the MIME type when known (e.g. from a VIEW intent).</summary>
        public string MimeType { get; set; }
    }
}
