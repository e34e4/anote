// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub implementation of <see cref="ICameraService"/> for the Windows platform.
    /// </summary>
    internal class CameraService : ICameraService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool HasPermission => false;

        /// <inheritdoc/>
        public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public Task<CameraPhotoModel> CapturePhotoAsync(
            string format,
            int quality,
            bool useFrontCamera,
            string flashMode,
            bool autoSaveToGallery) => Task.FromResult<CameraPhotoModel>(null);

        /// <inheritdoc/>
        public Task<CameraPhotoModel> CaptureWithSystemCameraAsync(
            string format,
            int quality,
            bool autoSaveToGallery) => Task.FromResult<CameraPhotoModel>(null);

        /// <inheritdoc/>
        public Task<CameraPhotoModel> ConvertExistingImageAsync(
            byte[] imageBytes,
            string format,
            int quality,
            bool autoSaveToGallery) => Task.FromResult<CameraPhotoModel>(null);

        /// <inheritdoc/>
        public IReadOnlyList<CameraPhotoModel> GetPhotos() => Array.Empty<CameraPhotoModel>();

        /// <inheritdoc/>
        public Task<string> GetPhotoPreviewDataUrlAsync(string fileName, int maxDimension = 360) => Task.FromResult<string>(null);

        /// <inheritdoc/>
        public Task<bool> OpenPhotoAsync(string fileName) => Task.FromResult(false);

        /// <inheritdoc/>
        public Task<string> SaveToPublicGalleryAsync(string fileName) => Task.FromResult<string>(null);

        /// <inheritdoc/>
        public Task<bool> SharePhotoAsync(string fileName) => Task.FromResult(false);

        /// <inheritdoc/>
        public bool RenamePhoto(string fileName, string newNameWithoutExtension) => false;

        /// <inheritdoc/>
        public bool DeletePhoto(string fileName) => false;
    }
}
