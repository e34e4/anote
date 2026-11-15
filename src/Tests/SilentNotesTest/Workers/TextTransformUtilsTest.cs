// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class TextTransformUtilsTest
    {
        [TestMethod]
        public void Base64RoundTripRestoresOriginalText()
        {
            string original = "Hello aNote, äöü 123";
            var steps = new List<TextTransformStep>
            {
                new TextTransformStep { Kind = TextTransformKind.Base64Encode },
                new TextTransformStep { Kind = TextTransformKind.Base64Decode },
            };

            Assert.IsTrue(TextTransformUtils.TryApply(original, steps, out string result, out string error));
            Assert.IsNull(error);
            Assert.AreEqual(original, result);
        }

        [TestMethod]
        public void Base64DecodeReportsInvalidInput()
        {
            var steps = new List<TextTransformStep> { new TextTransformStep { Kind = TextTransformKind.Base64Decode } };

            Assert.IsFalse(TextTransformUtils.TryApply("not base64 %%%", steps, out _, out string error));
            StringAssert.Contains(error, "Base64");
        }

        [TestMethod]
        public void UrlRoundTripRestoresOriginalText()
        {
            string original = "a b&c=d/ä";
            var steps = new List<TextTransformStep>
            {
                new TextTransformStep { Kind = TextTransformKind.UrlEncode },
                new TextTransformStep { Kind = TextTransformKind.UrlDecode },
            };

            Assert.IsTrue(TextTransformUtils.TryApply(original, steps, out string result, out _));
            Assert.AreEqual(original, result);
        }

        [TestMethod]
        public void JsonPrettyPrintAndMinify()
        {
            string compact = "{\"a\":1,\"b\":[1,2]}";
            string pretty = TextTransformUtils.ApplyStep(compact, new TextTransformStep { Kind = TextTransformKind.JsonPrettyPrint });
            StringAssert.Contains(pretty, "\n");
            StringAssert.Contains(pretty, "\"a\": 1");

            string minified = TextTransformUtils.ApplyStep(pretty, new TextTransformStep { Kind = TextTransformKind.JsonMinify });
            Assert.AreEqual(compact, minified);
        }

        [TestMethod]
        public void XmlPrettyPrintAndMinify()
        {
            string compact = "<root><item id=\"1\">x</item></root>";
            string pretty = TextTransformUtils.ApplyStep(compact, new TextTransformStep { Kind = TextTransformKind.XmlPrettyPrint });
            StringAssert.Contains(pretty, "\n");

            string minified = TextTransformUtils.ApplyStep(pretty, new TextTransformStep { Kind = TextTransformKind.XmlMinify });
            Assert.AreEqual(compact, minified);
        }

        [TestMethod]
        public void HashReturnsKnownDigests()
        {
            string md5 = TextTransformUtils.ApplyStep("abc", new TextTransformStep { Kind = TextTransformKind.Md5Hash });
            string sha256 = TextTransformUtils.ApplyStep("abc", new TextTransformStep { Kind = TextTransformKind.Sha256Hash });

            Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", md5);
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha256);
        }

        [TestMethod]
        public void SortAndDeduplicateLines()
        {
            string input = "b\na\nb\n\nc";

            string sorted = TextTransformUtils.ApplyStep(input, new TextTransformStep { Kind = TextTransformKind.SortLinesAscending });
            Assert.AreEqual("\na\nb\nb\nc", sorted);

            string deduplicated = TextTransformUtils.ApplyStep(input, new TextTransformStep { Kind = TextTransformKind.DeduplicateLines });
            Assert.AreEqual("b\na\n\nc", deduplicated);

            string withoutEmpty = TextTransformUtils.ApplyStep(input, new TextTransformStep { Kind = TextTransformKind.RemoveEmptyLines });
            Assert.AreEqual("b\na\nb\nc", withoutEmpty);
        }

        [TestMethod]
        public void RegexExtractReturnsFirstGroup()
        {
            string input = "id=17 name=abc id=42";
            string result = TextTransformUtils.ApplyStep(input, new TextTransformStep
            {
                Kind = TextTransformKind.RegexExtract,
                Parameter = @"id=(\d+)",
            });

            Assert.AreEqual("17\n42", result);
        }

        [TestMethod]
        public void RegexExtractWithoutPatternFails()
        {
            Assert.IsFalse(TextTransformUtils.TryApply(
                "text",
                new[] { new TextTransformStep { Kind = TextTransformKind.RegexExtract } },
                out _,
                out string error));
            StringAssert.Contains(error, "regular expression");
        }

        [TestMethod]
        public void AllKindsHaveDisplayNames()
        {
            foreach (TextTransformKind kind in TextTransformUtils.AllKinds)
                Assert.IsFalse(string.IsNullOrWhiteSpace(TextTransformUtils.GetDisplayName(kind)));
            Assert.AreEqual(TextTransformUtils.AllKinds.Count, TextTransformUtils.AllKinds.Distinct().Count());
        }
    }
}
