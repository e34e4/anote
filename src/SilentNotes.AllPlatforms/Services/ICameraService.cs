// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Platform camera service supporting live viewfinder capture, system camera capture, and
    /// encoding photos in JPG, PNG, or WEBP format with a configurable quality percentage (1-100%).
    /// </summary>
    public interface ICameraService
    {
        /// <summary>Gets a value indicating whether camera capture is supported on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether camera permission is granted.</summary>
        bool HasPermission { get; }

        /// <summary>Requests camera permission from the user.</summary>
        Task<bool> RequestPermissionAsync();

        /// <summary>
        /// Opens the built-in live camera viewfinder and captures a photo encoded in the chosen
        /// format ("jpg", "png", "webp") and quality (1..100).
        /// </summary>
        Task<CameraPhotoModel> CapturePhotoAsync(
            string format,
            int quality,
            bool useFrontCamera,
            string flashMode,
            bool autoSaveToGallery);

        /// <summary>
        /// Launches the system camera app to capture a photo, then re-encodes the resulting image
        /// into the chosen format ("jpg", "png", "webp") and quality (1..100).
        /// </summary>
        Task<CameraPhotoModel> CaptureWithSystemCameraAsync(
            string format,
            int quality,
            bool autoSaveToGallery);

        /// <summary>
        /// Encodes raw image bytes into the chosen format ("jpg", "png", "webp") and quality (1..100)
        /// and stores the resulting photo.
        /// </summary>
        Task<CameraPhotoModel> ConvertExistingImageAsync(
            byte[] imageBytes,
            string format,
            int quality,
            bool autoSaveToGallery);

        /// <summary>Lists all photos stored in the app's camera directory, newest first.</summary>
        IReadOnlyList<CameraPhotoModel> GetPhotos();

        /// <summary>Builds a base64 data URL thumbnail for displaying in the UI.</summary>
        Task<string> GetPhotoPreviewDataUrlAsync(string fileName, int maxDimension = 360);

        /// <summary>Opens the photo in an external image viewer application.</summary>
        Task<bool> OpenPhotoAsync(string fileName);

        /// <summary>Exports the photo to the public device gallery (Pictures/aNote) keeping its format.</summary>
        Task<string> SaveToPublicGalleryAsync(string fileName);

        /// <summary>Shares the photo with other apps using the system share sheet.</summary>
        Task<bool> SharePhotoAsync(string fileName);

        /// <summary>Renames a stored photo keeping its extension.</summary>
        bool RenamePhoto(string fileName, string newNameWithoutExtension);

        /// <summary>Deletes a stored photo.</summary>
        bool DeletePhoto(string fileName);
    }
}
