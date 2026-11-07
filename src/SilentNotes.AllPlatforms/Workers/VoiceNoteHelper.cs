// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Ensures a pinned "Voice" capture note exists and prepends assistant captures to it.
    /// </summary>
    public static class VoiceNoteHelper
    {
        /// <summary>Stable tag used to find the Voice note across devices.</summary>
        public const string VoiceTag = "__anote_voice__";

        /// <summary>Stable id used when creating the Voice note for the first time.</summary>
        public static readonly Guid VoiceNoteId = new Guid("a401e001-001c-4000-8000-000000000001");

        private const string HeaderHtml = "<h1>Voice</h1>";
        private const string EmptyBodyHtml = "<p></p>";

        /// <summary>
        /// Ensures a pinned Voice note exists at the top of the repository. Returns the note.
        /// </summary>
        public static NoteModel EnsureVoiceNote(NoteRepositoryModel repository)
        {
            return EnsureVoiceNote(repository, out _);
        }

        /// <summary>
        /// Ensures a pinned Voice note exists at the top of the repository.
        /// </summary>
        /// <param name="changed">True when the repository was modified.</param>
        public static NoteModel EnsureVoiceNote(NoteRepositoryModel repository, out bool changed)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            changed = false;
            NoteModel note = FindVoiceNote(repository);
            if (note == null)
            {
                note = new NoteModel
                {
                    Id = VoiceNoteId,
                    NoteType = NoteType.Text,
                    HtmlContent = HeaderHtml + EmptyBodyHtml,
                    BackgroundColorHex = "#e8f0fe",
                    IsPinned = true,
                };
                note.Tags.Add(VoiceTag);
                repository.Notes.Insert(0, note);
                repository.RefreshOrderModifiedAt();
                changed = true;
                return note;
            }

            if (note.InRecyclingBin)
            {
                note.InRecyclingBin = false;
                changed = true;
            }

            if (!note.IsPinned)
            {
                note.IsPinned = true;
                changed = true;
            }

            if (!note.Tags.Any(t => string.Equals(t, VoiceTag, StringComparison.OrdinalIgnoreCase)))
            {
                note.Tags.Add(VoiceTag);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(note.HtmlContent)
                || !note.HtmlContent.Contains("<h1>", StringComparison.OrdinalIgnoreCase))
            {
                note.HtmlContent = HeaderHtml + (note.HtmlContent ?? EmptyBodyHtml);
                changed = true;
            }

            int index = repository.Notes.IndexOf(note);
            if (index > 0)
            {
                repository.Notes.RemoveAt(index);
                repository.Notes.Insert(0, note);
                repository.RefreshOrderModifiedAt();
                changed = true;
            }

            return note;
        }

        /// <summary>Finds the Voice note if present.</summary>
        public static NoteModel FindVoiceNote(NoteRepositoryModel repository)
        {
            if (repository?.Notes == null)
                return null;

            NoteModel byId = repository.Notes.FirstOrDefault(n => n.Id == VoiceNoteId);
            if (byId != null)
                return byId;

            return repository.Notes.FirstOrDefault(n =>
                n.Tags != null
                && n.Tags.Any(t => string.Equals(t, VoiceTag, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Prepends a capture block (timestamp, optional transcript, optional screenshot) under the
        /// Voice header.
        /// </summary>
        public static void PrependCapture(
            NoteModel voiceNote,
            string recognizedText,
            string screenshotDataUrl,
            DateTime utcTimestamp)
        {
            if (voiceNote == null)
                throw new ArgumentNullException(nameof(voiceNote));

            string stamp = utcTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            block.Append("<hr/><p><strong>");
            block.Append(WebUtility.HtmlEncode(stamp));
            block.Append("</strong></p>");

            if (!string.IsNullOrWhiteSpace(recognizedText))
            {
                block.Append("<p>");
                block.Append(WebUtility.HtmlEncode(recognizedText.Trim()).Replace("\n", "<br/>"));
                block.Append("</p>");
            }

            if (!string.IsNullOrWhiteSpace(screenshotDataUrl)
                && screenshotDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                block.Append("<p><img src=\"");
                block.Append(screenshotDataUrl);
                block.Append("\"/></p>");
            }

            string html = voiceNote.HtmlContent ?? string.Empty;
            int insertAt = FindInsertPositionAfterHeader(html);
            if (insertAt < 0)
            {
                voiceNote.HtmlContent = HeaderHtml + block + html;
            }
            else
            {
                voiceNote.HtmlContent = html.Insert(insertAt, block.ToString());
            }

            voiceNote.RefreshModifiedAt();
        }

        private static int FindInsertPositionAfterHeader(string html)
        {
            if (string.IsNullOrEmpty(html))
                return -1;

            int h1Open = html.IndexOf("<h1", StringComparison.OrdinalIgnoreCase);
            if (h1Open < 0)
                return -1;

            int h1Close = html.IndexOf("</h1>", h1Open, StringComparison.OrdinalIgnoreCase);
            if (h1Close < 0)
                return -1;

            return h1Close + "</h1>".Length;
        }
    }
}
