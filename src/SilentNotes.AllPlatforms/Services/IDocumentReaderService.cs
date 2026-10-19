// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Browses device storage and opens documents read-only for the Reader tab
    /// (text, Office, PDF, EPUB/FB2; DjVu via external viewer when needed).
    /// </summary>
    public interface IDocumentReaderService
    {
        /// <summary>Gets a value indicating whether document reading is supported on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether all-files / broad storage access is granted.</summary>
        bool HasAllFilesPermission { get; }

        /// <summary>Requests all-files storage access (opens system settings on Android 11+).</summary>
        Task<bool> RequestAllFilesPermissionAsync();

        /// <summary>Gets the absolute path of the directory currently being browsed.</summary>
        string CurrentDirectory { get; }

        /// <summary>Gets a short display label for <see cref="CurrentDirectory"/>.</summary>
        string CurrentDirectoryDisplay { get; }

        /// <summary>Gets a value indicating whether navigating one level up is possible.</summary>
        bool CanNavigateUp { get; }

        /// <summary>Lists folders and readable document files in the current directory.</summary>
        IReadOnlyList<DocumentItemModel> ListCurrentDirectory();

        /// <summary>Enters a subdirectory.</summary>
        void EnterDirectory(string path);

        /// <summary>Navigates one level up, when possible.</summary>
        void NavigateUp();

        /// <summary>Jumps to a well-known root (internal storage, Documents, Download, …).</summary>
        void GoToRoot(string rootKey);

        /// <summary>Gets available storage roots for quick navigation.</summary>
        IReadOnlyList<(string Key, string Label, string Path)> GetStorageRoots();

        /// <summary>Raised when open-document state changes.</summary>
        event Action StateChanged;

        /// <summary>Gets the document currently open for reading, or null.</summary>
        DocumentContentModel ActiveDocument { get; }

        /// <summary>Opens a browsed file for reading.</summary>
        Task OpenAsync(DocumentItemModel item);

        /// <summary>Opens a document from a content URI / path (e.g. system VIEW intent).</summary>
        Task OpenUriAsync(string uriOrPath, string mimeType = null, string displayName = null);

        /// <summary>Closes the currently open document and returns to the browser.</summary>
        void CloseDocument();

        /// <summary>For PDF documents, moves to another page and re-renders it.</summary>
        Task GoToPdfPageAsync(int pageIndex);

        /// <summary>Lets the user pick a document via the system picker and opens it.</summary>
        Task PickAndOpenAsync();

        /// <summary>Opens the active (or given) path in an external app.</summary>
        Task OpenExternallyAsync(string uriOrPath = null, string mimeType = null);
    }
}
