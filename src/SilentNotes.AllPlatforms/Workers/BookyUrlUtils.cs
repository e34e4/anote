// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Helper methods to detect urls in arbitrary text, ported from the Booky app.
    /// </summary>
    public static class BookyUrlUtils
    {
        private static readonly Regex UrlRegex = new Regex(
            @"https?://[^\s<>""')\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Extracts the first http(s) url from a text, or returns null if none is found.
        /// </summary>
        /// <param name="text">Text which may contain a url.</param>
        /// <returns>The found url, or null.</returns>
        public static string FirstUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string trimmed = text.Trim().TrimEnd('/', ' ');
            if ((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            {
                return trimmed.TrimEnd('.', ',', ';', ')', ']');
            }

            Match match = UrlRegex.Match(text);
            return match.Success ? match.Value.TrimEnd('.', ',', ';', ')', ']') : null;
        }

        /// <summary>
        /// Determines whether a text contains an http(s) url.
        /// </summary>
        /// <param name="text">Text to check.</param>
        /// <returns>True if a url was found, otherwise false.</returns>
        public static bool LooksLikeUrl(string text)
        {
            return FirstUrl(text) != null;
        }

        /// <summary>
        /// Gets the host of a url, or the url itself if the host cannot be determined.
        /// </summary>
        /// <param name="url">The url to inspect.</param>
        /// <returns>The lower case host name.</returns>
        public static string HostOf(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
                return uri.Host.ToLowerInvariant();
            return url;
        }
    }
}
