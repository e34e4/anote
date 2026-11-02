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
    public class AbroAiMarkdownTest
    {
        [TestMethod]
        public void ParsesHeadingsFencesAndLists()
        {
            const string md = @"## 3. Pseudo-code

Intro text.

```python
def ingest():
    return 1
```

- first
- second

1. alpha
2. beta

> quoted
";

            List<AbroAiMarkdown.Block> blocks = AbroAiMarkdown.Parse(md);
            Assert.AreEqual(AbroAiMarkdown.BlockKind.Heading, blocks[0].Kind);
            Assert.AreEqual(2, blocks[0].Level);
            Assert.AreEqual("3. Pseudo-code", blocks[0].Text);

            Assert.AreEqual(AbroAiMarkdown.BlockKind.Paragraph, blocks[1].Kind);
            Assert.AreEqual("Intro text.", blocks[1].Text);

            Assert.AreEqual(AbroAiMarkdown.BlockKind.Code, blocks[2].Kind);
            Assert.AreEqual("python", blocks[2].Language);
            Assert.IsTrue(blocks[2].Text.Contains("def ingest():"));

            Assert.AreEqual(AbroAiMarkdown.BlockKind.ListItem, blocks[3].Kind);
            Assert.AreEqual("first", blocks[3].Text);
            Assert.IsFalse(blocks[3].ContinuesList);

            Assert.AreEqual(AbroAiMarkdown.BlockKind.ListItem, blocks[4].Kind);
            Assert.IsTrue(blocks[4].ContinuesList);

            Assert.AreEqual(AbroAiMarkdown.BlockKind.ListItem, blocks[5].Kind);
            Assert.AreEqual(1, blocks[5].Level); // numbered
            Assert.AreEqual("alpha", blocks[5].Text);

            Assert.AreEqual(AbroAiMarkdown.BlockKind.Quote, blocks[7].Kind);
            Assert.AreEqual("quoted", blocks[7].Text);
        }

        [TestMethod]
        public void PreservesAsciiTreeLineBreaks()
        {
            const string md = @"API
├── GET /feed
└── POST /ingest";

            List<AbroAiMarkdown.Block> blocks = AbroAiMarkdown.Parse(md);
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(AbroAiMarkdown.BlockKind.Paragraph, blocks[0].Kind);
            Assert.IsTrue(blocks[0].Text.Contains("\n├── GET /feed\n"));
        }

        [TestMethod]
        public void ParsesInlineBoldItalicAndCode()
        {
            List<AbroAiMarkdown.Inline> spans = AbroAiMarkdown.ParseInlines("use **bold** and *italic* plus `code`");
            Assert.AreEqual(6, spans.Count);
            Assert.AreEqual(AbroAiMarkdown.InlineKind.Text, spans[0].Kind);
            Assert.AreEqual(AbroAiMarkdown.InlineKind.Bold, spans[1].Kind);
            Assert.AreEqual("bold", spans[1].Text);
            Assert.AreEqual(AbroAiMarkdown.InlineKind.Italic, spans[3].Kind);
            Assert.AreEqual("italic", spans[3].Text);
            Assert.AreEqual(AbroAiMarkdown.InlineKind.Code, spans[5].Kind);
            Assert.AreEqual("code", spans[5].Text);
        }
    }
}
