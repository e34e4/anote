// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Windows implementation of <see cref="IWebdavFileService"/>.
    /// </summary>
    internal class WebdavFileService : WebdavFileServiceBase
    {
        /// <inheritdoc/>
        public override Task<string> SaveDownloadedFileAsync(string fileName, byte[] content)
        {
            try
            {
                string safeName = Path.GetFileName(fileName);
                string downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                string path = Path.Combine(downloads, safeName);
                File.WriteAllBytes(path, content);
                return Task.FromResult(path);
            }
            catch (Exception)
            {
                return Task.FromResult<string>(null);
            }
        }

        /// <inheritdoc/>
        public override Task<bool> ShareDownloadedFileAsync(string fileName, byte[] content)
        {
            return Task.FromResult(false);
        }
    }
}
