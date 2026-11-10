// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class AbroBrowserSettingsServiceTest
    {
        [TestMethod]
        public void ParsesOpenSearchAndDuckDuckGoSuggestions()
        {
            IReadOnlyList<string> google = AbroBrowserSettingsService.ParseSuggestions(
                "[\"cat\",[\"cats\",\"cathode\",\"cathedral\"]]", 8);
            Assert.AreEqual(3, google.Count);
            Assert.AreEqual("cats", google[0]);

            IReadOnlyList<string> ddg = AbroBrowserSettingsService.ParseSuggestions(
                "[{\"phrase\":\"cats\"},{\"phrase\":\"cathode\"}]", 8);
            Assert.AreEqual(2, ddg.Count);
            Assert.AreEqual("cats", ddg[0]);
        }

        [TestMethod]
        public void BlackModeAppliesByModeAndDomainList()
        {
            var settings = new AbroBrowserSettingsModel
            {
                BlackMode = AbroBlackMode.Disable,
            };
            Assert.IsFalse(settings.ShouldApplyBlackMode("https://example.com/"));

            settings.BlackMode = AbroBlackMode.Enable;
            Assert.IsTrue(settings.ShouldApplyBlackMode("https://example.com/page"));

            settings.BlackMode = AbroBlackMode.Selectively;
            settings.BlackModeDomains.Add("example.com");
            Assert.IsTrue(settings.ShouldApplyBlackMode("https://www.example.com/a"));
            Assert.IsTrue(settings.ShouldApplyBlackMode("https://news.example.com/"));
            Assert.IsFalse(settings.ShouldApplyBlackMode("https://other.com/"));
        }

        [TestMethod]
        public void SerializesAndParsesBlackModeSettings()
        {
            var settings = new AbroBrowserSettingsModel
            {
                BlackMode = AbroBlackMode.Selectively,
            };
            settings.BlackModeDomains.Add("Example.COM");
            settings.BlackModeDomains.Add("news.site.org");

            string json = AbroBrowserSettingsFormat.Serialize(settings);
            AbroBrowserSettingsModel parsed = AbroBrowserSettingsFormat.Parse(json);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(AbroBlackMode.Selectively, parsed.BlackMode);
            Assert.AreEqual(2, parsed.BlackModeDomains.Count);
            Assert.AreEqual("example.com", parsed.BlackModeDomains[0]);
            Assert.AreEqual("news.site.org", parsed.BlackModeDomains[1]);
        }

        [TestMethod]
        public void SerializesAndParsesBlockImagesAndSocks5()
        {
            var settings = new AbroBrowserSettingsModel
            {
                BlockImagesMode = AbroBlockImagesMode.Selectively,
                BlockImagesDomains = { "imgs.example.com", "cdn.news.org" },
                Socks5Enabled = true,
                Socks5Host = "127.0.0.1",
                Socks5Port = 9050,
                Socks5Username = "u",
                Socks5Password = "p",
            };

            string json = AbroBrowserSettingsFormat.Serialize(settings);
            AbroBrowserSettingsModel parsed = AbroBrowserSettingsFormat.Parse(json);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(AbroBlockImagesMode.Selectively, parsed.BlockImagesMode);
            Assert.AreEqual(2, parsed.BlockImagesDomains.Count);
            Assert.AreEqual("imgs.example.com", parsed.BlockImagesDomains[0]);
            Assert.IsTrue(parsed.ShouldApplyBlockImages("https://imgs.example.com/page"));
            Assert.IsFalse(parsed.ShouldApplyBlockImages("https://other.com/page"));
            Assert.IsTrue(parsed.Socks5Enabled);
            Assert.IsTrue(parsed.HasSocks5Config);
            Assert.AreEqual("127.0.0.1", parsed.Socks5Host);
            Assert.AreEqual(9050, parsed.Socks5Port);
            Assert.AreEqual("u", parsed.Socks5Username);
            Assert.AreEqual("p", parsed.Socks5Password);
        }

        [TestMethod]
        public void ParsesLegacyBlockImagesBoolAsEnable()
        {
            const string json = """
                {
                  "version": 1,
                  "blockImages": true,
                  "engines": []
                }
                """;
            AbroBrowserSettingsModel parsed = AbroBrowserSettingsFormat.Parse(json);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(AbroBlockImagesMode.Enable, parsed.BlockImagesMode);
            Assert.IsTrue(parsed.BlockImages);
        }
    }
}
