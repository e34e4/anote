// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Opens a perspective document crop UI (four corners) and returns the warped image bytes.
    /// </summary>
    public interface IDocumentCropService
    {
        /// <summary>Gets a value indicating whether document perspective crop is available.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Lets the user adjust a four-corner crop and returns JPEG bytes of the flattened document.
        /// </summary>
        /// <param name="filePath">Absolute path to a local image file.</param>
        /// <returns>Cropped image bytes, or null when the user cancelled or crop failed.</returns>
        Task<byte[]> CropDocumentAsync(string filePath);
    }
}