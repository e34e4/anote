// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class AbroAdBlockListParserTest
    {
        [TestMethod]
        public void ParsesHostsPlainAndAdGuardDomains()
        {
            const string text = @"
# comment
0.0.0.0 ads.example.com
127.0.0.1 tracker.test
doubleclick.net
||adservice.google.com^
||path.example.com^$script
##.ad-banner
@@||whitelist.example.com^
";

            HashSet<string> domains = AbroAdBlockListParser.Parse(text);
            Assert.IsTrue(domains.Contains("ads.example.com"));
            Assert.IsTrue(domains.Contains("tracker.test"));
            Assert.IsTrue(domains.Contains("doubleclick.net"));
            Assert.IsTrue(domains.Contains("adservice.google.com"));
            Assert.IsFalse(domains.Contains("path.example.com"));
            Assert.IsFalse(domains.Contains("whitelist.example.com"));
        }
    }
}
