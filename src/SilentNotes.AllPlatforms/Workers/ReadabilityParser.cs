// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Extracts the readable main content of a web page and removes navigation, ads, scripts and
    /// other clutter, so an article can be cached and displayed offline.
    /// </summary>
    public static class ReadabilityParser
    {
        private static readonly Regex CommentRegex = new Regex(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
        private static readonly Regex HeadRegex = new Regex(@"<head\b[^>]*>[\s\S]*?</head\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RemoveBlockRegex = new Regex(
            @"<(script|style|noscript|template|svg|canvas|iframe|object|embed|form|button|input|select|textarea|nav|footer|aside)\b[^>]*>[\s\S]*?</\1\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RemoveSelfClosingRegex = new Regex(
            @"<(script|style|noscript|template|svg|canvas|iframe|object|embed|form|button|input|select|textarea|nav|footer|aside)\b[^>]*/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NoisyContainerRegex = new Regex(
            @"<(div|section|aside|ul|ol|li|span|p|header|footer|form|table)\b[^>]*(class|id)\s*=\s*[""'][^""']*" +
            @"(nav|menu|sidebar|side-bar|footer|header|comment|share|social|advert|adsbygoogle|sponsor|promo|related|newsletter|cookie|consent|popup|modal|banner|breadcrumb|subscribe|paywall|recommend|widget|toolbar)" +
            @"[^""']*[""'][^>]*>[\s\S]*?</\1\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ArticleRegex = new Regex(@"<article\b[^>]*>([\s\S]*?)</article\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MainRegex = new Regex(@"<main\b[^>]*>([\s\S]*?)</main\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BodyRegex = new Regex(@"<body\b[^>]*>([\s\S]*?)</body\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex AttributeRegex = new Regex(@"\s+([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"[ \t\r\n]+", RegexOptions.Compiled);

        private static readonly HashSet<string> AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "code",
            "a", "img", "figure", "figcaption", "table", "thead", "tbody", "tr", "td", "th",
            "strong", "b", "em", "i", "u", "br", "hr", "div", "span", "caption",
        };

        private static readonly HashSet<string> VoidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "hr", "img",
        };

        /// <summary>
        /// Extracts the readable article HTML.
        /// </summary>
        /// <param name="html">Raw page HTML.</param>
        /// <param name="baseUrl">Optional page URL used to make relative links absolute.</param>
        /// <returns>Cleaned article HTML, or null if no meaningful content was found.</returns>
        public static string Extract(string html, string baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            string cleaned = CommentRegex.Replace(html, string.Empty);
            cleaned = HeadRegex.Replace(cleaned, string.Empty);
            cleaned = RemoveBlockRegex.Replace(cleaned, " ");
            cleaned = RemoveSelfClosingRegex.Replace(cleaned, string.Empty);

            for (int i = 0; i < 4; i++)
            {
                string reduced = NoisyContainerRegex.Replace(cleaned, " ");
                if (string.Equals(reduced, cleaned, StringComparison.Ordinal))
                    break;
                cleaned = reduced;
            }

            Match article = ArticleRegex.Match(cleaned);
            if (article.Success && RssFeedParser.StripHtml(article.Groups[1].Value).Length > 300)
            {
                cleaned = article.Groups[1].Value;
            }
            else
            {
                Match main = MainRegex.Match(cleaned);
                if (main.Success && RssFeedParser.StripHtml(main.Groups[1].Value).Length > 300)
                    cleaned = main.Groups[1].Value;
                else
                {
                    Match body = BodyRegex.Match(cleaned);
                    if (body.Success)
                        cleaned = body.Groups[1].Value;
                }
            }

            cleaned = FilterTags(cleaned);
            cleaned = MakeLinksAbsolute(cleaned, baseUrl);
            cleaned = WhitespaceRegex.Replace(cleaned, " ").Trim();
            cleaned = RssFeedParser.SanitizeHtml(cleaned);

            if (RssFeedParser.StripHtml(cleaned).Length < 120)
                return null;

            return cleaned;
        }

        private static string FilterTags(string html)
        {
            return TagRegex.Replace(html, match =>
            {
                string tag = match.Value;
                bool closing = tag.StartsWith("</", StringComparison.Ordinal);
                string name = ExtractTagName(tag);
                if (name == null || !AllowedTags.Contains(name))
                    return string.Empty;

                if (closing)
                    return "</" + name + ">";

                if (name.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    string href = ExtractAttribute(tag, "href");
                    return string.IsNullOrEmpty(href) ? "<a>" : "<a href=\"" + href + "\">";
                }
                if (name.Equals("img", StringComparison.OrdinalIgnoreCase))
                {
                    string src = ExtractAttribute(tag, "src");
                    string alt = ExtractAttribute(tag, "alt");
                    if (string.IsNullOrEmpty(src))
                        return string.Empty;
                    return "<img src=\"" + src + "\"" + (string.IsNullOrEmpty(alt) ? string.Empty : " alt=\"" + alt + "\"") + "/>";
                }

                if (VoidTags.Contains(name))
                    return "<" + name + "/>";

                return "<" + name + ">";
            });
        }

        private static string MakeLinksAbsolute(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri))
                return html;

            return Regex.Replace(html, @"<(a|img)\b([^>]*)>", match =>
            {
                string tagName = match.Groups[1].Value;
                string attributes = match.Groups[2].Value;
                string attributeName = tagName.Equals("img", StringComparison.OrdinalIgnoreCase) ? "src" : "href";
                string value = ExtractAttribute(attributes, attributeName);
                if (string.IsNullOrWhiteSpace(value))
                    return match.Value;

                if (Uri.TryCreate(baseUri, value, out Uri absolute))
                {
                    if (tagName.Equals("img", StringComparison.OrdinalIgnoreCase))
                        return "<img src=\"" + absolute + "\"/>";
                    return "<a href=\"" + absolute + "\">";
                }

                return match.Value;
            }, RegexOptions.IgnoreCase);
        }

        private static string ExtractTagName(string tag)
        {
            int index = tag.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
                index++;
            int start = index;
            while (index < tag.Length && (char.IsLetterOrDigit(tag[index]) || tag[index] == '-'))
                index++;
            return index > start ? tag.Substring(start, index - start) : null;
        }

        private static string ExtractAttribute(string tag, string attributeName)
        {
            foreach (Match match in AttributeRegex.Matches(tag))
            {
                if (!string.Equals(match.Groups[1].Value, attributeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string value = match.Groups[2].Value.Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                    value = value.Substring(1, value.Length - 2);
                return value;
            }
            return null;
        }
    }
}
