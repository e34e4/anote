// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using SilentNotes.Models;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class PasswordVaultSyncService : IPasswordVaultSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly IPasswordVaultService _vaultService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordVaultSyncService"/> class.
        /// </summary>
        public PasswordVaultSyncService(
            ISettingsService settingsService,
            IPasswordVaultService vaultService,
            ICloudStorageClientFactory cloudStorageClientFactory)
        {
            _settingsService = settingsService;
            _vaultService = vaultService;
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
                return settings.BookySyncEnabled
                    && !string.IsNullOrWhiteSpace(settings.BookySyncUrl)
                    && !string.IsNullOrWhiteSpace(GetRemotePath(settings));
            }
        }

        /// <inheritdoc/>
        public async Task<PasswordVaultSyncResult> SyncAsync()
        {
            CloudStorageCredentials credentials = null;
            LastError = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.BookySyncEnabled)
                    return PasswordVaultSyncResult.Disabled;

                if (string.IsNullOrWhiteSpace(settings.BookySyncUrl))
                    return PasswordVaultSyncResult.NotConfigured;

                string remotePath = GetRemotePath(settings);
                if (string.IsNullOrWhiteSpace(remotePath))
                    return PasswordVaultSyncResult.NotConfigured;

                (string folderUrl, string fileName) = SplitRemotePath(settings.BookySyncUrl, remotePath);
                credentials = BuildCredentials(folderUrl, settings);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                byte[] local = _vaultService.TryReadEncryptedBytes();
                byte[] remote = null;
                if (await client.ExistsFileAsync(fileName, credentials))
                    remote = await client.DownloadFileAsync(fileName, credentials);

                if ((local == null || local.Length == 0) && (remote == null || remote.Length == 0))
                    return PasswordVaultSyncResult.NothingToSync;

                if (local == null || local.Length == 0)
                {
                    if (!_vaultService.WriteEncryptedBytesAndLock(remote))
                    {
                        LastError = "Could not write the downloaded vault file.";
                        return PasswordVaultSyncResult.Failed;
                    }
                    return PasswordVaultSyncResult.Success;
                }

                if (remote == null || remote.Length == 0)
                {
                    await client.UploadFileAsync(fileName, local, credentials);
                    return PasswordVaultSyncResult.Success;
                }

                if (ByteArraysEqual(local, remote))
                    return PasswordVaultSyncResult.Success;

                // Both sides exist and differ: merge by ModifiedAt when unlocked, otherwise prefer
                // local (push) so an active device is not overwritten while the vault is locked.
                byte[] winner = local;
                if (_vaultService.IsUnlocked)
                {
                    PasswordVaultMergeResult merge = _vaultService.TryMergeRemoteCipher(remote);
                    if (merge == PasswordVaultMergeResult.RemoteApplied)
                    {
                        winner = _vaultService.TryReadEncryptedBytes() ?? local;
                    }
                    else if (merge == PasswordVaultMergeResult.DecryptFailed)
                    {
                        LastError = "Remote vault could not be decrypted with the current master password.";
                        return PasswordVaultSyncResult.Failed;
                    }
                    else
                    {
                        winner = local;
                    }
                }

                await client.UploadFileAsync(fileName, winner, credentials);
                return PasswordVaultSyncResult.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return PasswordVaultSyncResult.Failed;
            }
            finally
            {
                credentials?.Dispose();
            }
        }

        private static string GetRemotePath(SettingsModel settings)
        {
            string path = settings.PasswordVaultSyncRemotePath;
            if (string.IsNullOrWhiteSpace(path))
                path = PasswordVaultService.VaultFileName;
            return path.Trim();
        }

        private static (string FolderUrl, string FileName) SplitRemotePath(string baseUrl, string remotePath)
        {
            string basePart = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = (remotePath ?? string.Empty).Trim().TrimStart('/');
            if (path.Length == 0)
                path = PasswordVaultService.VaultFileName;

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

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}
