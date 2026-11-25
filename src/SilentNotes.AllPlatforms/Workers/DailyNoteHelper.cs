// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Ensures one journal note per calendar day (title yyyy-MM-dd, tag <see cref="DailyTag"/>).
    /// </summary>
    public static class DailyNoteHelper
    {
        /// <summary>Tag identifying daily journal notes.</summary>
        public const string DailyTag = "__anote_daily__";

        /// <summary>Html comment marker before an injected event list.</summary>
        public const string CalendarMarker = "<!-- anote-daily-calendar -->";

        private static readonly Regex ParagraphBody = new Regex(
            @"<p(?:\s+class\s*=\s*""(?<class>[^""]*)"")?[^>]*>(?<body>.*?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex MarkdownUnchecked = new Regex(
            @"^-\s*\[\s*\]\s*(.+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Ensures today's daily note exists, optionally injecting calendar lines and carrying forward todos.
        /// </summary>
        public static NoteModel EnsureToday(NoteRepositoryModel repository, IEnumerable<string> calendarLines = null)
        {
            return EnsureToday(repository, calendarLines, out _);
        }

        /// <summary>
        /// Ensures today's daily note exists (no calendar injection). Suitable on repository load.
        /// </summary>
        public static NoteModel EnsureTodayWithoutCalendar(NoteRepositoryModel repository, out bool changed)
        {
            return EnsureToday(repository, null, out changed);
        }

        /// <summary>
        /// Ensures today's daily note exists, optionally injecting calendar lines and carrying forward todos.
        /// </summary>
        public static NoteModel EnsureToday(
            NoteRepositoryModel repository,
            IEnumerable<string> calendarLines,
            out bool changed)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            changed = false;
            string todayTitle = GetTodayTitle();
            NoteModel today = FindTodayNote(repository, todayTitle);
            if (today != null)
            {
                changed |= RestoreDailyNote(today, todayTitle);
                changed |= InjectCalendarIfNeeded(today, calendarLines);
                return today;
            }

            List<string> carried = ExtractUncheckedItems(FindYesterdayNote(repository));
            today = CreateTodayNote(todayTitle, carried);
            repository.Notes.Insert(0, today);
            repository.RefreshOrderModifiedAt();
            changed = true;
            changed |= InjectCalendarIfNeeded(today, calendarLines);
            return today;
        }

        /// <summary>Finds today's note id, or null when missing.</summary>
        public static Guid? FindTodayId(NoteRepositoryModel repository)
        {
            NoteModel note = FindTodayNote(repository, GetTodayTitle());
            if (note == null || note.Id == Guid.Empty)
                return null;
            return note.Id;
        }

        /// <summary>Returns the local calendar title for today.</summary>
        public static string GetTodayTitle()
        {
            return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static NoteModel FindTodayNote(NoteRepositoryModel repository, string todayTitle)
        {
            if (repository?.Notes == null)
                return null;

            NoteModel byTagAndTitle = repository.Notes.FirstOrDefault(n =>
                n != null
                && !n.InRecyclingBin
                && n.Tags != null
                && n.Tags.Any(t => string.Equals(t, DailyTag, StringComparison.OrdinalIgnoreCase))
                && string.Equals(NoteHtmlUtils.GetTitle(n.HtmlContent), todayTitle, StringComparison.Ordinal));
            if (byTagAndTitle != null)
                return byTagAndTitle;

            return repository.Notes.FirstOrDefault(n =>
                n != null
                && !n.InRecyclingBin
                && string.Equals(NoteHtmlUtils.GetTitle(n.HtmlContent), todayTitle, StringComparison.Ordinal));
        }

        private static NoteModel FindYesterdayNote(NoteRepositoryModel repository)
        {
            if (repository?.Notes == null)
                return null;

            string yesterdayTitle = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return repository.Notes.FirstOrDefault(n =>
                n != null
                && !n.InRecyclingBin
                && (string.Equals(NoteHtmlUtils.GetTitle(n.HtmlContent), yesterdayTitle, StringComparison.Ordinal)
                    || (n.Tags != null && n.Tags.Any(t => string.Equals(t, DailyTag, StringComparison.OrdinalIgnoreCase))
                        && string.Equals(NoteHtmlUtils.GetTitle(n.HtmlContent), yesterdayTitle, StringComparison.Ordinal))));
        }

        private static NoteModel CreateTodayNote(string todayTitle, IReadOnlyList<string> carriedItems)
        {
            var html = new StringBuilder();
            html.Append("<h1>");
            html.Append(WebUtility.HtmlEncode(todayTitle));
            html.Append("</h1>");

            bool useChecklist = carriedItems != null && carriedItems.Count > 0;
            if (useChecklist)
            {
                html.Append("<p><strong>Carried forward</strong></p>");
                foreach (string item in carriedItems)
                {
                    if (string.IsNullOrWhiteSpace(item))
                        continue;
                    html.Append("<p>");
                    html.Append(WebUtility.HtmlEncode(item.Trim()));
                    html.Append("</p>");
                }
            }
            else
            {
                html.Append("<p></p>");
            }

            var note = new NoteModel
            {
                NoteType = useChecklist ? NoteType.Checklist : NoteType.Text,
                HtmlContent = html.ToString(),
                BackgroundColorHex = "#f3e8ff",
            };
            note.Tags.Add(DailyTag);
            return note;
        }

        private static bool RestoreDailyNote(NoteModel note, string todayTitle)
        {
            bool changed = false;
            if (note.InRecyclingBin)
            {
                note.InRecyclingBin = false;
                changed = true;
            }

            if (note.Tags == null)
                note.Tags = new List<string>();

            if (!note.Tags.Any(t => string.Equals(t, DailyTag, StringComparison.OrdinalIgnoreCase)))
            {
                note.Tags.Add(DailyTag);
                changed = true;
            }

            string html = note.HtmlContent ?? string.Empty;
            if (!html.Contains("<h1", StringComparison.OrdinalIgnoreCase))
            {
                note.HtmlContent = "<h1>" + WebUtility.HtmlEncode(todayTitle) + "</h1>" + html;
                changed = true;
            }

            return changed;
        }

        private static bool InjectCalendarIfNeeded(NoteModel note, IEnumerable<string> calendarLines)
        {
            if (note == null || calendarLines == null)
                return false;

            List<string> lines = calendarLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();
            if (lines.Count == 0)
                return false;

            string html = note.HtmlContent ?? string.Empty;
            if (html.Contains(CalendarMarker, StringComparison.Ordinal))
                return false;

            var block = new StringBuilder();
            block.Append(CalendarMarker);
            block.Append("<p><strong>Calendar</strong></p><ul>");
            foreach (string line in lines)
            {
                block.Append("<li>");
                block.Append(WebUtility.HtmlEncode(line));
                block.Append("</li>");
            }
            block.Append("</ul>");

            int insertAt = FindInsertPositionAfterHeader(html);
            if (insertAt < 0)
            {
                note.HtmlContent = block + html;
            }
            else
            {
                note.HtmlContent = html.Insert(insertAt, block.ToString());
            }

            note.RefreshModifiedAt();
            return true;
        }

        internal static List<string> ExtractUncheckedItems(NoteModel yesterdayNote)
        {
            var result = new List<string>();
            if (yesterdayNote == null || string.IsNullOrWhiteSpace(yesterdayNote.HtmlContent))
                return result;

            string html = yesterdayNote.HtmlContent;
            foreach (Match match in ParagraphBody.Matches(html))
            {
                string className = match.Groups["class"].Value.Trim();
                if (string.Equals(className, "done", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(className, "disabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = NoteHtmlUtils.StripTags(match.Groups["body"].Value).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (!result.Any(existing => string.Equals(existing, text, StringComparison.Ordinal)))
                    result.Add(text);
            }

            string plain = NoteHtmlUtils.GetPlainText(html);
            foreach (Match match in MarkdownUnchecked.Matches(plain))
            {
                string text = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (!result.Any(existing => string.Equals(existing, text, StringComparison.Ordinal)))
                    result.Add(text);
            }

            return result;
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