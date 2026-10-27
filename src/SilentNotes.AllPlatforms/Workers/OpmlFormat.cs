// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Import and export of RSS feed subscriptions as OPML 2.0.
    /// </summary>
    public static class OpmlFormat
    {
        /// <summary>
        /// Serializes feeds to an OPML 2.0 document.
        /// </summary>
        public static string Export(IEnumerable<RssFeedModel> feeds, string title = "aNote RSS Feeds")
        {
            var body = new XElement("body");
            foreach (RssFeedModel feed in feeds ?? Enumerable.Empty<RssFeedModel>())
            {
                if (string.IsNullOrWhiteSpace(feed?.Url))
                    continue;

                string feedTitle = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title.Trim();
                var outline = new XElement("outline",
                    new XAttribute("text", feedTitle),
                    new XAttribute("title", feedTitle),
                    new XAttribute("type", "rss"),
                    new XAttribute("xmlUrl", feed.Url.Trim()));

                if (!string.IsNullOrWhiteSpace(feed.SiteUrl))
                    outline.Add(new XAttribute("htmlUrl", feed.SiteUrl.Trim()));

                body.Add(outline);
            }

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("opml",
                    new XAttribute("version", "2.0"),
                    new XElement("head",
                        new XElement("title", title ?? "aNote RSS Feeds"),
                        new XElement("dateCreated", DateTime.UtcNow.ToString("r"))),
                    body));

            var builder = new StringBuilder();
            builder.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            builder.Append(document.ToString(SaveOptions.None));
            return builder.ToString();
        }

        /// <summary>
        /// Parses an OPML document into feed models. Nested outlines are flattened.
        /// </summary>
        /// <returns>Imported feeds, or null if the text is not valid OPML with any feeds.</returns>
        public static RssFeedListModel Import(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            XDocument document;
            try
            {
                document = XDocument.Parse(text, LoadOptions.None);
            }
            catch (Exception)
            {
                return null;
            }

            XElement root = document.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
                return null;

            var result = new RssFeedListModel();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement outline in root.Descendants().Where(e =>
                string.Equals(e.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase)))
            {
                string xmlUrl = GetAttr(outline, "xmlUrl") ?? GetAttr(outline, "xmlurl");
                if (string.IsNullOrWhiteSpace(xmlUrl))
                    continue;

                xmlUrl = xmlUrl.Trim();
                if (!seenUrls.Add(xmlUrl))
                    continue;

                string title = GetAttr(outline, "title")
                    ?? GetAttr(outline, "text")
                    ?? xmlUrl;
                string siteUrl = GetAttr(outline, "htmlUrl") ?? GetAttr(outline, "htmlurl");

                result.Add(new RssFeedModel
                {
                    Title = title?.Trim(),
                    Url = xmlUrl,
                    SiteUrl = string.IsNullOrWhiteSpace(siteUrl) ? null : siteUrl.Trim(),
                });
            }

            return result.Count > 0 ? result : null;
        }

        private static string GetAttr(XElement element, string name)
        {
            XAttribute attr = element.Attributes().FirstOrDefault(a =>
                string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attr?.Value;
        }
    }
}
