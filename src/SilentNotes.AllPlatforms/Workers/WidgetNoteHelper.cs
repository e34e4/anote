// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Ensures a pinned "widget" note exists and reads/writes its plain-text body for the
    /// Android home-screen widget.
    /// </summary>
    public static class WidgetNoteHelper
    {
        /// <summary>Stable tag used to find the widget note across devices.</summary>
        public const string WidgetTag = "__anote_widget__";

        /// <summary>Stable id used when creating the widget note for the first time.</summary>
        public static readonly Guid WidgetNoteId = new Guid("a401e001-001c-4000-8000-000000000003");

        private const string HeaderHtml = "<h1>widget</h1>";
        private const string EmptyBodyHtml = "<p></p>";

        /// <summary>
        /// Ensures a pinned widget note exists. Returns the note.
        /// </summary>
        public static NoteModel EnsureWidgetNote(NoteRepositoryModel repository)
        {
            return EnsureWidgetNote(repository, out _);
        }

        /// <summary>
        /// Ensures a pinned widget note exists.
        /// </summary>
        /// <param name="changed">True when the repository was modified.</param>
        public static NoteModel EnsureWidgetNote(NoteRepositoryModel repository, out bool changed)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            changed = false;
            NoteModel note = FindWidgetNote(repository);
            if (note == null)
            {
                note = new NoteModel
                {
                    Id = WidgetNoteId,
                    NoteType = NoteType.Text,
                    HtmlContent = HeaderHtml + EmptyBodyHtml,
                    BackgroundColorHex = "#000000",
                    IsPinned = true,
                };
                note.Tags.Add(WidgetTag);
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

            if (!note.Tags.Any(t => string.Equals(t, WidgetTag, StringComparison.OrdinalIgnoreCase)))
            {
                note.Tags.Add(WidgetTag);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(note.HtmlContent)
                || !note.HtmlContent.Contains("<h1>", StringComparison.OrdinalIgnoreCase))
            {
                note.HtmlContent = HeaderHtml + (note.HtmlContent ?? EmptyBodyHtml);
                changed = true;
            }

            return note;
        }

        /// <summary>Finds the widget note if present.</summary>
        public static NoteModel FindWidgetNote(NoteRepositoryModel repository)
        {
            if (repository?.Notes == null)
                return null;

            NoteModel byId = repository.Notes.FirstOrDefault(n => n.Id == WidgetNoteId);
            if (byId != null)
                return byId;

            // Match by tag only — never hijack an ordinary user note that happens to be titled "widget".
            return repository.Notes.FirstOrDefault(n =>
                n.Tags != null
                && n.Tags.Any(t => string.Equals(t, WidgetTag, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Gets the plain-text body of the widget note (without the title heading).</summary>
        public static string GetWidgetPlainText(NoteModel note)
        {
            string html = note?.HtmlContent ?? string.Empty;
            int h1Close = html.IndexOf("</h1>", StringComparison.OrdinalIgnoreCase);
            string bodyHtml = h1Close >= 0 ? html.Substring(h1Close + "</h1>".Length) : html;
            return NoteHtmlUtils.GetPlainText(bodyHtml);
        }

        /// <summary>Replaces the widget note body with plain text (keeps the widget heading).</summary>
        public static void SetWidgetBody(NoteModel note, string plainText)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            string encoded = WebUtility.HtmlEncode(plainText ?? string.Empty).Replace("\n", "<br/>");
            note.HtmlContent = HeaderHtml + "<p>" + encoded + "</p>";
            note.RefreshModifiedAt();
        }
    }
}
