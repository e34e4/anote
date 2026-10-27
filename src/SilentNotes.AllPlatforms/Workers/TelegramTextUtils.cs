// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Text helpers for Telegram: emoji → symbolic form, display formatting, and markdown-lite parsing.
    /// Outgoing MarkdownV2 can also be handled by WTelegram <c>TL.Markdown.MarkdownToEntities</c> in the service.
    /// </summary>
    public static class TelegramTextUtils
    {
        /// <summary>
        /// Replaces emoji (including ZWJ / flag / keycap sequences) with symbolic forms like <c>:U+1F600:</c>.
        /// </summary>
        public static string ReplaceEmojisWithSymbolic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = new StringBuilder(text.Length);
            var sequence = new List<int>();
            int i = 0;
            while (i < text.Length)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out Rune rune, out int consumed);
                int cp = rune.Value;

                if (IsEmojiCodepoint(cp) || (sequence.Count > 0 && IsEmojiJoinerOrModifier(cp)))
                {
                    sequence.Add(cp);
                    i += consumed;
                    // Continue absorbing ZWJ / VS16 / skin tone / keycap / tag continuations.
                    continue;
                }

                if (sequence.Count > 0)
                {
                    FlushEmojiSequence(result, sequence);
                    sequence.Clear();
                }

                result.Append(rune.ToString());
                i += consumed;
            }

            if (sequence.Count > 0)
                FlushEmojiSequence(result, sequence);

            return result.ToString();
        }

        /// <summary>
        /// Builds a simple display string from plain text + entity spans using markers
        /// (*bold*, _italic_, `code`) and HTML-escapes the surrounding text so it is safe for UI.
        /// </summary>
        public static string FormatForDisplay(string plainText, IReadOnlyList<TelegramTextEntitySpan> entities)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            string symbolic = ReplaceEmojisWithSymbolic(plainText);
            if (entities == null || entities.Count == 0)
                return EscapeHtml(symbolic);

            // Apply markers on UTF-16 offsets from the original plain text, then escape.
            // We work on the original text for offsets, then replace emojis on the marked result.
            var markers = new List<(int Index, string Insert, int Order)>();
            int order = 0;
            foreach (var entity in entities)
            {
                if (entity == null || entity.Length <= 0)
                    continue;
                int start = Math.Clamp(entity.Offset, 0, plainText.Length);
                int end = Math.Clamp(entity.Offset + entity.Length, 0, plainText.Length);
                if (end <= start)
                    continue;

                string open;
                string close;
                switch (entity.Kind)
                {
                    case TelegramTextFormat.Bold:
                        open = "*"; close = "*";
                        break;
                    case TelegramTextFormat.Italic:
                        open = "_"; close = "_";
                        break;
                    case TelegramTextFormat.Code:
                    case TelegramTextFormat.Pre:
                        open = "`"; close = "`";
                        break;
                    case TelegramTextFormat.Strike:
                        open = "~"; close = "~";
                        break;
                    case TelegramTextFormat.Spoiler:
                        open = "||"; close = "||";
                        break;
                    case TelegramTextFormat.Underline:
                        open = "__"; close = "__";
                        break;
                    case TelegramTextFormat.Link when !string.IsNullOrEmpty(entity.Url):
                        open = "[";
                        close = "](" + entity.Url + ")";
                        break;
                    default:
                        continue;
                }

                markers.Add((start, open, order++));
                markers.Add((end, close, order++));
            }

            markers.Sort((a, b) =>
            {
                int cmp = a.Index.CompareTo(b.Index);
                return cmp != 0 ? cmp : b.Order.CompareTo(a.Order); // close before open at same index
            });

            var sb = new StringBuilder(plainText.Length + markers.Count * 2);
            int cursor = 0;
            foreach (var marker in markers)
            {
                if (marker.Index > cursor)
                    sb.Append(plainText, cursor, marker.Index - cursor);
                sb.Append(marker.Insert);
                cursor = marker.Index;
            }
            if (cursor < plainText.Length)
                sb.Append(plainText, cursor, plainText.Length - cursor);

            return EscapeHtml(ReplaceEmojisWithSymbolic(sb.ToString()));
        }

        /// <summary>
        /// Parses markdown-lite: *bold* _italic_ `code` ```pre``` ~strike~ ||spoiler|| [text](url)
        /// into plain text + entity spans. Nesting is best-effort (non-overlapping greedy).
        /// </summary>
        public static (string PlainText, List<TelegramTextEntitySpan> Entities) ParseMarkdownLite(string markdown)
        {
            var entities = new List<TelegramTextEntitySpan>();
            if (string.IsNullOrEmpty(markdown))
                return (string.Empty, entities);

            var plain = new StringBuilder(markdown.Length);
            int i = 0;
            while (i < markdown.Length)
            {
                if (TryParseFence(markdown, ref i, "```", out string preBody))
                {
                    int offset = plain.Length;
                    plain.Append(preBody);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = preBody.Length,
                        Kind = TelegramTextFormat.Pre,
                    });
                    continue;
                }

                if (TryParseDelimited(markdown, ref i, "||", out string spoiler))
                {
                    int offset = plain.Length;
                    plain.Append(spoiler);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = spoiler.Length,
                        Kind = TelegramTextFormat.Spoiler,
                    });
                    continue;
                }

                if (markdown[i] == '[' && TryParseLink(markdown, ref i, out string linkText, out string url))
                {
                    int offset = plain.Length;
                    plain.Append(linkText);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = linkText.Length,
                        Kind = TelegramTextFormat.Link,
                        Url = url,
                    });
                    continue;
                }

                if (TryParseWrapped(markdown, ref i, '`', out string code))
                {
                    int offset = plain.Length;
                    plain.Append(code);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = code.Length,
                        Kind = TelegramTextFormat.Code,
                    });
                    continue;
                }

                if (TryParseWrapped(markdown, ref i, '*', out string bold))
                {
                    int offset = plain.Length;
                    plain.Append(bold);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = bold.Length,
                        Kind = TelegramTextFormat.Bold,
                    });
                    continue;
                }

                if (TryParseWrapped(markdown, ref i, '_', out string italic))
                {
                    int offset = plain.Length;
                    plain.Append(italic);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = italic.Length,
                        Kind = TelegramTextFormat.Italic,
                    });
                    continue;
                }

                if (TryParseWrapped(markdown, ref i, '~', out string strike))
                {
                    int offset = plain.Length;
                    plain.Append(strike);
                    entities.Add(new TelegramTextEntitySpan
                    {
                        Offset = offset,
                        Length = strike.Length,
                        Kind = TelegramTextFormat.Strike,
                    });
                    continue;
                }

                plain.Append(markdown[i]);
                i++;
            }

            return (plain.ToString(), entities);
        }

        /// <summary>HTML-escapes text for safe display.</summary>
        public static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        private static void FlushEmojiSequence(StringBuilder result, List<int> sequence)
        {
            result.Append(':');
            for (int n = 0; n < sequence.Count; n++)
            {
                if (n > 0)
                    result.Append(':');
                result.Append("U+");
                result.Append(sequence[n].ToString("X", CultureInfo.InvariantCulture));
            }
            result.Append(':');
        }

        private static bool IsEmojiCodepoint(int cp)
        {
            if (cp >= 0x1F600 && cp <= 0x1F64F) return true; // emoticons
            if (cp >= 0x1F300 && cp <= 0x1F5FF) return true; // misc symbols & pictographs
            if (cp >= 0x1F680 && cp <= 0x1F6FF) return true; // transport
            if (cp >= 0x1F700 && cp <= 0x1F77F) return true; // alchemical
            if (cp >= 0x1F780 && cp <= 0x1F7FF) return true; // geometric extended
            if (cp >= 0x1F800 && cp <= 0x1F8FF) return true; // arrows supplemental
            if (cp >= 0x1F900 && cp <= 0x1F9FF) return true; // supplemental symbols
            if (cp >= 0x1FA00 && cp <= 0x1FAFF) return true; // extended-A
            if (cp >= 0x1F1E6 && cp <= 0x1F1FF) return true; // regional indicators (flags)
            if (cp >= 0x2600 && cp <= 0x26FF) return true;   // misc symbols
            if (cp >= 0x2700 && cp <= 0x27BF) return true;   // dingbats
            if (cp >= 0x2300 && cp <= 0x23FF) return true;   // misc technical (⌚ etc.)
            if (cp >= 0x2B00 && cp <= 0x2BFF) return true;   // misc symbols and arrows
            if (cp == 0x3030 || cp == 0x303D) return true;
            if (cp == 0x3297 || cp == 0x3299) return true;
            if (cp == 0x00A9 || cp == 0x00AE) return true;   // © ®
            if (cp == 0x2122 || cp == 0x2139) return true;
            if (cp >= 0x2194 && cp <= 0x2199) return true;
            if (cp >= 0x21A9 && cp <= 0x21AA) return true;
            if (cp >= 0x231A && cp <= 0x231B) return true;
            if (cp >= 0x23E9 && cp <= 0x23F3) return true;
            if (cp >= 0x23F8 && cp <= 0x23FA) return true;
            if (cp >= 0x25AA && cp <= 0x25AB) return true;
            if (cp == 0x25B6 || cp == 0x25C0) return true;
            if (cp >= 0x25FB && cp <= 0x25FE) return true;
            if (cp >= 0x2934 && cp <= 0x2935) return true;
            if (cp >= 0x2B05 && cp <= 0x2B07) return true;
            if (cp == 0x2B1B || cp == 0x2B1C || cp == 0x2B50 || cp == 0x2B55) return true;
            if (cp == 0x203C || cp == 0x2049) return true;
            // Enclosed / keycap base digits often combined with FE0F+20E3
            if (cp >= 0x0030 && cp <= 0x0039) return false; // handled via joiner path when followed by keycap
            if (cp == 0x0023 || cp == 0x002A) return false;
            return false;
        }

        private static bool IsEmojiJoinerOrModifier(int cp)
        {
            if (cp == 0x200D) return true;                 // ZWJ
            if (cp == 0xFE0F || cp == 0xFE0E) return true; // variation selectors
            if (cp == 0x20E3) return true;                 // combining enclosing keycap
            if (cp >= 0x1F3FB && cp <= 0x1F3FF) return true; // skin tones
            if (cp >= 0xE0020 && cp <= 0xE007F) return true; // tags
            if (cp >= 0x1F1E6 && cp <= 0x1F1FF) return true; // flag pair second half
            return IsEmojiCodepoint(cp);
        }

        private static bool TryParseFence(string text, ref int i, string fence, out string body)
        {
            body = null;
            if (!text.AsSpan(i).StartsWith(fence, StringComparison.Ordinal))
                return false;
            int startContent = i + fence.Length;
            int end = text.IndexOf(fence, startContent, StringComparison.Ordinal);
            if (end < 0)
                return false;
            body = text.Substring(startContent, end - startContent);
            i = end + fence.Length;
            return true;
        }

        private static bool TryParseDelimited(string text, ref int i, string delim, out string body)
        {
            body = null;
            if (!text.AsSpan(i).StartsWith(delim, StringComparison.Ordinal))
                return false;
            int startContent = i + delim.Length;
            int end = text.IndexOf(delim, startContent, StringComparison.Ordinal);
            if (end < 0 || end == startContent)
                return false;
            body = text.Substring(startContent, end - startContent);
            i = end + delim.Length;
            return true;
        }

        private static bool TryParseWrapped(string text, ref int i, char delim, out string body)
        {
            body = null;
            if (text[i] != delim)
                return false;
            // Require a non-empty body and a closing delim on the same conceptual span.
            int close = text.IndexOf(delim, i + 1);
            if (close <= i + 1)
                return false;
            // Avoid matching across newlines for single-char wrappers.
            if (text.IndexOf('\n', i + 1, close - (i + 1)) >= 0)
                return false;
            body = text.Substring(i + 1, close - (i + 1));
            i = close + 1;
            return true;
        }

        private static bool TryParseLink(string text, ref int i, out string linkText, out string url)
        {
            linkText = null;
            url = null;
            if (text[i] != '[')
                return false;
            int closeBracket = text.IndexOf(']', i + 1);
            if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
                return false;
            int closeParen = text.IndexOf(')', closeBracket + 2);
            if (closeParen < 0)
                return false;
            linkText = text.Substring(i + 1, closeBracket - (i + 1));
            url = text.Substring(closeBracket + 2, closeParen - (closeBracket + 2));
            i = closeParen + 1;
            return true;
        }
    }
}
