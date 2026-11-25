// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Appends offline voice memo captures (transcript + optional recording reference) to the Voice note.
    /// </summary>
    public static class NoteVoiceMemoHelper
    {
        /// <summary>
        /// Prepends a voice memo block under the Voice note header.
        /// </summary>
        /// <param name="repository">The note repository.</param>
        /// <param name="transcript">Recognized dictation text (may be empty).</param>
        /// <param name="audioFileName">Optional recording file name for reference.</param>
        /// <returns>The updated Voice note.</returns>
        public static NoteModel AppendMemo(NoteRepositoryModel repository, string transcript, string audioFileName = null)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            NoteModel voiceNote = VoiceNoteHelper.EnsureVoiceNote(repository);
            var body = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(audioFileName))
            {
                body.Append("Recording: ");
                body.Append(audioFileName.Trim());
                body.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(transcript))
                body.Append(transcript.Trim());

            string combined = body.Length == 0 ? null : body.ToString();
            VoiceNoteHelper.PrependCapture(voiceNote, combined, null, DateTime.UtcNow);
            return voiceNote;
        }
    }
}