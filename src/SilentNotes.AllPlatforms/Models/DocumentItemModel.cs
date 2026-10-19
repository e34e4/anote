// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Kind of document entry shown in the Reader tab.
    /// </summary>
    public enum DocumentKind
    {
        Folder,
        Text,
        Office,
        Pdf,
        Ebook,
        Djvu,
        Other,
    }

    /// <summary>
    /// How opened document content should be rendered in the Reader tab.
    /// </summary>
    public enum DocumentRenderMode
    {
        /// <summary>Plain or lightly formatted text in a scrollable view.</summary>
        Text,

        /// <summary>HTML fragment (e.g. EPUB chapter) rendered read-only.</summary>
        Html,

        /// <summary>One or more PDF page images (base64 PNG data URLs).</summary>
        PdfPages,

        /// <summary>Opened in an external viewer (DjVu / unsupported binary formats).</summary>
        External,
    }

    /// <summary>
    /// A file or folder entry discovered while browsing device storage in the Reader tab.
    /// </summary>
    public class DocumentItemModel
    {
        /// <summary>Gets or sets the absolute filesystem path, or a content URI string.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the document kind.</summary>
        public DocumentKind Kind { get; set; }

        /// <summary>Gets or sets the file size in bytes (0 for folders).</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the last modified time in local time.</summary>
        public DateTime ModifiedLocal { get; set; }

        /// <summary>Gets or sets the MIME type when known (e.g. from a VIEW intent).</summary>
        public string MimeType { get; set; }
    }

    /// <summary>
    /// Content currently open in the Reader tab (read-only).
    /// </summary>
    public class DocumentContentModel
    {
        public string Path { get; set; }

        public string Title { get; set; }

        public DocumentKind Kind { get; set; }

        public DocumentRenderMode RenderMode { get; set; }

        /// <summary>Plain text body when <see cref="RenderMode"/> is <see cref="DocumentRenderMode.Text"/>.</summary>
        public string Text { get; set; }

        /// <summary>HTML body when <see cref="RenderMode"/> is <see cref="DocumentRenderMode.Html"/>.</summary>
        public string Html { get; set; }

        /// <summary>PDF page images as data URLs (data:image/png;base64,...) for the current window.</summary>
        public System.Collections.Generic.List<string> PdfPageDataUrls { get; set; } =
            new System.Collections.Generic.List<string>();

        public int PdfPageCount { get; set; }

        public int PdfPageIndex { get; set; }

        /// <summary>Optional note shown above the content (truncation, external open, etc.).</summary>
        public string InfoMessage { get; set; }

        public bool WasTruncated { get; set; }
    }
}
