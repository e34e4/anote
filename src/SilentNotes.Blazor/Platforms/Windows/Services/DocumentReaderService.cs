// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub implementation of <see cref="IDocumentReaderService"/> for the Windows platform.
    /// </summary>
    internal class DocumentReaderService : IDocumentReaderService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool HasAllFilesPermission => false;

        /// <inheritdoc/>
        public Task<bool> RequestAllFilesPermissionAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public string CurrentDirectory => string.Empty;

        /// <inheritdoc/>
        public string CurrentDirectoryDisplay => string.Empty;

        /// <inheritdoc/>
        public bool CanNavigateUp => false;

        /// <inheritdoc/>
        public event Action StateChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public DocumentContentModel ActiveDocument => null;

        /// <inheritdoc/>
        public IReadOnlyList<DocumentItemModel> ListCurrentDirectory() => Array.Empty<DocumentItemModel>();

        /// <inheritdoc/>
        public void EnterDirectory(string path)
        {
        }

        /// <inheritdoc/>
        public void NavigateUp()
        {
        }

        /// <inheritdoc/>
        public void GoToRoot(string rootKey)
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<(string Key, string Label, string Path)> GetStorageRoots() =>
            Array.Empty<(string, string, string)>();

        /// <inheritdoc/>
        public Task OpenAsync(DocumentItemModel item) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task OpenUriAsync(string uriOrPath, string mimeType = null, string displayName = null) =>
            Task.CompletedTask;

        /// <inheritdoc/>
        public void CloseDocument()
        {
        }

        /// <inheritdoc/>
        public Task GoToPdfPageAsync(int pageIndex) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task PickAndOpenAsync() => Task.CompletedTask;

        /// <inheritdoc/>
        public Task OpenExternallyAsync(string uriOrPath = null, string mimeType = null) =>
            Task.CompletedTask;
    }
}
