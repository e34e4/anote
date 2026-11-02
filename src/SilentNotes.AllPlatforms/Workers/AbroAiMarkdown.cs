// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Lightweight Markdown block parser for Abro AI chat bubbles. Not a full CommonMark
    /// implementation — enough for typical assistant replies (headers, fences, lists, inline).
    /// </summary>
    public static class AbroAiMarkdown
    {
        /// <summary>Block kinds produced by <see cref="Parse"/>.</summary>
        public enum BlockKind
        {
            Paragraph,
            Heading,
            Code,
            ListItem,
            Quote,
        }

        /// <summary>A rendered Markdown block.</summary>
        public sealed class Block
        {
            public BlockKind Kind { get; set; }

            /// <summary>Heading level 1–6, or 0 for other blocks.</summary>
            public int Level { get; set; }

            /// <summary>Language tag for fenced code, if any.</summary>
            public string Language { get; set; }

            /// <summary>Plain text content of the block (inline markers still present for paragraphs).</summary>
            public string Text { get; set; }

            /// <summary>True when this list item continues a list started by a previous ListItem.</summary>
            public bool ContinuesList { get; set; }
        }

        /// <summary>An inline span inside a paragraph/heading/list item.</summary>
        public enum InlineKind
        {
            Text,
            Bold,
            Italic,
            Code,
        }

        /// <summary>Inline run.</summary>
        public sealed class Inline
        {
            public InlineKind Kind { get; set; }

            public string Text { get; set; }
        }

        private static readonly Regex FenceStart = new Regex(
            @"^```([\w+-]*)\s*$",
            RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(
            @"^(#{1,6})\s+(.*)$",
            RegexOptions.Compiled);
        private static readonly Regex Unordered = new Regex(
            @"^(\s*)[-*+]\s+(.*)$",
            RegexOptions.Compiled);
        private static readonly Regex Ordered = new Regex(
            @"^(\s*)\d+[.)]\s+(.*)$",
            RegexOptions.Compiled);
        private static readonly Regex Quote = new Regex(
            @"^>\s?(.*)$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses Markdown into a sequence of blocks.
        /// </summary>
        public static List<Block> Parse(string markdown)
        {
            var blocks = new List<Block>();
            if (string.IsNullOrEmpty(markdown))
                return blocks;

            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int i = 0;
            bool inList = false;
            while (i < lines.Length)
            {
                string line = lines[i];

                Match fence = FenceStart.Match(line);
                if (fence.Success)
                {
                    inList = false;
                    string lang = fence.Groups[1].Value;
                    i++;
                    var code = new StringBuilder();
                    while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                    {
                        if (code.Length > 0)
                            code.Append('\n');
                        code.Append(lines[i]);
                        i++;
                    }

                    if (i < lines.Length && lines[i].StartsWith("```", StringComparison.Ordinal))
                        i++;

                    blocks.Add(new Block
                    {
                        Kind = BlockKind.Code,
                        Language = lang,
                        Text = code.ToString(),
                    });
                    continue;
                }

                Match heading = Heading.Match(line);
                if (heading.Success)
                {
                    inList = false;
                    blocks.Add(new Block
                    {
                        Kind = BlockKind.Heading,
                        Level = heading.Groups[1].Value.Length,
                        Text = heading.Groups[2].Value.TrimEnd(),
                    });
                    i++;
                    continue;
                }

                Match quote = Quote.Match(line);
                if (quote.Success)
                {
                    inList = false;
                    var quoted = new StringBuilder(quote.Groups[1].Value);
                    i++;
                    while (i < lines.Length)
                    {
                        Match cont = Quote.Match(lines[i]);
                        if (!cont.Success)
                            break;
                        quoted.Append('\n').Append(cont.Groups[1].Value);
                        i++;
                    }

                    blocks.Add(new Block
                    {
                        Kind = BlockKind.Quote,
                        Text = quoted.ToString(),
                    });
                    continue;
                }

                Match unordered = Unordered.Match(line);
                Match ordered = unordered.Success ? null : Ordered.Match(line);
                if (unordered.Success || (ordered != null && ordered.Success))
                {
                    Match listMatch = unordered.Success ? unordered : ordered;
                    blocks.Add(new Block
                    {
                        Kind = BlockKind.ListItem,
                        Text = listMatch.Groups[2].Value,
                        ContinuesList = inList,
                        Level = unordered.Success ? 0 : 1, // 0 = bullet, 1 = numbered marker reserved
                    });
                    inList = true;
                    i++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    inList = false;
                    i++;
                    continue;
                }

                inList = false;
                var para = new StringBuilder(line);
                i++;
                while (i < lines.Length
                    && !string.IsNullOrWhiteSpace(lines[i])
                    && !FenceStart.IsMatch(lines[i])
                    && !Heading.IsMatch(lines[i])
                    && !Quote.IsMatch(lines[i])
                    && !Unordered.IsMatch(lines[i])
                    && !Ordered.IsMatch(lines[i]))
                {
                    // Keep line breaks so ASCII trees / diagrams stay readable in chat.
                    para.Append('\n').Append(lines[i]);
                    i++;
                }

                blocks.Add(new Block
                {
                    Kind = BlockKind.Paragraph,
                    Text = para.ToString(),
                });
            }

            return blocks;
        }

        /// <summary>
        /// Parses a limited set of inline markers: <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>.
        /// </summary>
        public static List<Inline> ParseInlines(string text)
        {
            var result = new List<Inline>();
            if (string.IsNullOrEmpty(text))
                return result;

            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '`' )
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        result.Add(new Inline { Kind = InlineKind.Code, Text = text.Substring(i + 1, end - i - 1) });
                        i = end + 1;
                        continue;
                    }
                }

                if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        result.Add(new Inline { Kind = InlineKind.Bold, Text = text.Substring(i + 2, end - i - 2) });
                        i = end + 2;
                        continue;
                    }
                }

                if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end > i)
                    {
                        result.Add(new Inline { Kind = InlineKind.Italic, Text = text.Substring(i + 1, end - i - 1) });
                        i = end + 1;
                        continue;
                    }
                }

                int next = FindNextMarker(text, i);
                result.Add(new Inline { Kind = InlineKind.Text, Text = text.Substring(i, next - i) });
                i = next;
            }

            return result;
        }

        private static int FindNextMarker(string text, int start)
        {
            for (int i = start + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '`' || c == '*')
                    return i;
            }

            return text.Length;
        }
    }
}
