// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Resolves the inline link syntax of the note editor into ordinary html anchors:
    /// <list type="bullet">
    /// <item><c>[[Note title]]</c> links to a note with this title.</item>
    /// <item><c>@Contact</c> links to a contact (its first phone number).</item>
    /// <item><c>[[bookmark:Title]]</c> links to a bookmark.</item>
    /// </list>
    /// The generated anchors use the <c>anote://</c> scheme, so they stay stable when notes are
    /// renamed, and can be resolved again by the editor.
    /// </summary>
    public static class NoteLinkExpander
    {
        /// <summary>Prefix of a link to another note.</summary>
        public const string NoteLinkPrefix = "anote://note/";

        /// <summary>Prefix of a link to a contact.</summary>
        public const string ContactLinkPrefix = "anote://contact/";

        /// <summary>Prefix of a link to a bookmark.</summary>
        public const string BookmarkLinkPrefix = "anote://bookmark/";

        private const string TagPattern = "<[^>]*>";

        /// <summary>
        /// Expands all inline link shorthands in the html content of a note. Text inside existing
        /// links, code blocks and attributes is left untouched, so the function is idempotent.
        /// </summary>
        /// <param name="html">The html content of the note.</param>
        /// <param name="repository">The repository used to resolve note titles, contacts and bookmarks.</param>
        /// <returns>The html content with expanded links.</returns>
        public static string Expand(string html, NoteRepositoryModel repository)
        {
            if (string.IsNullOrEmpty(html) || repository == null)
                return html;

            var noteTitles = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (NoteModel note in repository.Notes)
            {
                if (note.InRecyclingBin)
                    continue;
                string title = NoteHtmlUtils.GetTitle(note.HtmlContent);
                if (!string.IsNullOrWhiteSpace(title) && !noteTitles.ContainsKey(title))
                    noteTitles[title] = note.Id;
            }

            var contacts = new Dictionary<string, ContactModel>(StringComparer.OrdinalIgnoreCase);
            foreach (ContactModel contact in repository.Contacts)
            {
                if (!string.IsNullOrWhiteSpace(contact.Name) && !contacts.ContainsKey(contact.Name))
                    contacts[contact.Name] = contact;
            }

            var bookmarks = new Dictionary<string, BookmarkModel>(StringComparer.OrdinalIgnoreCase);
            foreach (BookmarkModel bookmark in repository.Bookmarks)
            {
                if (!string.IsNullOrWhiteSpace(bookmark.Title) && !bookmarks.ContainsKey(bookmark.Title))
                    bookmarks[bookmark.Title] = bookmark;
            }

            var result = new StringBuilder(html.Length + 64);
            int index = 0;
            int anchorDepth = 0;
            int codeDepth = 0;
            var textBuilder = new StringBuilder();

            while (index < html.Length)
            {
                int tagStart = html.IndexOf('<', index);
                if (tagStart < 0)
                {
                    textBuilder.Append(html, index, html.Length - index);
                    break;
                }

                if (tagStart > index)
                    textBuilder.Append(html, index, tagStart - index);

                int tagEnd = html.IndexOf('>', tagStart);
                if (tagEnd < 0)
                {
                    textBuilder.Append(html, tagStart, html.Length - tagStart);
                    break;
                }

                // Flush the text which was collected before this tag, it can contain shorthands.
                if (textBuilder.Length > 0)
                {
                    string text = textBuilder.ToString();
                    textBuilder.Clear();
                    if (anchorDepth == 0 && codeDepth == 0)
                        text = ExpandText(text, noteTitles, contacts, bookmarks);
                    result.Append(text);
                }

                string tag = html.Substring(tagStart, tagEnd - tagStart + 1);
                result.Append(tag);
                UpdateDepth(tag, ref anchorDepth, ref codeDepth);
                index = tagEnd + 1;
            }

            if (textBuilder.Length > 0)
            {
                string text = textBuilder.ToString();
                if (anchorDepth == 0 && codeDepth == 0)
                    text = ExpandText(text, noteTitles, contacts, bookmarks);
                result.Append(text);
            }

            return result.ToString();
        }

        private static void UpdateDepth(string tag, ref int anchorDepth, ref int codeDepth)
        {
            string lower = tag.ToLowerInvariant();
            if (lower.StartsWith("<a ") || lower.StartsWith("<a>"))
                anchorDepth++;
            else if (lower.StartsWith("</a"))
                anchorDepth = Math.Max(0, anchorDepth - 1);
            else if (lower.StartsWith("<code") || lower.StartsWith("<pre"))
                codeDepth++;
            else if (lower.StartsWith("</code") || lower.StartsWith("</pre"))
                codeDepth = Math.Max(0, codeDepth - 1);
        }

        private static string ExpandText(
            string text,
            Dictionary<string, Guid> noteTitles,
            Dictionary<string, ContactModel> contacts,
            Dictionary<string, BookmarkModel> bookmarks)
        {
            // Bookmark links use an explicit prefix: [[bookmark:Title]] or [[b:Title]]
            text = Regex.Replace(text, @"\[\[(?:bookmark|b):([^\[\]\r\n]{1,200})\]\]", match =>
            {
                string title = match.Groups[1].Value.Trim();
                if (!bookmarks.TryGetValue(title, out BookmarkModel bookmark))
                    return match.Value;
                return BuildAnchor(BookmarkLinkPrefix + bookmark.Id.ToString("N"), title);
            }, RegexOptions.IgnoreCase);

            // Note links: [[Title]]
            text = Regex.Replace(text, @"\[\[([^\[\]\r\n]{1,200})\]\]", match =>
            {
                string title = match.Groups[1].Value.Trim();
                if (!noteTitles.TryGetValue(title, out Guid noteId))
                    return match.Value;
                return BuildAnchor(NoteLinkPrefix + noteId.ToString("N"), title);
            });

            // Contacts: @Name or @[Full name]
            text = Regex.Replace(text, @"(?<![\w])@(?:\[([^\[\]\r\n]{1,80})\]|([\p{L}\p{N}_\-\.]{1,60}))", match =>
            {
                string name = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
                if (!contacts.TryGetValue(name, out ContactModel contact))
                    return match.Value;
                return BuildAnchor(BuildContactLink(contact), name);
            });

            return text;
        }

        private static string BuildAnchor(string href, string title)
        {
            return string.Format("<a href=\"{0}\">{1}</a>", WebUtility.HtmlEncode(href), WebUtility.HtmlEncode(title));
        }

        /// <summary>
        /// Tries to parse an <c>anote://</c> link.
        /// </summary>
        /// <param name="uri">The link to parse.</param>
        /// <param name="target">Receives the target kind: "note", "contact" or "bookmark".</param>
        /// <param name="id">Receives the id of the target.</param>
        /// <returns>True if the link is a known aNote link, otherwise false.</returns>
        public static bool TryParseCustomUri(string uri, out string target, out Guid id)
        {
            target = null;
            id = Guid.Empty;
            if (string.IsNullOrWhiteSpace(uri))
                return false;

            string value = uri.Trim();
            foreach (var candidate in new[]
                     {
                         (Kind: "note", Prefix: NoteLinkPrefix),
                         (Kind: "contact", Prefix: ContactLinkPrefix),
                         (Kind: "bookmark", Prefix: BookmarkLinkPrefix),
                     })
            {
                if (!value.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string idText = value.Substring(candidate.Prefix.Length).Trim().TrimEnd('/');
                if (Guid.TryParseExact(idText, "N", out Guid parsed) || Guid.TryParse(idText, out parsed))
                {
                    target = candidate.Kind;
                    id = parsed;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Builds a link which points to another note.
        /// </summary>
        /// <param name="noteId">The id of the target note.</param>
        /// <returns>The href of the link.</returns>
        public static string BuildNoteLink(Guid noteId)
        {
            return NoteLinkPrefix + noteId.ToString("N");
        }

        /// <summary>
        /// Builds a link which points to a contact. It uses a <c>tel:</c> link, so it works in
        /// other viewers too, and falls back to the contact dialog when the number is missing.
        /// </summary>
        /// <param name="contact">The contact to link.</param>
        /// <returns>The href of the link.</returns>
        public static string BuildContactLink(ContactModel contact)
        {
            string phone = contact?.Phones?.FirstOrDefault(number => !string.IsNullOrWhiteSpace(number));
            if (!string.IsNullOrWhiteSpace(phone))
                return "tel:" + phone.Trim().Replace(" ", string.Empty);
            return ContactLinkPrefix + contact.Id.ToString("N");
        }

        /// <summary>
        /// Builds a link which points to a bookmark.
        /// </summary>
        /// <param name="bookmarkId">The id of the bookmark.</param>
        /// <returns>The href of the link.</returns>
        public static string BuildBookmarkLink(Guid bookmarkId)
        {
            return BookmarkLinkPrefix + bookmarkId.ToString("N");
        }

        /// <summary>
        /// Builds a link for an aNote target, used when the contact has no phone number.
        /// </summary>
        /// <param name="contactId">The id of the contact.</param>
        /// <returns>The href of the link.</returns>
        public static string BuildContactFallbackLink(Guid contactId)
        {
            return ContactLinkPrefix + contactId.ToString("N");
        }
    }
}
