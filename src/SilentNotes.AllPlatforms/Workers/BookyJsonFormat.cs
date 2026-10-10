// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Reads and writes the JSON file which is shared between the Booky browser extension, the
    /// Booky Android app and aNote. The file is a plain JSON document:
    /// <code>
    /// {
    ///   "version": 1,
    ///   "updatedAt": "2026-01-01T12:00:00.000Z",
    ///   "bookmarks": [ { "id": "...", "createdAt": "...", "updatedAt": "...", "title": "...",
    ///                    "url": "...", "favIconUrl": "...", "note": "...", "anchor": { ... } } ]
    /// }
    /// </code>
    /// </summary>
    public static class BookyJsonFormat
    {
        private const string IdField = "id";
        private const string CreatedAtField = "createdAt";
        private const string UpdatedAtField = "updatedAt";
        private const string TitleField = "title";
        private const string UrlField = "url";
        private const string FavIconUrlField = "favIconUrl";
        private const string NoteField = "note";
        private const string AnchorField = "anchor";
        private const string QuoteField = "quote";

        /// <summary>
        /// Parses the content of a shared Booky bookmarks file.
        /// </summary>
        /// <param name="json">The content of the JSON file. A bare array is accepted as well as
        /// the wrapped object.</param>
        /// <returns>The parsed bookmarks, an empty list for empty content, or null if the content
        /// could not be parsed.</returns>
        public static BookmarkListModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new BookmarkListModel();

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            JsonArray items = root as JsonArray;
            if ((items == null) && (root is JsonObject obj))
                items = obj["bookmarks"] as JsonArray;
            if (items == null)
                return null;

            var result = new BookmarkListModel();
            foreach (JsonNode node in items)
            {
                if (node is not JsonObject item)
                    continue;

                var bookmark = new BookmarkModel();
                if (TryGetGuid(item, IdField, out Guid id))
                    bookmark.Id = id;
                if (TryGetUtcDateTime(item, CreatedAtField, out DateTime createdAt))
                    bookmark.CreatedAt = createdAt;
                if (TryGetUtcDateTime(item, UpdatedAtField, out DateTime updatedAt))
                    bookmark.ModifiedAt = updatedAt;

                bookmark.Title = GetString(item, TitleField);
                bookmark.Url = GetString(item, UrlField);
                bookmark.Note = GetString(item, NoteField);
                bookmark.FavIconUrl = GetString(item, FavIconUrlField);

                if (item[AnchorField] is JsonObject anchor)
                {
                    bookmark.AnchorJson = anchor.ToJsonString();
                    bookmark.Quote = GetString(anchor, QuoteField);
                }

                result.Add(bookmark);
            }
            return result;
        }

        /// <summary>
        /// Serializes the given bookmarks into the shared Booky bookmarks file format.
        /// </summary>
        /// <param name="bookmarks">The bookmarks to store.</param>
        /// <returns>The JSON content of the file.</returns>
        public static string Serialize(IEnumerable<BookmarkModel> bookmarks)
        {
            var items = new JsonArray();
            foreach (BookmarkModel bookmark in bookmarks)
                items.Add(BuildItem(bookmark));

            var root = new JsonObject
            {
                ["version"] = 1,
                ["updatedAt"] = ToIso(DateTime.UtcNow),
                ["bookmarks"] = items,
            };
            return root.ToJsonString(CreateOptions());
        }

        private static JsonObject BuildItem(BookmarkModel bookmark)
        {
            return new JsonObject
            {
                [IdField] = bookmark.Id.ToString(),
                [CreatedAtField] = ToIso(bookmark.CreatedAt),
                [UpdatedAtField] = ToIso(bookmark.ModifiedAt),
                [TitleField] = bookmark.Title ?? string.Empty,
                [UrlField] = bookmark.Url ?? string.Empty,
                [FavIconUrlField] = bookmark.FavIconUrl ?? string.Empty,
                [NoteField] = bookmark.Note ?? string.Empty,
                [AnchorField] = BuildAnchor(bookmark),
            };
        }

        private static JsonNode BuildAnchor(BookmarkModel bookmark)
        {
            // Preserve the anchor details (prefix/suffix/scroll position) of items which were
            // imported from the shared file, but keep the quote of the local bookmark authoritative.
            if (!string.IsNullOrEmpty(bookmark.AnchorJson))
            {
                JsonNode parsed = null;
                try
                {
                    parsed = JsonNode.Parse(bookmark.AnchorJson);
                }
                catch (JsonException)
                {
                }

                if (parsed is JsonObject anchor)
                {
                    anchor[QuoteField] = bookmark.Quote ?? string.Empty;
                    return anchor;
                }
            }

            if (string.IsNullOrEmpty(bookmark.Quote))
                return null;

            return new JsonObject
            {
                [QuoteField] = bookmark.Quote,
                ["prefix"] = string.Empty,
                ["suffix"] = string.Empty,
                ["scrollY"] = 0,
                ["scrollRatio"] = 0,
            };
        }

        private static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
        }

        private static string ToIso(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static string GetString(JsonObject obj, string name)
        {
            if ((obj != null) && (obj[name] is JsonNode node) && (node.GetValueKind() == JsonValueKind.String))
                return node.GetValue<string>();
            return null;
        }

        private static bool TryGetGuid(JsonObject obj, string name, out Guid value)
        {
            value = Guid.Empty;
            string text = GetString(obj, name);
            return (text != null) && Guid.TryParse(text, out value);
        }

        private static bool TryGetUtcDateTime(JsonObject obj, string name, out DateTime value)
        {
            value = default;
            string text = GetString(obj, name);
            if (text == null)
                return false;
            if (DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                value = parsed.UtcDateTime;
                return true;
            }
            return false;
        }
    }
}
