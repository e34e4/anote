// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Builds and parses the <c>anote://attachment/…</c> links which are embedded in a note when
    /// a file is attached.
    /// </summary>
    public static class NoteAttachmentLink
    {
        /// <summary>Prefix of a link to a note attachment.</summary>
        public const string AttachmentLinkPrefix = "anote://attachment/";

        /// <summary>Builds the href which points to a note attachment.</summary>
        public static string Build(Guid noteId, Guid attachmentId)
        {
            return AttachmentLinkPrefix + noteId.ToString("N") + "/" + attachmentId.ToString("N");
        }

        /// <summary>Tries to parse an attachment link.</summary>
        public static bool TryParse(string uri, out Guid noteId, out Guid attachmentId)
        {
            noteId = Guid.Empty;
            attachmentId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(uri) ||
                !uri.StartsWith(AttachmentLinkPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] parts = uri.Substring(AttachmentLinkPrefix.Length)
                .Trim()
                .TrimEnd('/')
                .Split('/');
            if (parts.Length != 2)
                return false;

            return Guid.TryParseExact(parts[0], "N", out noteId)
                && Guid.TryParseExact(parts[1], "N", out attachmentId);
        }
    }
}
