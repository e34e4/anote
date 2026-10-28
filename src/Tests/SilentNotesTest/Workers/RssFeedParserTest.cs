// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class RssFeedParserTest
    {
        [TestMethod]
        public void ParsesRss20Feed()
        {
            const string xml = @"<?xml version=""1.0""?>
<rss version=""2.0"">
  <channel>
    <title>Example Feed</title>
    <link>https://example.com/</link>
    <item>
      <title>Hello &amp; World</title>
      <link>https://example.com/hello</link>
      <guid>item-1</guid>
      <description>Short &lt;b&gt;preview&lt;/b&gt;</description>
      <pubDate>Mon, 01 Jan 2024 12:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>";

            RssFeedParser.ParsedFeed feed = RssFeedParser.Parse(xml);
            Assert.IsNotNull(feed);
            Assert.AreEqual("Example Feed", feed.Title);
            Assert.AreEqual(1, feed.Items.Count);
            Assert.AreEqual("Hello & World", feed.Items[0].Title);
            Assert.AreEqual("g:item-1", feed.Items[0].ItemKey);
            Assert.IsTrue(feed.Items[0].Summary.Contains("preview"));
            Assert.IsNotNull(feed.Items[0].PublishedAt);
        }

        [TestMethod]
        public void ParsesAtomFeed()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>Atom Example</title>
  <link href=""https://example.org/"" rel=""alternate""/>
  <entry>
    <title>Atom Item</title>
    <link href=""https://example.org/1""/>
    <id>urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a</id>
    <updated>2024-02-01T18:30:02Z</updated>
    <summary>Atom summary</summary>
  </entry>
</feed>";

            RssFeedParser.ParsedFeed feed = RssFeedParser.Parse(xml);
            Assert.IsNotNull(feed);
            Assert.AreEqual("Atom Example", feed.Title);
            Assert.AreEqual(1, feed.Items.Count);
            Assert.AreEqual("Atom Item", feed.Items[0].Title);
            Assert.IsTrue(feed.Items[0].ItemKey.StartsWith("g:"));
        }

        [TestMethod]
        public void OpmlRoundTrip()
        {
            var feeds = new RssFeedListModel
            {
                new RssFeedModel { Title = "A", Url = "https://a.example/rss", SiteUrl = "https://a.example/" },
                new RssFeedModel { Title = "B", Url = "https://b.example/atom.xml" },
            };

            string opml = OpmlFormat.Export(feeds);
            RssFeedListModel imported = OpmlFormat.Import(opml);
            Assert.IsNotNull(imported);
            Assert.AreEqual(2, imported.Count);
            Assert.AreEqual("https://a.example/rss", imported[0].Url);
            Assert.AreEqual("A", imported[0].Title);
            Assert.AreEqual("https://a.example/", imported[0].SiteUrl);
        }
    }
}
