// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Uploads a single item into the WebDAV <c>anote</c> folder using the File-tab credentials.
    /// </summary>
    public interface IWebdavQuickSaveService
    {
        /// <summary>
        /// Gets a value indicating whether WebDAV credentials are available.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Gets the last error message from a failed save, or null.
        /// </summary>
        string LastError { get; }

        /// <summary>
        /// Gets the file name of the last successful upload, or null.
        /// </summary>
        string LastFileName { get; }

        /// <summary>
        /// Uploads UTF-8 text as a <c>.txt</c> file named <c>{tabPrefix}_{yyyyMMdd_HHmmss}.txt</c>.
        /// </summary>
        Task<bool> SaveTextAsync(string tabPrefix, string text);

        /// <summary>
        /// Uploads raw bytes with the given extension (without a leading dot).
        /// </summary>
        Task<bool> SaveBytesAsync(string tabPrefix, byte[] content, string extension);

        /// <summary>
        /// Uploads an existing local file, keeping its extension when possible.
        /// </summary>
        Task<bool> SaveLocalFileAsync(string tabPrefix, string localFilePath);
    }
}
