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
    public class AbroAiSyncService : IAbroAiSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly IAbroAiStoreService _storeService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiSyncService"/> class.
        /// </summary>
        public AbroAiSyncService(
            ISettingsService settingsService,
            IAbroAiStoreService storeService,
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
                return settings.AbroAiSyncEnabled
                    && !string.IsNullOrWhiteSpace(settings.BookySyncUrl)
                    && !string.IsNullOrWhiteSpace(settings.AbroAiSyncRemotePath);
            }
        }

        /// <inheritdoc/>
        public Task<AbroAiSyncResult> SyncAsync()
        {
            return SyncInternalAsync(overwriteRemote: false);
        }

        /// <inheritdoc/>
        public Task<AbroAiSyncResult> OverwriteRemoteAsync()
        {
            return SyncInternalAsync(overwriteRemote: true);
        }

        private async Task<AbroAiSyncResult> SyncInternalAsync(bool overwriteRemote)
        {
            CloudStorageCredentials credentials = null;
            LastError = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.AbroAiSyncEnabled)
                    return AbroAiSyncResult.Disabled;

                if (string.IsNullOrWhiteSpace(settings.BookySyncUrl))
                    return AbroAiSyncResult.NotConfigured;

                (string folderUrl, string fileName) = SplitRemotePath(settings.BookySyncUrl, settings.AbroAiSyncRemotePath);
                credentials = BuildCredentials(folderUrl, settings);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                if (!overwriteRemote)
                {
                    AbroAiStoreModel remote = new AbroAiStoreModel();
                    if (await client.ExistsFileAsync(fileName, credentials).ConfigureAwait(false))
                    {
                        byte[] bytes = await client.DownloadFileAsync(fileName, credentials).ConfigureAwait(false);
                        remote = AbroAiStoreFormat.Parse(Encoding.UTF8.GetString(bytes));
                        if (remote == null)
                        {
                            LastError = "The AI store file on the server has an unexpected format.";
                            return AbroAiSyncResult.Failed;
                        }
                    }

                    _storeService.MergeFromRemote(remote);
                }

                AbroAiStoreModel local = _storeService.LoadOrDefault();
                string json = AbroAiStoreFormat.Serialize(local);
                await client.UploadFileAsync(fileName, Encoding.UTF8.GetBytes(json), credentials).ConfigureAwait(false);
                return AbroAiSyncResult.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return AbroAiSyncResult.Failed;
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
                path = "abro/ai.json";

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
