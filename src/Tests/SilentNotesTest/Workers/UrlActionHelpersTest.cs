// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Moq;
using NUnit.Framework;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestFixture]
    public class UrlActionHelpersTest
    {
        [Test]
        public void ContainsOpenableUrl_FindsHttpLinkInText()
        {
            Assert.IsTrue(UrlActionHelpers.ContainsOpenableUrl("see https://example.com/path today"));
        }

        [Test]
        public void OpenFirstUrl_OpensFirstLink()
        {
            var browser = new Mock<INativeBrowserService>();
            UrlActionHelpers.OpenFirstUrl(browser.Object, "a https://example.com b https://other.test");
            browser.Verify(b => b.OpenWebsite("https://example.com"), Times.Once);
        }

        [Test]
        public void TryGetFirstOpenableUrl_RejectsNonHttpSchemesInText()
        {
            Assert.IsFalse(UrlActionHelpers.TryGetFirstOpenableUrl("mailto:test@example.com", out _));
        }
    }
}
