// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Optical character recognition (OCR) for images and screenshots.
    /// </summary>
    public interface IOcrService
    {
        /// <summary>Gets a value indicating whether OCR is supported on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Recognizes the text in an encoded image.
        /// </summary>
        /// <param name="imageBytes">Encoded image bytes (JPEG, PNG, WEBP).</param>
        /// <returns>Recognized text, or null when no text was found.</returns>
        Task<string> RecognizeTextAsync(byte[] imageBytes);
    }
}
