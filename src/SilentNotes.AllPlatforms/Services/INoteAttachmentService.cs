// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Stores arbitrary encrypted files (PDF, audio, archives, …) which belong to a note.
    /// </summary>
    public interface INoteAttachmentService
    {
        /// <summary>Gets the attachments of a note, newest last.</summary>
        IReadOnlyList<NoteAttachmentModel> ListAttachments(Guid noteId);

        /// <summary>
        /// Encrypts and stores a file for the note. When the note is not yet protected by a safe,
        /// a per-note attachment key is created and protected with the device keystore.
        /// </summary>
        /// <returns>True when the attachment was stored.</returns>
        bool TryAddAttachment(Guid noteId, byte[] content, string fileName, string mimeType, out NoteAttachmentModel attachment, out string error);

        /// <summary>Decrypts an attachment, or returns null when it cannot be read.</summary>
        Task<byte[]> ReadAttachmentAsync(Guid noteId, Guid attachmentId);

        /// <summary>Deletes a single attachment and its index entry.</summary>
        bool DeleteAttachment(Guid noteId, Guid attachmentId);

        /// <summary>Deletes all attachments of a note, e.g. when the note is removed for good.</summary>
        void DeleteAllAttachments(Guid noteId);

        /// <summary>Builds the href which is inserted into the note html.</summary>
        string BuildAttachmentUri(Guid noteId, Guid attachmentId);

        /// <summary>Guesses a mime type from the file extension.</summary>
        string GuessMimeType(string fileName);
    }
}
