// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Logs completed Pomodoro focus sessions into a linked note or a dedicated focus log note.
    /// </summary>
    public static class FocusSessionHelper
    {
        /// <summary>Timer mode value stored in <see cref="ClockDataModel.TimerMode"/> for focus.</summary>
        public const string ModeFocus = "focus";

        /// <summary>Timer mode value for a short break.</summary>
        public const string ModeBreak = "break";

        /// <summary>Timer mode value for a long break.</summary>
        public const string ModeLongBreak = "longbreak";

        /// <summary>Stable tag used to find the focus log note across devices.</summary>
        public const string FocusLogTag = "__anote_focus__";

        /// <summary>Stable id used when creating the focus log note for the first time.</summary>
        public static readonly Guid FocusLogNoteId = new Guid("a401e001-001c-4000-8000-000000000003");

        private const string HeaderHtml = "<h1>Focus log</h1>";

        /// <summary>
        /// Appends a focus completion entry to the linked note, or to the focus log note when none is linked.
        /// </summary>
        public static void RecordCompletion(
            NoteRepositoryModel repository,
            IRepositoryStorageService storageService,
            Guid? linkedNoteId,
            int durationSeconds)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            NoteModel target = ResolveTargetNote(repository, linkedNoteId);
            if (target == null)
                return;

            AppendFocusEntry(target, durationSeconds);
            if (target.InRecyclingBin)
                target.InRecyclingBin = false;

            target.RefreshModifiedAt();
            storageService?.TrySaveRepository(repository);
        }

        private static NoteModel ResolveTargetNote(NoteRepositoryModel repository, Guid? linkedNoteId)
        {
            if (linkedNoteId.HasValue && linkedNoteId.Value != Guid.Empty)
            {
                NoteModel linked = repository.Notes.FirstOrDefault(n => n.Id == linkedNoteId.Value && !n.InRecyclingBin);
                if (linked != null)
                    return linked;
            }

            return EnsureFocusLogNote(repository);
        }

        private static NoteModel EnsureFocusLogNote(NoteRepositoryModel repository)
        {
            NoteModel note = repository.Notes.FirstOrDefault(n => n.Id == FocusLogNoteId)
                ?? repository.Notes.FirstOrDefault(n =>
                    n.Tags != null && n.Tags.Any(tag =>
                        string.Equals(tag, FocusLogTag, StringComparison.OrdinalIgnoreCase)));

            if (note == null)
            {
                note = new NoteModel
                {
                    Id = FocusLogNoteId,
                    NoteType = NoteType.Text,
                    HtmlContent = HeaderHtml + "<p></p>",
                    BackgroundColorHex = "#e8f5e9",
                    IsPinned = true,
                };
                note.Tags.Add(FocusLogTag);
                repository.Notes.Insert(0, note);
                repository.RefreshOrderModifiedAt();
                return note;
            }

            if (note.InRecyclingBin)
                note.InRecyclingBin = false;
            if (!note.IsPinned)
                note.IsPinned = true;
            if (!note.Tags.Any(tag => string.Equals(tag, FocusLogTag, StringComparison.OrdinalIgnoreCase)))
                note.Tags.Add(FocusLogTag);

            if (string.IsNullOrWhiteSpace(note.HtmlContent)
                || !note.HtmlContent.Contains("<h1", StringComparison.OrdinalIgnoreCase))
                note.HtmlContent = HeaderHtml + (note.HtmlContent ?? string.Empty);

            return note;
        }

        private static void AppendFocusEntry(NoteModel note, int durationSeconds)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string durationText = FormatDuration(durationSeconds);
            string block = "<p><strong>Focus</strong> "
                + WebUtility.HtmlEncode(durationText) + " · "
                + WebUtility.HtmlEncode(stamp) + "</p>";

            note.HtmlContent = (note.HtmlContent ?? string.Empty) + block;
        }

        private static string FormatDuration(int durationSeconds)
        {
            durationSeconds = Math.Max(0, durationSeconds);
            if (durationSeconds < 60)
                return durationSeconds.ToString(CultureInfo.InvariantCulture) + "s";

            int minutes = durationSeconds / 60;
            int seconds = durationSeconds % 60;
            if (seconds == 0)
                return minutes.ToString(CultureInfo.InvariantCulture) + "m";

            return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", minutes, seconds);
        }
    }
}