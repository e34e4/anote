// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Metadata of a file attachment which belongs to a note. The content itself is stored
    /// encrypted in a separate file next to the repository.
    /// </summary>
    public class NoteAttachmentModel
    {
        /// <summary>Gets or sets the id of the attachment.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the original file name shown to the user.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets the mime type of the file.</summary>
        public string MimeType { get; set; }

        /// <summary>Gets or sets the size of the unencrypted file in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the UTC time when the attachment was added.</summary>
        public DateTime CreatedAt { get; set; }
    }
}
