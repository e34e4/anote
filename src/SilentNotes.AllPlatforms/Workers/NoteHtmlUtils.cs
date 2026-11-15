// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Small helpers to read information out of the html content of a note.
    /// </summary>
    public static class NoteHtmlUtils
    {
        /// <summary>
        /// Gets the title of a note. aNote does not store a title, the first heading (or the first
        /// line of text when there is no heading) is used as title.
        /// </summary>
        /// <param name="html">The html content of the note.</param>
        /// <returns>The title, or an empty string when the note has no text.</returns>
        public static string GetTitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            Match heading = Regex.Match(html, @"<h[1-3][^>]*>(.*?)</h[1-3]>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string raw = heading.Success ? heading.Groups[1].Value : html;

            string text = StripTags(raw);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length > 100)
                text = text.Substring(0, 100).TrimEnd();
            return text;
        }

        /// <summary>
        /// Converts the html content of a note to plain text, preserving line breaks between
        /// paragraphs and list items.
        /// </summary>
        /// <param name="html">The html content of the note.</param>
        /// <returns>The plain text.</returns>
        public static string GetPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            string text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|blockquote|pre)>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<(li)[^>]*>", "- ", RegexOptions.IgnoreCase);
            text = StripTags(text);
            text = Regex.Replace(text, @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        /// <summary>
        /// Removes all html tags and decodes html entities.
        /// </summary>
        /// <param name="html">The html fragment.</param>
        /// <returns>The text without tags.</returns>
        public static string StripTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            string result = Regex.Replace(html, @"<script[^>]*>.*?</script>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            result = Regex.Replace(result, @"<style[^>]*>.*?</style>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            result = Regex.Replace(result, "<[^>]+>", string.Empty);
            return WebUtility.HtmlDecode(result);
        }

        /// <summary>
        /// Creates a file name friendly slug from a title.
        /// </summary>
        /// <param name="title">The title to convert.</param>
        /// <returns>A short slug, never empty.</returns>
        public static string Slugify(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "note";

            var builder = new StringBuilder(title.Length);
            foreach (char c in title.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(c);
                else if ((c == ' ') || (c == '-') || (c == '_'))
                    builder.Append('-');
            }

            string slug = Regex.Replace(builder.ToString(), "-{2,}", "-").Trim('-');
            if (slug.Length > 60)
                slug = slug.Substring(0, 60).Trim('-');
            return string.IsNullOrEmpty(slug) ? "note" : slug;
        }
    }
}
