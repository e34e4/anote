// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroBookmarkSyncService : IAbroBookmarkSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly IAbroBookmarkStoreService _storeService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBookmarkSyncService"/> class.
        /// </summary>
        public AbroBookmarkSyncService(
            ISettingsService settingsService,
            IAbroBookmarkStoreService storeService,
            ICloudStorageClientFactory cloudStorageClientFactory)
        {
            _settingsService = settingsService;
            _storeService = storeService;
            _cloudStorageClientFactory = cloudStorageClientFactory;
        }

        /// <inheritdoc/>
        public string LastError { get; private set; }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                return settings.AbroBookmarksSyncEnabled
                    && !string.IsNullOrWhiteSpace(settings.BookySyncUrl)
                    && !string.IsNullOrWhiteSpace(settings.AbroBookmarksSyncRemotePath);
            }
        }

        /// <inheritdoc/>
        public Task<AbroBrowserSyncResult> SyncAsync()
        {
            return SyncInternalAsync(overwriteRemote: false);
        }

        /// <inheritdoc/>
        public Task<AbroBrowserSyncResult> OverwriteRemoteAsync()
        {
            return SyncInternalAsync(overwriteRemote: true);
        }

        private async Task<AbroBrowserSyncResult> SyncInternalAsync(bool overwriteRemote)
        {
            CloudStorageCredentials credentials = null;
            LastError = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.AbroBookmarksSyncEnabled)
                    return AbroBrowserSyncResult.Disabled;

                if (string.IsNullOrWhiteSpace(settings.BookySyncUrl))
                    return AbroBrowserSyncResult.NotConfigured;

                (string folderUrl, string fileName) = SplitRemotePath(settings.BookySyncUrl, settings.AbroBookmarksSyncRemotePath);
                credentials = BuildCredentials(folderUrl, settings);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                if (!overwriteRemote)
                {
                    AbroBookmarkStoreModel remote = new AbroBookmarkStoreModel();
                    if (await client.ExistsFileAsync(fileName, credentials).ConfigureAwait(false))
                    {
                        byte[] bytes = await client.DownloadFileAsync(fileName, credentials).ConfigureAwait(false);
                        remote = AbroBookmarkStoreFormat.Parse(Encoding.UTF8.GetString(bytes));
                        if (remote == null)
                        {
                            LastError = "The bookmarks file on the server has an unexpected format.";
                            return AbroBrowserSyncResult.Failed;
                        }
                    }

                    _storeService.MergeFromRemote(remote);
                }

                AbroBookmarkStoreModel local = _storeService.LoadOrDefault();
                string json = AbroBookmarkStoreFormat.Serialize(local);
                await client.UploadFileAsync(fileName, Encoding.UTF8.GetBytes(json), credentials).ConfigureAwait(false);
                return AbroBrowserSyncResult.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return AbroBrowserSyncResult.Failed;
            }
            finally
            {
                credentials?.Dispose();
            }
        }

        private static (string FolderUrl, string FileName) SplitRemotePath(string baseUrl, string remotePath)
        {
            string basePart = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = (remotePath ?? string.Empty).Trim().TrimStart('/');
            if (path.Length == 0)
                path = "abro/bookmarks.json";

            int lastSlash = path.LastIndexOf('/');
            string folder = (lastSlash >= 0) ? path.Substring(0, lastSlash) : string.Empty;
            string fileName = (lastSlash >= 0) ? path.Substring(lastSlash + 1) : path;
            string folderUrl = (folder.Length > 0) ? basePart + "/" + folder : basePart;
            return (folderUrl, fileName);
        }

        private static CloudStorageCredentials BuildCredentials(string folderUrl, SettingsModel settings)
        {
            var credentials = new CloudStorageCredentials
            {
                CloudStorageId = CloudStorageClientFactory.CloudStorageIdWebdav,
                Url = folderUrl,
                Username = settings.BookySyncUsername,
            };
            credentials.UnprotectedPassword = settings.BookySyncPassword ?? string.Empty;
            return credentials;
        }
    }
}
