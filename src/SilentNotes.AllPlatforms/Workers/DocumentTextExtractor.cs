// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Extracts read-only text/HTML from common document formats (text, Office Open XML,
    /// ODT/ODS, EPUB, FB2). PDF/DjVu are handled by the platform service.
    /// </summary>
    public static class DocumentTextExtractor
    {
        public const int MaxTextChars = 800_000;

        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".text", ".csv", ".tsv", ".log", ".md", ".markdown", ".json", ".xml",
            ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".cs", ".vb", ".vbs",
            ".py", ".java", ".kt", ".kts", ".go", ".rs", ".c", ".cpp", ".cc", ".h", ".hpp",
            ".sql", ".yaml", ".yml", ".ini", ".conf", ".cfg", ".sh", ".bash", ".bat", ".cmd",
            ".ps1", ".php", ".rb", ".swift", ".r", ".m", ".pl", ".lua", ".toml", ".properties",
            ".gradle", ".cmake", ".makefile", ".mk", ".diff", ".patch", ".srt", ".vtt",
            ".rtf", ".tex", ".bib", ".csvt",
        };

        private static readonly HashSet<string> OfficeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".odt", ".ods", ".pptx", ".pptm",
            // Legacy binary Office — listed so they appear in the browser; opened externally.
            ".doc", ".xls", ".ppt",
        };

        private static readonly HashSet<string> EbookExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".epub", ".fb2",
        };

        private static readonly HashSet<string> PdfExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
        };

        private static readonly HashSet<string> DjvuExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".djvu", ".djv",
        };

        /// <summary>Detects document kind from a path or MIME type.</summary>
        public static DocumentKind DetectKind(string uriOrPath, string mimeType = null)
        {
            if (!string.IsNullOrEmpty(mimeType))
            {
                string m = mimeType.ToLowerInvariant();
                if (m.StartsWith("text/", StringComparison.Ordinal) ||
                    m == "application/json" ||
                    m == "application/xml" ||
                    m == "application/javascript" ||
                    m == "application/x-javascript" ||
                    m == "application/sql")
                {
                    return DocumentKind.Text;
                }

                if (m == "application/pdf")
                    return DocumentKind.Pdf;

                if (m.Contains("epub", StringComparison.Ordinal) ||
                    m.Contains("fictionbook", StringComparison.Ordinal))
                {
                    return DocumentKind.Ebook;
                }

                if (m.Contains("djvu", StringComparison.Ordinal))
                    return DocumentKind.Djvu;

                if (m.Contains("officedocument", StringComparison.Ordinal) ||
                    m.Contains("msword", StringComparison.Ordinal) ||
                    m.Contains("ms-excel", StringComparison.Ordinal) ||
                    m.Contains("ms-powerpoint", StringComparison.Ordinal) ||
                    m.Contains("opendocument", StringComparison.Ordinal))
                {
                    return DocumentKind.Office;
                }
            }

            string ext = GetExtension(uriOrPath);
            if (string.IsNullOrEmpty(ext))
                return DocumentKind.Other;
            if (TextExtensions.Contains(ext))
                return DocumentKind.Text;
            if (OfficeExtensions.Contains(ext))
                return DocumentKind.Office;
            if (EbookExtensions.Contains(ext))
                return DocumentKind.Ebook;
            if (PdfExtensions.Contains(ext))
                return DocumentKind.Pdf;
            if (DjvuExtensions.Contains(ext))
                return DocumentKind.Djvu;
            return DocumentKind.Other;
        }

        /// <summary>Gets a value indicating whether the path is a readable document (not a folder).</summary>
        public static bool IsReadableDocument(string path, string mimeType = null)
        {
            DocumentKind kind = DetectKind(path, mimeType);
            return kind != DocumentKind.Folder && kind != DocumentKind.Other;
        }

        /// <summary>
        /// Extracts content from a seekable stream. Caller owns the stream.
        /// </summary>
        public static DocumentContentModel Extract(
            Stream stream,
            string pathOrName,
            string mimeType = null,
            string displayName = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            string title = displayName ?? Path.GetFileName(pathOrName) ?? "Document";
            DocumentKind kind = DetectKind(pathOrName, mimeType);
            string ext = GetExtension(pathOrName);

            var content = new DocumentContentModel
            {
                Path = pathOrName,
                Title = title,
                Kind = kind,
            };

            switch (kind)
            {
                case DocumentKind.Text:
                    FillText(content, stream, ext);
                    break;
                case DocumentKind.Office:
                    if (IsLegacyBinaryOffice(ext))
                    {
                        content.RenderMode = DocumentRenderMode.External;
                        content.InfoMessage = "Legacy Office format opens in an external viewer.";
                    }
                    else
                    {
                        FillOffice(content, stream, ext);
                    }
                    break;
                case DocumentKind.Ebook:
                    FillEbook(content, stream, ext);
                    break;
                case DocumentKind.Pdf:
                case DocumentKind.Djvu:
                    // Platform-specific.
                    content.RenderMode = kind == DocumentKind.Pdf
                        ? DocumentRenderMode.PdfPages
                        : DocumentRenderMode.External;
                    content.InfoMessage = kind == DocumentKind.Djvu
                        ? "DjVu files open in an external viewer."
                        : null;
                    break;
                default:
                    content.RenderMode = DocumentRenderMode.External;
                    content.InfoMessage = "This format is not supported for in-app reading.";
                    break;
            }

            return content;
        }

        private static void FillText(DocumentContentModel content, Stream stream, string ext)
        {
            content.RenderMode = DocumentRenderMode.Text;
            string text = ReadTextWithEncoding(stream);
            if (string.Equals(ext, ".rtf", StringComparison.OrdinalIgnoreCase))
                text = StripRtf(text);
            ApplyTextLimit(content, text);
        }

        private static bool IsLegacyBinaryOffice(string ext)
        {
            return string.Equals(ext, ".doc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".ppt", StringComparison.OrdinalIgnoreCase);
        }

        private static void FillOffice(DocumentContentModel content, Stream stream, string ext)
        {
            content.RenderMode = DocumentRenderMode.Text;
            string text;
            if (ext.StartsWith(".xls", StringComparison.OrdinalIgnoreCase))
                text = ExtractXlsx(stream);
            else if (ext.StartsWith(".ppt", StringComparison.OrdinalIgnoreCase))
                text = ExtractPptx(stream);
            else if (string.Equals(ext, ".ods", StringComparison.OrdinalIgnoreCase))
                text = ExtractOdf(stream, spreadsheet: true);
            else if (string.Equals(ext, ".odt", StringComparison.OrdinalIgnoreCase))
                text = ExtractOdf(stream, spreadsheet: false);
            else
                text = ExtractDocx(stream);

            ApplyTextLimit(content, text);
        }

        private static void FillEbook(DocumentContentModel content, Stream stream, string ext)
        {
            if (string.Equals(ext, ".fb2", StringComparison.OrdinalIgnoreCase))
            {
                content.RenderMode = DocumentRenderMode.Text;
                ApplyTextLimit(content, ExtractFb2(stream));
                return;
            }

            // EPUB: prefer HTML chapters concatenated into a simple readable HTML body.
            content.RenderMode = DocumentRenderMode.Html;
            string html = ExtractEpubHtml(stream);
            if (html.Length > MaxTextChars)
            {
                html = html.Substring(0, MaxTextChars);
                content.WasTruncated = true;
                content.InfoMessage = "Document truncated for display.";
            }
            content.Html = html;
        }

        private static void ApplyTextLimit(DocumentContentModel content, string text)
        {
            text ??= string.Empty;
            if (text.Length > MaxTextChars)
            {
                text = text.Substring(0, MaxTextChars);
                content.WasTruncated = true;
                content.InfoMessage = "Document truncated for display.";
            }
            content.Text = text;
        }

        private static string ReadTextWithEncoding(Stream stream)
        {
            if (stream.CanSeek)
                stream.Position = 0;

            // Detect BOM / try UTF-8 then Windows-1251 (common for older local files).
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                byte[] bytes = ms.ToArray();
                if (bytes.Length == 0)
                    return string.Empty;

                Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                try
                {
                    return utf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                }

                // Fallback without CodePages package: ISO-8859-1 preserves byte values.
                return Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
            }
        }

        private static string ExtractDocx(Stream stream)
        {
            using (ZipArchive zip = OpenZip(stream))
            {
                ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
                if (entry == null)
                    return string.Empty;

                using (Stream entryStream = entry.Open())
                {
                    XDocument doc = XDocument.Load(entryStream);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    var parts = new List<string>();
                    foreach (XElement p in doc.Descendants(w + "p"))
                    {
                        string line = string.Concat(p.Descendants(w + "t").Select(t => (string)t));
                        parts.Add(line);
                    }
                    return string.Join("\n", parts);
                }
            }
        }

        private static string ExtractXlsx(Stream stream)
        {
            using (ZipArchive zip = OpenZip(stream))
            {
                List<string> shared = ReadSharedStrings(zip);
                var sb = new StringBuilder();
                foreach (ZipArchiveEntry sheet in zip.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine("--- " + Path.GetFileNameWithoutExtension(sheet.Name) + " ---");
                    using (Stream entryStream = sheet.Open())
                    {
                        XDocument doc = XDocument.Load(entryStream);
                        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                        string currentRow = null;
                        var rowCells = new List<string>();
                        foreach (XElement c in doc.Descendants(main + "c"))
                        {
                            string r = (string)c.Attribute("r");
                            string row = RowOf(r);
                            if (currentRow != null && row != currentRow)
                            {
                                sb.AppendLine(string.Join("\t", rowCells));
                                rowCells.Clear();
                            }
                            currentRow = row;
                            rowCells.Add(CellValue(c, shared, main));
                        }
                        if (rowCells.Count > 0)
                            sb.AppendLine(string.Join("\t", rowCells));
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }

        private static string ExtractPptx(Stream stream)
        {
            using (ZipArchive zip = OpenZip(stream))
            {
                var sb = new StringBuilder();
                XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
                foreach (ZipArchiveEntry slide in zip.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine("--- " + Path.GetFileNameWithoutExtension(slide.Name) + " ---");
                    using (Stream entryStream = slide.Open())
                    {
                        XDocument doc = XDocument.Load(entryStream);
                        foreach (XElement t in doc.Descendants(a + "t"))
                        {
                            string value = (string)t;
                            if (!string.IsNullOrWhiteSpace(value))
                                sb.AppendLine(value);
                        }
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }

        private static string ExtractOdf(Stream stream, bool spreadsheet)
        {
            using (ZipArchive zip = OpenZip(stream))
            {
                ZipArchiveEntry entry = zip.GetEntry("content.xml");
                if (entry == null)
                    return string.Empty;

                using (Stream entryStream = entry.Open())
                {
                    XDocument doc = XDocument.Load(entryStream);
                    XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
                    XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
                    if (spreadsheet)
                    {
                        var sb = new StringBuilder();
                        foreach (XElement row in doc.Descendants(table + "table-row"))
                        {
                            var cells = row.Elements(table + "table-cell")
                                .Select(c => string.Concat(c.Descendants(text + "p").Select(p => p.Value)))
                                .ToList();
                            sb.AppendLine(string.Join("\t", cells));
                        }
                        return sb.ToString();
                    }

                    return string.Join("\n", doc.Descendants(text + "p").Select(p => p.Value));
                }
            }
        }

        private static string ExtractFb2(Stream stream)
        {
            if (stream.CanSeek)
                stream.Position = 0;

            XDocument doc = XDocument.Load(stream);
            var parts = new List<string>();

            string bookTitle = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "book-title")?.Value;
            if (!string.IsNullOrWhiteSpace(bookTitle))
                parts.Add(bookTitle.Trim());

            foreach (XElement el in doc.Descendants())
            {
                string local = el.Name.LocalName;
                if (local == "p" || local == "v" || local == "subtitle" || local == "text-author")
                {
                    string value = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        parts.Add(value);
                }
                else if (local == "title" && el.Parent != null && el.Parent.Name.LocalName == "section")
                {
                    string value = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        parts.Add(value);
                }
            }

            return string.Join("\n\n", parts);
        }

        private static string ExtractEpubHtml(Stream stream)
        {
            using (ZipArchive zip = OpenZip(stream))
            {
                string opfPath = FindOpfPath(zip);
                if (string.IsNullOrEmpty(opfPath))
                    return "<p>Could not find EPUB package.</p>";

                ZipArchiveEntry opfEntry = zip.GetEntry(opfPath);
                if (opfEntry == null)
                    return "<p>Could not open EPUB package.</p>";

                string opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
                if (!string.IsNullOrEmpty(opfDir) && !opfDir.EndsWith("/", StringComparison.Ordinal))
                    opfDir += "/";

                List<string> spineHrefs;
                using (Stream opfStream = opfEntry.Open())
                {
                    XDocument opf = XDocument.Load(opfStream);
                    XNamespace pkg = "http://www.idpf.org/2007/opf";
                    var idToHref = opf.Descendants(pkg + "item")
                        .Where(i => i.Attribute("id") != null && i.Attribute("href") != null)
                        .ToDictionary(
                            i => (string)i.Attribute("id"),
                            i => (string)i.Attribute("href"),
                            StringComparer.OrdinalIgnoreCase);

                    spineHrefs = opf.Descendants(pkg + "itemref")
                        .Select(i => (string)i.Attribute("idref"))
                        .Where(id => !string.IsNullOrEmpty(id) && idToHref.ContainsKey(id))
                        .Select(id => idToHref[id])
                        .ToList();
                }

                var sb = new StringBuilder();
                sb.Append("<div class=\"reader-epub\">");
                foreach (string href in spineHrefs)
                {
                    string entryPath = CombineZipPath(opfDir, href);
                    ZipArchiveEntry chapter = zip.GetEntry(entryPath)
                        ?? zip.Entries.FirstOrDefault(e =>
                            string.Equals(e.FullName, entryPath, StringComparison.OrdinalIgnoreCase));
                    if (chapter == null)
                        continue;

                    using (Stream chapterStream = chapter.Open())
                    using (var reader = new StreamReader(chapterStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    {
                        string xhtml = reader.ReadToEnd();
                        string body = ExtractHtmlBody(xhtml);
                        if (!string.IsNullOrWhiteSpace(body))
                            sb.Append(body).Append("<hr/>");
                    }

                    if (sb.Length > MaxTextChars)
                        break;
                }
                sb.Append("</div>");
                return sb.ToString();
            }
        }

        private static string FindOpfPath(ZipArchive zip)
        {
            ZipArchiveEntry container = zip.GetEntry("META-INF/container.xml");
            if (container != null)
            {
                using (Stream s = container.Open())
                {
                    XDocument doc = XDocument.Load(s);
                    XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
                    string fullPath = doc.Descendants(ns + "rootfile")
                        .Select(r => (string)r.Attribute("full-path"))
                        .FirstOrDefault(p => !string.IsNullOrEmpty(p));
                    if (!string.IsNullOrEmpty(fullPath))
                        return fullPath.Replace('\\', '/');
                }
            }

            return zip.Entries
                .Select(e => e.FullName)
                .FirstOrDefault(n => n.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractHtmlBody(string xhtml)
        {
            if (string.IsNullOrEmpty(xhtml))
                return string.Empty;

            Match body = Regex.Match(
                xhtml,
                @"<body[^>]*>(.*)</body>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string fragment = body.Success ? body.Groups[1].Value : xhtml;

            // Drop scripts/styles; keep simple markup for MudBlazor MarkupString.
            fragment = Regex.Replace(fragment, @"<script[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            fragment = Regex.Replace(fragment, @"<style[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            fragment = Regex.Replace(fragment, @"\son\w+\s*=\s*(""[^""]*""|'[^']*')", string.Empty, RegexOptions.IgnoreCase);
            return fragment.Trim();
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return result;

            using (Stream s = entry.Open())
            {
                XDocument doc = XDocument.Load(s);
                XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (XElement si in doc.Descendants(main + "si"))
                {
                    string value = string.Concat(si.Descendants(main + "t").Select(t => (string)t));
                    result.Add(value ?? string.Empty);
                }
            }
            return result;
        }

        private static string CellValue(XElement cell, List<string> shared, XNamespace main)
        {
            string type = (string)cell.Attribute("t");
            XElement v = cell.Element(main + "v");
            string raw = v?.Value ?? string.Empty;
            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(raw, out int index) &&
                index >= 0 && index < shared.Count)
            {
                return shared[index];
            }

            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(cell.Descendants(main + "t").Select(t => (string)t));
            }

            return raw;
        }

        private static string RowOf(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef))
                return "?";
            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
                i++;
            return i < cellRef.Length ? cellRef.Substring(i) : "?";
        }

        private static ZipArchive OpenZip(Stream stream)
        {
            if (stream.CanSeek)
                stream.Position = 0;
            else
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                stream = ms;
            }
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }

        private static string CombineZipPath(string dir, string href)
        {
            string combined = string.IsNullOrEmpty(dir) ? href : dir + href;
            combined = combined.Replace('\\', '/');
            // Strip query/fragment and normalize ./ ../
            int q = combined.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
                combined = combined.Substring(0, q);

            var parts = new List<string>();
            foreach (string part in combined.Split('/'))
            {
                if (string.IsNullOrEmpty(part) || part == ".")
                    continue;
                if (part == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static string GetExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string clean = path;
            int q = clean.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
                clean = clean.Substring(0, q);

            // content URIs often end with encoded names
            try
            {
                return Path.GetExtension(clean);
            }
            catch (Exception)
            {
                int dot = clean.LastIndexOf('.');
                return dot >= 0 ? clean.Substring(dot) : string.Empty;
            }
        }

        private static string StripRtf(string rtf)
        {
            if (string.IsNullOrEmpty(rtf) || !rtf.TrimStart().StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase))
                return rtf;

            // Minimal RTF → text: drop groups/control words, keep printable runs.
            var sb = new StringBuilder(rtf.Length);
            bool slash = false;
            for (int i = 0; i < rtf.Length; i++)
            {
                char c = rtf[i];
                if (slash)
                {
                    slash = false;
                    if (c == '\\' || c == '{' || c == '}')
                    {
                        sb.Append(c);
                        continue;
                    }
                    // skip control word
                    while (i < rtf.Length && (char.IsLetter(rtf[i]) || rtf[i] == '-'))
                        i++;
                    if (i < rtf.Length && (rtf[i] == ' ' || char.IsDigit(rtf[i]) || rtf[i] == '-'))
                    {
                        // skip optional numeric param / space delimiter
                        if (rtf[i] == '-' || char.IsDigit(rtf[i]))
                        {
                            if (rtf[i] == '-')
                                i++;
                            while (i < rtf.Length && char.IsDigit(rtf[i]))
                                i++;
                        }
                        if (i < rtf.Length && rtf[i] == ' ')
                            i++;
                    }
                    i--;
                    continue;
                }

                if (c == '\\')
                {
                    slash = true;
                    continue;
                }
                if (c == '{' || c == '}')
                    continue;
                if (c == '\r')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
