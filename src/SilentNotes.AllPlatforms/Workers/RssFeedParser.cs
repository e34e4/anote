// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Parses RSS 2.0 and Atom feed XML into a list of articles.
    /// </summary>
    public static class RssFeedParser
    {
        private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";
        private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Parsed feed metadata and articles.
        /// </summary>
        public class ParsedFeed
        {
            public string Title { get; set; }
            public string SiteUrl { get; set; }
            public List<ParsedItem> Items { get; set; } = new List<ParsedItem>();
        }

        /// <summary>
        /// A single parsed article.
        /// </summary>
        public class ParsedItem
        {
            public string ItemKey { get; set; }
            public string Title { get; set; }
            public string Link { get; set; }
            public string Summary { get; set; }
            public string ContentHtml { get; set; }
            public DateTime? PublishedAt { get; set; }
        }

        /// <summary>
        /// Parses RSS or Atom XML text.
        /// </summary>
        /// <param name="xml">Raw feed XML.</param>
        /// <returns>Parsed feed, or null if the XML is not a recognizable feed.</returns>
        public static ParsedFeed Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return null;

            XDocument document;
            try
            {
                document = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (Exception)
            {
                return null;
            }

            XElement root = document.Root;
            if (root == null)
                return null;

            string localName = root.Name.LocalName;
            if (string.Equals(localName, "rss", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(localName, "rdf", StringComparison.OrdinalIgnoreCase))
            {
                return ParseRss(root);
            }

            if (string.Equals(localName, "feed", StringComparison.OrdinalIgnoreCase))
                return ParseAtom(root);

            // Some feeds use a bare <channel> root.
            if (string.Equals(localName, "channel", StringComparison.OrdinalIgnoreCase))
                return ParseRssChannel(root);

            return null;
        }

        private static ParsedFeed ParseRss(XElement root)
        {
            XElement channel = root.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "channel", StringComparison.OrdinalIgnoreCase));
            if (channel == null)
                return null;
            return ParseRssChannel(channel);
        }

        private static ParsedFeed ParseRssChannel(XElement channel)
        {
            var result = new ParsedFeed
            {
                Title = GetChildText(channel, "title"),
                SiteUrl = GetChildText(channel, "link"),
            };

            foreach (XElement item in channel.Elements().Where(e =>
                string.Equals(e.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase)))
            {
                string title = GetChildText(item, "title");
                string link = GetChildText(item, "link");
                string guid = GetChildText(item, "guid");
                string description = GetChildText(item, "description");
                string contentEncoded = GetChildText(item, ContentNs + "encoded")
                    ?? GetChildText(item, "encoded");
                string contentHtml = !string.IsNullOrWhiteSpace(contentEncoded) ? contentEncoded : description;
                DateTime? published = ParseDate(
                    GetChildText(item, "pubDate")
                    ?? GetChildText(item, DcNs + "date")
                    ?? GetChildText(item, "date"));

                result.Items.Add(new ParsedItem
                {
                    ItemKey = BuildItemKey(guid, link, title, published),
                    Title = CleanText(title),
                    Link = link?.Trim(),
                    Summary = BuildSummary(description ?? contentHtml),
                    ContentHtml = contentHtml ?? string.Empty,
                    PublishedAt = published,
                });
            }

            return result;
        }

        private static ParsedFeed ParseAtom(XElement feed)
        {
            var result = new ParsedFeed
            {
                Title = GetChildText(feed, AtomNs + "title") ?? GetChildText(feed, "title"),
                SiteUrl = GetAtomLink(feed, "alternate") ?? GetAtomLink(feed, null),
            };

            IEnumerable<XElement> entries = feed.Elements(AtomNs + "entry");
            if (!entries.Any())
                entries = feed.Elements().Where(e => string.Equals(e.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase));

            foreach (XElement entry in entries)
            {
                string title = GetChildText(entry, AtomNs + "title") ?? GetChildText(entry, "title");
                string link = GetAtomLink(entry, "alternate") ?? GetAtomLink(entry, null);
                string id = GetChildText(entry, AtomNs + "id") ?? GetChildText(entry, "id");
                string summary = GetChildText(entry, AtomNs + "summary") ?? GetChildText(entry, "summary");
                string content = GetChildText(entry, AtomNs + "content") ?? GetChildText(entry, "content");
                string contentHtml = !string.IsNullOrWhiteSpace(content) ? content : summary;
                DateTime? published = ParseDate(
                    GetChildText(entry, AtomNs + "published")
                    ?? GetChildText(entry, "published")
                    ?? GetChildText(entry, AtomNs + "updated")
                    ?? GetChildText(entry, "updated"));

                result.Items.Add(new ParsedItem
                {
                    ItemKey = BuildItemKey(id, link, title, published),
                    Title = CleanText(title),
                    Link = link?.Trim(),
                    Summary = BuildSummary(summary ?? contentHtml),
                    ContentHtml = contentHtml ?? string.Empty,
                    PublishedAt = published,
                });
            }

            return result;
        }

        private static string GetAtomLink(XElement parent, string rel)
        {
            IEnumerable<XElement> links = parent.Elements(AtomNs + "link");
            if (!links.Any())
                links = parent.Elements().Where(e => string.Equals(e.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase));

            foreach (XElement link in links)
            {
                string linkRel = (string)link.Attribute("rel");
                if (rel == null || string.Equals(linkRel, rel, StringComparison.OrdinalIgnoreCase) ||
                    (rel == "alternate" && string.IsNullOrEmpty(linkRel)))
                {
                    string href = (string)link.Attribute("href");
                    if (!string.IsNullOrWhiteSpace(href))
                        return href.Trim();
                }
            }

            return null;
        }

        private static string GetChildText(XElement parent, XName name)
        {
            XElement child = parent.Element(name);
            if (child == null && name.NamespaceName.Length == 0)
            {
                child = parent.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase));
            }
            return child?.Value;
        }

        private static string BuildItemKey(string guid, string link, string title, DateTime? published)
        {
            if (!string.IsNullOrWhiteSpace(guid))
                return "g:" + guid.Trim();
            if (!string.IsNullOrWhiteSpace(link))
                return "l:" + link.Trim();
            string stamp = published?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
            return "t:" + (title ?? string.Empty).Trim() + "|" + stamp;
        }

        private static string BuildSummary(string htmlOrText)
        {
            string plain = StripHtml(htmlOrText);
            if (string.IsNullOrEmpty(plain))
                return string.Empty;
            plain = Regex.Replace(plain, @"\s+", " ").Trim();
            return plain.Length <= 180 ? plain : plain.Substring(0, 180) + "…";
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            return StripHtml(text);
        }

        /// <summary>
        /// Strips HTML tags and decodes entities for plain text display.
        /// </summary>
        public static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            string cleaned = Regex.Replace(html, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"</(p|div|li|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);
            cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);
            return WebUtility.HtmlDecode(cleaned).Trim();
        }

        /// <summary>
        /// Removes script/iframe tags from HTML for safer in-app rendering.
        /// </summary>
        public static string SanitizeHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            string cleaned = Regex.Replace(html, @"<(script|iframe|object|embed)\b[^>]*>[\s\S]*?</\1>", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<(script|iframe|object|embed)\b[^>]*/?>", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", string.Empty, RegexOptions.IgnoreCase);
            return cleaned;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                return parsed;
            }

            // RFC 822 style dates often used in RSS.
            string[] formats =
            {
                "ddd, dd MMM yyyy HH:mm:ss K",
                "ddd, dd MMM yyyy HH:mm:ss zzz",
                "ddd, d MMM yyyy HH:mm:ss K",
                "ddd, d MMM yyyy HH:mm:ss zzz",
            };
            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
