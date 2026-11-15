// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Import and export of RSS feed subscriptions as standard OPML 2.0.
    /// </summary>
    public static class OpmlFormat
    {
        /// <summary>
        /// Serializes feeds to an OPML 2.0 document. Feeds with a category are grouped into
        /// nested outline folders as defined by the OPML specification.
        /// </summary>
        public static string Export(IEnumerable<RssFeedModel> feeds, string title = "aNote RSS Feeds")
        {
            var body = new XElement("body");
            var feedsByCategory = (feeds ?? Enumerable.Empty<RssFeedModel>())
                .Where(feed => feed != null && !string.IsNullOrWhiteSpace(feed.Url))
                .GroupBy(feed => feed.Category?.Trim() ?? string.Empty)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, RssFeedModel> group in feedsByCategory)
            {
                var outlines = group
                    .OrderBy(feed => string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(CreateFeedOutline)
                    .ToList();

                if (string.IsNullOrEmpty(group.Key))
                {
                    foreach (XElement outline in outlines)
                        body.Add(outline);
                }
                else
                {
                    var folder = new XElement("outline",
                        new XAttribute("text", group.Key),
                        new XAttribute("title", group.Key));
                    folder.Add(outlines);
                    body.Add(folder);
                }
            }

            string now = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("opml",
                    new XAttribute("version", "2.0"),
                    new XElement("head",
                        new XElement("title", title ?? "aNote RSS Feeds"),
                        new XElement("dateCreated", now),
                        new XElement("dateModified", now),
                        new XElement("ownerName", "aNote")),
                    body));

            var builder = new StringBuilder();
            builder.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            builder.Append(document.ToString(SaveOptions.None));
            return builder.ToString();
        }

        private static XElement CreateFeedOutline(RssFeedModel feed)
        {
            string feedTitle = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title.Trim();
            var outline = new XElement("outline",
                new XAttribute("text", feedTitle),
                new XAttribute("title", feedTitle),
                new XAttribute("type", "rss"),
                new XAttribute("xmlUrl", feed.Url.Trim()));

            if (!string.IsNullOrWhiteSpace(feed.SiteUrl))
                outline.Add(new XAttribute("htmlUrl", feed.SiteUrl.Trim()));

            return outline;
        }

        /// <summary>
        /// Parses a standard OPML document into feed models. Nested outlines are flattened, the
        /// folder title is preserved as <see cref="RssFeedModel.Category"/>.
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
            XElement body = root.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase));
            if (body != null)
                ReadOutlines(body, null, result, seenUrls);

            return result.Count > 0 ? result : null;
        }

        private static void ReadOutlines(XElement parent, string category, RssFeedListModel result, HashSet<string> seenUrls)
        {
            foreach (XElement outline in parent.Elements().Where(element =>
                string.Equals(element.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase)))
            {
                string xmlUrl = GetAttr(outline, "xmlUrl");
                if (string.IsNullOrWhiteSpace(xmlUrl))
                {
                    string folderName = GetAttr(outline, "title") ?? GetAttr(outline, "text");
                    ReadOutlines(outline, string.IsNullOrWhiteSpace(folderName) ? category : folderName.Trim(), result, seenUrls);
                    continue;
                }

                xmlUrl = xmlUrl.Trim();
                if (!seenUrls.Add(xmlUrl))
                    continue;

                string title = GetAttr(outline, "title") ?? GetAttr(outline, "text") ?? xmlUrl;
                string siteUrl = GetAttr(outline, "htmlUrl");
                string feedCategory = GetAttr(outline, "category") ?? category;

                result.Add(new RssFeedModel
                {
                    Title = title?.Trim(),
                    Url = xmlUrl,
                    SiteUrl = string.IsNullOrWhiteSpace(siteUrl) ? null : siteUrl.Trim(),
                    Category = string.IsNullOrWhiteSpace(feedCategory) ? null : feedCategory.Trim(),
                });
            }
        }

        private static string GetAttr(XElement element, string name)
        {
            XAttribute attr = element.Attributes().FirstOrDefault(a =>
                string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attr?.Value;
        }
    }
}
