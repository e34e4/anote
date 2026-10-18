// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Service for browsing, uploading, downloading, and deleting files over WebDAV in the File tab.
    /// </summary>
    public interface IWebdavFileService
    {
        /// <summary>
        /// Lists all files and subfolders in the specified remote directory on the WebDAV server.
        /// </summary>
        Task<IReadOnlyList<WebdavFileItemModel>> ListDirectoryAsync(string baseUrl, string remoteFolder, string username, string password);

        /// <summary>
        /// Uploads a file to the specified remote directory on the WebDAV server.
        /// </summary>
        Task UploadFileAsync(string baseUrl, string remoteFolder, string fileName, byte[] content, string username, string password);

        /// <summary>
        /// Downloads a file from the specified remote directory on the WebDAV server.
        /// </summary>
        Task<byte[]> DownloadFileAsync(string baseUrl, string remoteFolder, string fileName, string username, string password);

        /// <summary>
        /// Deletes a file or folder in the specified remote directory on the WebDAV server.
        /// </summary>
        Task DeleteItemAsync(string baseUrl, string remoteFolder, string itemName, bool isDirectory, string username, string password);

        /// <summary>
        /// Creates a new subfolder (MKCOL) in the specified remote directory on the WebDAV server.
        /// </summary>
        Task CreateDirectoryAsync(string baseUrl, string remoteFolder, string newFolderName, string username, string password);

        /// <summary>
        /// Saves downloaded bytes to the device's Downloads folder.
        /// </summary>
        /// <returns>Display path where the file was saved, or null if failed.</returns>
        Task<string> SaveDownloadedFileAsync(string fileName, byte[] content);

        /// <summary>
        /// Shares or opens a downloaded file using the platform's share/open sheet.
        /// </summary>
        Task<bool> ShareDownloadedFileAsync(string fileName, byte[] content);
    }
}
