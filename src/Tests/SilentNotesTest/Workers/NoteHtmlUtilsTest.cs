// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class NoteHtmlUtilsTest
    {
        [TestMethod]
        public void GetTitlePrefersFirstHeading()
        {
            Assert.AreEqual("My title",
                NoteHtmlUtils.GetTitle("<p>intro</p><h1>My title</h1><p>body</p>"));
        }

        [TestMethod]
        public void GetTitleFallsBackToTextAndDecodesEntities()
        {
            Assert.AreEqual("Tom & Jerry", NoteHtmlUtils.GetTitle("<p>Tom &amp; Jerry</p>"));
        }

        [TestMethod]
        public void GetTitleOfEmptyContentIsEmpty()
        {
            Assert.AreEqual(string.Empty, NoteHtmlUtils.GetTitle(null));
            Assert.AreEqual(string.Empty, NoteHtmlUtils.GetTitle("<p></p>"));
        }

        [TestMethod]
        public void GetPlainTextKeepsParagraphsAndListItems()
        {
            string text = NoteHtmlUtils.GetPlainText("<h2>Title</h2><p>One</p><ul><li>Two</li><li>Three</li></ul>");
            StringAssert.Contains(text, "Title\nOne");
            StringAssert.Contains(text, "- Two");
        }

        [TestMethod]
        public void SlugifyCreatesFileNameSafeText()
        {
            Assert.AreEqual("my-note-title", NoteHtmlUtils.Slugify("My note: title!"));
            Assert.AreEqual("note", NoteHtmlUtils.Slugify("   "));
            Assert.AreEqual("a-b", NoteHtmlUtils.Slugify("a---b"));
        }

        [TestMethod]
        public void MarkdownConverterProducesReadableMarkdown()
        {
            string html = "<h1>Title</h1><p>Some <strong>bold</strong> and <a href=\"https://example.com\">a link</a>.</p>" +
                "<ul><li>First</li><li>Second</li></ul>";

            string markdown = SimpleHtmlToMarkdown.Convert(html);

            StringAssert.Contains(markdown, "# Title");
            StringAssert.Contains(markdown, "**bold**");
            StringAssert.Contains(markdown, "[a link](https://example.com)");
            StringAssert.Contains(markdown, "- First");
            StringAssert.Contains(markdown, "- Second");
        }

        [TestMethod]
        public void MarkdownConverterReplacesEmbeddedImages()
        {
            string html = "<p><img src=\"data:image/png;base64,AAAA\" alt=\"pic\"></p>";
            string markdown = SimpleHtmlToMarkdown.Convert(html);

            StringAssert.Contains(markdown, "[image: pic]");
            Assert.IsFalse(markdown.Contains("data:image"));
        }
    }
}
