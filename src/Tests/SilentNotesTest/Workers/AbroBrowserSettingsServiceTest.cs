// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Services;

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
    }
}
