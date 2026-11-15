// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Converts the html content of a note into Markdown. This is a dependency free converter
    /// (the editor itself converts via javascript), so it can be used by the backup process on
    /// any platform and in unit tests.
    /// </summary>
    public static class SimpleHtmlToMarkdown
    {
        /// <summary>
        /// Converts an html fragment into Markdown.
        /// </summary>
        /// <param name="html">The html content to convert.</param>
        /// <returns>The Markdown representation.</returns>
        public static string Convert(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            string result = html;
            result = Regex.Replace(result, @"<script[^>]*>.*?</script>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            result = Regex.Replace(result, @"<style[^>]*>.*?</style>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Images: embedded base64 images are replaced with a placeholder, the Markdown bundle
            // should stay readable and small.
            result = Regex.Replace(result, @"<img[^>]*>", match =>
            {
                string tag = match.Value;
                Match srcMatch = Regex.Match(tag, "src=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                Match altMatch = Regex.Match(tag, "alt=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                string src = srcMatch.Success ? srcMatch.Groups[1].Value : string.Empty;
                string alt = altMatch.Success ? WebUtility.HtmlDecode(altMatch.Groups[1].Value) : string.Empty;
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrEmpty(alt) ? "[embedded image]" : "[image: " + alt + "]";
                return "![" + alt + "](" + src + ")";
            }, RegexOptions.IgnoreCase);

            result = Regex.Replace(result, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>",
                match => "[" + WebUtility.HtmlDecode(NoteHtmlUtils.StripTags(match.Groups[2].Value)).Trim() + "](" + match.Groups[1].Value + ")",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            result = Regex.Replace(result, @"<h1[^>]*>", "\n\n# ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<h2[^>]*>", "\n\n## ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<h3[^>]*>", "\n\n### ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<h4[^>]*>", "\n\n#### ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<h5[^>]*>", "\n\n##### ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<h6[^>]*>", "\n\n###### ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);

            result = Regex.Replace(result, @"<blockquote[^>]*>", "\n\n> ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</blockquote>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<pre[^>]*>", "\n\n```\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</pre>", "\n```\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<code[^>]*>", "`", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</code>", "`", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<(strong|b)[^>]*>", "**", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</(strong|b)>", "**", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<(em|i)[^>]*>", "*", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</(em|i)>", "*", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<u[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</u>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<s[^>]*>", "~~", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</s>", "~~", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<hr[^>]*/?>", "\n\n---\n\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<br[^>]*/?>", "  \n", RegexOptions.IgnoreCase);

            result = Regex.Replace(result, @"<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</li>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<ul[^>]*>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</ul>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<ol[^>]*>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</ol>", "\n", RegexOptions.IgnoreCase);

            // Tables: one row per line with pipe separated cells.
            result = Regex.Replace(result, @"<tr[^>]*>", "\n| ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</tr>", " |", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</t[dh]>", " | ", RegexOptions.IgnoreCase);

            result = Regex.Replace(result, @"</(p|div)>", "\n\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<[^>]+>", string.Empty);
            result = WebUtility.HtmlDecode(result);

            // Normalize whitespace and blank lines.
            result = result.Replace("\r\n", "\n").Replace('\r', '\n');
            result = Regex.Replace(result, @"[ \t]+\n", "\n");
            result = Regex.Replace(result, @"\n{3,}", "\n\n");
            result = Regex.Replace(result, @"\|\s*\n\s*\n", "|\n", RegexOptions.IgnoreCase);
            return result.Trim();
        }
    }
}
