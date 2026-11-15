// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Creates notes for text captured outside the note editor (OCR, share targets and the quick
    /// settings scratchpad).
    /// </summary>
    public static class CaptureNoteHelper
    {
        /// <summary>Stable tag used to find the Scratchpad note across devices.</summary>
        public const string ScratchpadTag = "__anote_scratchpad__";

        /// <summary>Stable id used when creating the Scratchpad note for the first time.</summary>
        public static readonly Guid ScratchpadNoteId = new Guid("a401e001-001c-4000-8000-000000000002");

        private const string ScratchpadHeader = "<h1>Scratchpad</h1>";

        /// <summary>
        /// Inserts a new text note at the top of the repository.
        /// </summary>
        public static NoteModel CreateTextNote(NoteRepositoryModel repository, string title, string text)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            string heading = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Captured" : title.Trim());
            string body = WebUtility.HtmlEncode((text ?? string.Empty).Trim()).Replace("\n", "<br/>");
            var note = new NoteModel
            {
                NoteType = NoteType.Text,
                HtmlContent = "<h1>" + heading + "</h1><p>" + body + "</p>",
            };

            repository.Notes.Insert(0, note);
            repository.RefreshOrderModifiedAt();
            return note;
        }

        /// <summary>
        /// Appends a scratchpad entry to the pinned Scratchpad note, creating the note if needed.
        /// </summary>
        public static NoteModel AppendScratchpad(NoteRepositoryModel repository, string text)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            NoteModel note = repository.Notes.FirstOrDefault(candidate => candidate.Id == ScratchpadNoteId)
                ?? repository.Notes.FirstOrDefault(candidate =>
                    candidate.Tags != null && candidate.Tags.Any(tag =>
                        string.Equals(tag, ScratchpadTag, StringComparison.OrdinalIgnoreCase)));

            if (note == null)
            {
                note = new NoteModel
                {
                    Id = ScratchpadNoteId,
                    NoteType = NoteType.Text,
                    HtmlContent = ScratchpadHeader + "<p></p>",
                    BackgroundColorHex = "#fff8e1",
                    IsPinned = true,
                };
                note.Tags.Add(ScratchpadTag);
                repository.Notes.Insert(0, note);
                repository.RefreshOrderModifiedAt();
            }

            if (note.InRecyclingBin)
                note.InRecyclingBin = false;
            if (!note.IsPinned)
                note.IsPinned = true;
            if (!note.Tags.Any(tag => string.Equals(tag, ScratchpadTag, StringComparison.OrdinalIgnoreCase)))
                note.Tags.Add(ScratchpadTag);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string block = "<hr/><p><strong>" + WebUtility.HtmlEncode(stamp) + "</strong></p><p>"
                + WebUtility.HtmlEncode((text ?? string.Empty).Trim()).Replace("\n", "<br/>") + "</p>";

            string html = note.HtmlContent ?? string.Empty;
            int headerEnd = html.IndexOf("</h1>", StringComparison.OrdinalIgnoreCase);
            if (headerEnd >= 0)
                note.HtmlContent = html.Insert(headerEnd + "</h1>".Length, block);
            else
                note.HtmlContent = ScratchpadHeader + block + html;

            note.RefreshModifiedAt();
            int index = repository.Notes.IndexOf(note);
            if (index > 0)
            {
                repository.Notes.RemoveAt(index);
                repository.Notes.Insert(0, note);
                repository.RefreshOrderModifiedAt();
            }
            return note;
        }
    }
}
