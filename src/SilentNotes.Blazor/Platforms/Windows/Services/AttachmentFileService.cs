// Copyright © 2026 aNote.
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
    /// Windows implementation of <see cref="IAttachmentFileService"/>.
    /// </summary>
    internal class AttachmentFileService : IAttachmentFileService
    {
        /// <inheritdoc/>
        public Task<bool> OpenAttachmentAsync(byte[] content, string fileName, string mimeType)
        {
            string path = WriteTempFile(content, fileName);
            if (path == null)
                return Task.FromResult(false);
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return Task.FromResult(true);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc/>
        public Task<bool> ShareAttachmentAsync(byte[] content, string fileName, string mimeType)
        {
            string path = WriteTempFile(content, fileName);
            if (path == null)
                return Task.FromResult(false);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                return Task.FromResult(true);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
        }

        private static string WriteTempFile(byte[] content, string fileName)
        {
            if (content == null || content.Length == 0)
                return null;
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "anote-attachments");
                Directory.CreateDirectory(directory);
                string name = Path.GetFileName(fileName ?? "attachment");
                if (string.IsNullOrWhiteSpace(name))
                    name = "attachment";
                string path = Path.Combine(directory, name);
                File.WriteAllBytes(path, content);
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
