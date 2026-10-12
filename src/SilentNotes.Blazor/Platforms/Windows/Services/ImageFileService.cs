// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IImageFileService"/> interface for the Windows platform.
    /// </summary>
    internal class ImageFileService : IImageFileService
    {
        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public Task<bool> OpenImageAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return Task.FromResult(false);

                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                return Task.FromResult(true);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc/>
        public Task<string> SaveImageToPicturesAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return Task.FromResult<string>(null);

                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "clips");
                Directory.CreateDirectory(directory);
                string targetPath = Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".jpg");

                using (var image = new System.Drawing.Bitmap(filePath))
                {
                    image.Save(targetPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                return Task.FromResult(targetPath);
            }
            catch (Exception)
            {
                return Task.FromResult<string>(null);
            }
        }
    }
}
