// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Models;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class WebdavQuickSaveService : IWebdavQuickSaveService
    {
        private const string RemoteFolderName = "anote";

        private readonly IWebdavFileService _webdavService;
        private readonly ISettingsService _settingsService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebdavQuickSaveService"/> class.
        /// </summary>
        public WebdavQuickSaveService(IWebdavFileService webdavService, ISettingsService settingsService)
        {
            _webdavService = webdavService;
            _settingsService = settingsService;
        }

        /// <inheritdoc/>
        public string LastError { get; private set; }

        /// <inheritdoc/>
        public string LastFileName { get; private set; }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                ResolveCredentials(out string url, out _, out _);
                return !string.IsNullOrWhiteSpace(url);
            }
        }

        /// <inheritdoc/>
        public Task<bool> SaveTextAsync(string tabPrefix, string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text ?? string.Empty);
            return SaveBytesAsync(tabPrefix, content, "txt");
        }

        /// <inheritdoc/>
        public async Task<bool> SaveLocalFileAsync(string tabPrefix, string localFilePath)
        {
            LastError = null;
            LastFileName = null;

            if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
            {
                LastError = "File not found.";
                return false;
            }

            string extension = Path.GetExtension(localFilePath)?.TrimStart('.') ?? "bin";
            if (string.IsNullOrWhiteSpace(extension))
                extension = "bin";

            byte[] content = await File.ReadAllBytesAsync(localFilePath);
            return await SaveBytesAsync(tabPrefix, content, extension);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveBytesAsync(string tabPrefix, byte[] content, string extension)
        {
            LastError = null;
            LastFileName = null;

            if (!ResolveCredentials(out string url, out string username, out string password))
            {
                LastError = "WebDAV is not configured.";
                return false;
            }

            string safePrefix = SanitizePrefix(tabPrefix);
            string safeExt = string.IsNullOrWhiteSpace(extension) ? "bin" : extension.Trim().TrimStart('.');
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1:yyyyMMdd_HHmmss}.{2}",
                safePrefix,
                DateTime.Now,
                safeExt);

            // Always upload into the dedicated "anote" folder at the WebDAV URL root.
            string remoteFolder = RemoteFolderName;

            try
            {
                await EnsureFolderExistsAsync(url, remoteFolder, username, password);
                await _webdavService.UploadFileAsync(url, remoteFolder, fileName, content ?? Array.Empty<byte>(), username, password);
                LastFileName = fileName;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private async Task EnsureFolderExistsAsync(string url, string remoteFolder, string username, string password)
        {
            // Create each path segment that may be missing (e.g. "anote").
            string[] parts = (remoteFolder ?? string.Empty).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string built = string.Empty;
            foreach (string part in parts)
            {
                string parent = built;
                try
                {
                    await _webdavService.CreateDirectoryAsync(url, parent, part, username, password);
                }
                catch (Exception)
                {
                    // Already exists or not required — upload will fail clearly if missing.
                }
                built = CombinePaths(built, part);
            }
        }

        private bool ResolveCredentials(out string url, out string username, out string password)
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (!string.IsNullOrWhiteSpace(settings.FileWebdavUrl))
            {
                url = settings.FileWebdavUrl.Trim();
                username = settings.FileWebdavUsername ?? string.Empty;
                password = settings.FileWebdavPassword ?? string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(settings.BookySyncUrl))
            {
                url = settings.BookySyncUrl.Trim();
                username = settings.BookySyncUsername ?? string.Empty;
                password = settings.BookySyncPassword ?? string.Empty;
                return true;
            }

            if (settings.Credentials != null &&
                string.Equals(settings.Credentials.CloudStorageId, CloudStorageClientFactory.CloudStorageIdWebdav, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(settings.Credentials.Url))
            {
                url = settings.Credentials.Url.Trim();
                username = settings.Credentials.Username ?? string.Empty;
                password = settings.Credentials.UnprotectedPassword ?? string.Empty;
                return true;
            }

            url = string.Empty;
            username = string.Empty;
            password = string.Empty;
            return false;
        }

        private static string SanitizePrefix(string tabPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(tabPrefix) ? "Item" : tabPrefix.Trim();
            var builder = new StringBuilder(prefix.Length);
            foreach (char c in prefix)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    builder.Append(c);
                else if (char.IsWhiteSpace(c))
                    builder.Append('_');
            }

            string result = builder.ToString();
            return string.IsNullOrEmpty(result) ? "Item" : result;
        }

        private static string CombinePaths(string first, string second)
        {
            string a = (first ?? string.Empty).Trim().Trim('/');
            string b = (second ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrEmpty(a))
                return b;
            if (string.IsNullOrEmpty(b))
                return a;
            return a + "/" + b;
        }
    }
}
