// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Builds HTML previews for CSV, HTML, and Markdown content shown from the Reader editor.
    /// </summary>
    public static class ReaderPreviewRenderer
    {
        /// <summary>Preview kinds the editor can render.</summary>
        public enum PreviewKind
        {
            None,
            Csv,
            Html,
            Markdown,
        }

        /// <summary>Detects whether a file name supports Preview.</summary>
        public static PreviewKind Detect(string pathOrName)
        {
            string ext = Path.GetExtension(pathOrName ?? string.Empty);
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase))
            {
                return PreviewKind.Csv;
            }

            if (string.Equals(ext, ".html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                return PreviewKind.Html;
            }

            if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase))
            {
                return PreviewKind.Markdown;
            }

            return PreviewKind.None;
        }

        /// <summary>Renders preview HTML for the given kind and text.</summary>
        public static string Render(PreviewKind kind, string text, string pathOrName = null)
        {
            text ??= string.Empty;
            return kind switch
            {
                PreviewKind.Csv => RenderCsv(text, pathOrName),
                PreviewKind.Html => text,
                PreviewKind.Markdown => RenderMarkdown(text),
                _ => WebUtility.HtmlEncode(text),
            };
        }

        private static string RenderCsv(string text, string pathOrName)
        {
            char sep = pathOrName != null && pathOrName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
                ? '\t'
                : DetectCsvSeparator(text);

            var rows = ParseCsv(text, sep);
            var sb = new StringBuilder();
            sb.Append("<table class=\"rte-preview-table\">");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("<tr>");
                string tag = r == 0 ? "th" : "td";
                foreach (string cell in rows[r])
                {
                    sb.Append('<').Append(tag).Append('>')
                        .Append(WebUtility.HtmlEncode(cell))
                        .Append("</").Append(tag).Append('>');
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        private static char DetectCsvSeparator(string text)
        {
            int comma = 0, semi = 0, tab = 0;
            int limit = Math.Min(text.Length, 4000);
            for (int i = 0; i < limit; i++)
            {
                char c = text[i];
                if (c == ',') comma++;
                else if (c == ';') semi++;
                else if (c == '\t') tab++;
            }
            if (tab >= comma && tab >= semi && tab > 0)
                return '\t';
            if (semi > comma)
                return ';';
            return ',';
        }

        private static List<List<string>> ParseCsv(string text, char sep)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == sep)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c != '\r')
                {
                    cell.Append(c);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static string RenderMarkdown(string markdown)
        {
            List<AbroAiMarkdown.Block> blocks = AbroAiMarkdown.Parse(markdown);
            var sb = new StringBuilder();
            bool inList = false;

            foreach (AbroAiMarkdown.Block block in blocks)
            {
                switch (block.Kind)
                {
                    case AbroAiMarkdown.BlockKind.Heading:
                        if (inList) { sb.Append("</ul>"); inList = false; }
                        int level = Math.Clamp(block.Level, 1, 6);
                        sb.Append("<h").Append(level).Append('>')
                            .Append(RenderInlines(block.Text))
                            .Append("</h").Append(level).Append('>');
                        break;
                    case AbroAiMarkdown.BlockKind.Code:
                        if (inList) { sb.Append("</ul>"); inList = false; }
                        sb.Append("<pre class=\"rte-preview-code\"><code>")
                            .Append(WebUtility.HtmlEncode(block.Text ?? string.Empty))
                            .Append("</code></pre>");
                        break;
                    case AbroAiMarkdown.BlockKind.Quote:
                        if (inList) { sb.Append("</ul>"); inList = false; }
                        sb.Append("<blockquote>").Append(RenderInlines(block.Text)).Append("</blockquote>");
                        break;
                    case AbroAiMarkdown.BlockKind.ListItem:
                        if (!inList)
                        {
                            sb.Append("<ul>");
                            inList = true;
                        }
                        sb.Append("<li>").Append(RenderInlines(block.Text)).Append("</li>");
                        break;
                    default:
                        if (inList) { sb.Append("</ul>"); inList = false; }
                        sb.Append("<p>").Append(RenderInlines(block.Text)).Append("</p>");
                        break;
                }
            }

            if (inList)
                sb.Append("</ul>");

            return sb.ToString();
        }

        private static string RenderInlines(string text)
        {
            List<AbroAiMarkdown.Inline> inlines = AbroAiMarkdown.ParseInlines(text ?? string.Empty);
            var sb = new StringBuilder();
            foreach (AbroAiMarkdown.Inline span in inlines)
            {
                string encoded = WebUtility.HtmlEncode(span.Text ?? string.Empty);
                switch (span.Kind)
                {
                    case AbroAiMarkdown.InlineKind.Bold:
                        sb.Append("<strong>").Append(encoded).Append("</strong>");
                        break;
                    case AbroAiMarkdown.InlineKind.Italic:
                        sb.Append("<em>").Append(encoded).Append("</em>");
                        break;
                    case AbroAiMarkdown.InlineKind.Code:
                        sb.Append("<code>").Append(encoded).Append("</code>");
                        break;
                    default:
                        sb.Append(encoded);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
