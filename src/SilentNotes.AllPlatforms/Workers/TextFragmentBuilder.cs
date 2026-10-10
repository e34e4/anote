// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Text;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Builds Chromium text-fragment urls (<c>#:~:text=</c>), so a browser can scroll to and
    /// highlight a previously selected text. Ported from the Booky app.
    /// </summary>
    /// <remarks>
    /// Spec: https://wicg.github.io/scroll-to-text-fragment/
    /// The characters '-' and ',' are directive syntax and MUST be percent encoded inside the
    /// matched text.
    /// </remarks>
    public static class TextFragmentBuilder
    {
        /// <summary>
        /// Builds a url with a text-fragment directive pointing at <paramref name="quote"/>.
        /// </summary>
        /// <param name="url">The page url.</param>
        /// <param name="quote">The text to scroll to and highlight.</param>
        /// <returns>The url including the text fragment.</returns>
        public static string WithTextFragment(string url, string quote)
        {
            string clean = NormalizeQuote(quote);
            if (clean.Length == 0)
                return StripTextFragment(url);

            string stripped = StripTextFragment(url);
            string baseUrl = SubstringBefore(stripped, '#');
            string existingHash = SubstringAfter(stripped, '#', string.Empty);
            string directive = BuildDirective(clean);

            // Keep SPA hash routes (#/path); drop unrelated fragments that fight scroll-to-text.
            if (existingHash.StartsWith("/"))
                return baseUrl + "#" + existingHash + ":~:" + directive;
            return baseUrl + "#:~:" + directive;
        }

        /// <summary>
        /// Removes an existing <c>:~:text=</c> fragment from a url.
        /// </summary>
        /// <param name="url">The url to clean.</param>
        /// <returns>The url without a text fragment.</returns>
        public static string StripTextFragment(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            int markerIndex = url.IndexOf(":~:", StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                int hashIndex = url.IndexOf('#');
                if (hashIndex >= 0 && markerIndex > hashIndex)
                    return url.Substring(0, markerIndex).TrimEnd('#');
            }
            return url;
        }

        private static string NormalizeQuote(string quote)
        {
            if (string.IsNullOrEmpty(quote))
                return string.Empty;

            string result = quote.Replace('\u00A0', ' ');
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
            return result.Trim();
        }

        private static string BuildDirective(string quote)
        {
            string[] words = quote.Split(' ').Where(word => word.Length > 0).ToArray();

            // One contiguous phrase is more stable than start,end (avoids brief wrong-match jitter).
            string phrase = (words.Length <= 12)
                ? quote.Substring(0, Math.Min(180, quote.Length))
                : string.Join(" ", words.Take(10));

            return "text=" + EncodeTerm(phrase);
        }

        /// <summary>
        /// Percent encodes a text for use inside a text-fragment directive.
        /// </summary>
        /// <param name="text">The text to encode.</param>
        /// <returns>The encoded text.</returns>
        public static string EncodeTerm(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch == ' ')
                {
                    sb.Append("%20");
                }
                else if (IsAsciiUrlSafe(ch))
                {
                    sb.Append(ch);
                }
                else
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(ch.ToString());
                    foreach (byte b in bytes)
                        sb.Append('%').Append(b.ToString("X2"));
                }
            }
            return sb.ToString();
        }

        private static bool IsAsciiUrlSafe(char ch)
        {
            return (ch >= 'a' && ch <= 'z') ||
                   (ch >= 'A' && ch <= 'Z') ||
                   (ch >= '0' && ch <= '9') ||
                   ch == '_' || ch == '.' || ch == '~' ||
                   ch == '\'' || ch == '!' || ch == '*' ||
                   ch == '(' || ch == ')';
        }

        private static string SubstringBefore(string value, char delimiter)
        {
            int index = value.IndexOf(delimiter);
            return index >= 0 ? value.Substring(0, index) : value;
        }

        private static string SubstringAfter(string value, char delimiter, string missingDelimiterValue)
        {
            int index = value.IndexOf(delimiter);
            return index >= 0 ? value.Substring(index + 1) : missingDelimiterValue;
        }
    }
}
