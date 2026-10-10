// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using Microsoft.Extensions.DependencyInjection;
using VanillaCloudStorageClient;

namespace SilentNotes.Stories.SynchronizationStory
{
    /// <summary>
    /// This step is an end point of the <see cref="SynchronizationStory"/>. It merges the
    /// local repository with the downloaded repository and stores the merged repository.
    /// </summary>
    internal class StoreMergedRepositoryAndQuitStep : SynchronizationStoryStepBase
    {
        /// <inheritdoc/>
        public override async Task<StoryStepResult<SynchronizationStoryModel>> RunStep(SynchronizationStoryModel model, IServiceProvider serviceProvider, StoryMode uiMode)
        {
            System.Diagnostics.Debug.WriteLine("** " + nameof(StoreMergedRepositoryAndQuitStep) + " " + uiMode.ToString());

            try
            {
                var settingsService = serviceProvider.GetService<ISettingsService>();
                var repositoryStorageService = serviceProvider.GetService<IRepositoryStorageService>();
                var languageService = serviceProvider.GetService<ILanguageService>();
                var cryptoRandomService = serviceProvider.GetService<ICryptoRandomService>();
                var cloudStorageClientFactory = serviceProvider.GetService<ICloudStorageClientFactory>();
                var clipboardSyncService = serviceProvider.GetService<IClipboardSyncService>();

                repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel localRepository);
                SettingsModel settings = settingsService.LoadSettingsOrDefault();

                // Merge repositories
                NoteRepositoryMerger merger = new NoteRepositoryMerger();
                NoteRepositoryModel mergedRepository = merger.Merge(localRepository, model.CloudRepository);

                // Merge the clipboard history with the downloaded clipboard and write it back to
                // the local device. The transfer collection is not persisted locally.
                IReadOnlyList<ClipboardTransferItemModel> mergedClipboard = clipboardSyncService.MergeAndImport(model.CloudRepository.Clipboard);

                bool repositoryChanged = !RepositoriesAreEqual(mergedRepository, localRepository);
                bool clipboardChanged = GetClipboardFingerprint(mergedClipboard) != GetClipboardFingerprint(model.CloudRepository.Clipboard);

                // Store merged repository locally when different
                if (repositoryChanged)
                {
                    repositoryStorageService.TrySaveRepository(mergedRepository);
                }

                // Store merged repository to the cloud when different, otherwise spare the slow upload
                if (repositoryChanged || clipboardChanged)
                {
                    mergedRepository.Clipboard = new ClipboardTransferListModel(mergedClipboard);
                    byte[] encryptedRepository = EncryptRepository(
                        mergedRepository, settings.TransferCode, cryptoRandomService, settings.SelectedEncryptionAlgorithm, settings.SelectedKdfAlgorithm);
                    clipboardSyncService.ClearTransfer(mergedRepository);

                    ICloudStorageClient cloudStorageClient = cloudStorageClientFactory.GetByKey(model.Credentials.CloudStorageId);
                    await cloudStorageClient.UploadFileAsync(NoteRepositoryModel.RepositoryFileName, encryptedRepository, model.Credentials);
                }

                return ToResult(new StopAndShowRepositoryStep(), languageService["sync_success"]);
            }
            catch (Exception ex)
            {
                // Keep the current page open and show the error message
                return ToResult(ex);
            }
        }

        private static bool RepositoriesAreEqual(NoteRepositoryModel repository1, NoteRepositoryModel repository2)
        {
            return repository1.GetModificationFingerprint() == repository2.GetModificationFingerprint();
        }

        private static long GetClipboardFingerprint(IEnumerable<ClipboardTransferItemModel> items)
        {
            var hashCodes = new List<long>();
            foreach (ClipboardTransferItemModel item in items)
            {
                hashCodes.Add(item.Id.GetHashCode());
                hashCodes.Add(item.ModifiedUtc.GetHashCode());
                hashCodes.Add(item.IsDeleted.GetHashCode());
            }
            return ModificationDetector.CombineHashCodes(hashCodes);
        }
    }
}
