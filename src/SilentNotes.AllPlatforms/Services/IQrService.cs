// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// A service to scan QR codes with the camera, to decode codes from image files and to
    /// generate own QR codes.
    /// </summary>
    public interface IQrService
    {
        /// <summary>Gets a value indicating whether the platform can scan codes with the camera.</summary>
        bool IsScanSupported { get; }

        /// <summary>Gets a value indicating whether the app is allowed to use the camera.</summary>
        bool HasCameraPermission { get; }

        /// <summary>
        /// Asks the user for the camera permission.
        /// </summary>
        /// <returns>True if the permission was granted, otherwise false.</returns>
        Task<bool> RequestCameraPermissionAsync();

        /// <summary>
        /// Opens the camera and scans for a QR code. The method returns as soon as a code was
        /// found or the user canceled the scanning.
        /// </summary>
        /// <returns>The decoded text, or null if scanning was canceled or is not supported.</returns>
        Task<string> ScanQrAsync();

        /// <summary>
        /// Tries to decode a QR code from an encoded image file (PNG, JPEG...).
        /// </summary>
        /// <param name="image">The encoded image file.</param>
        /// <returns>The decoded text, or null if no code was found.</returns>
        Task<string> DecodeImageAsync(byte[] image);

        /// <summary>
        /// Generates a QR code image for a given text.
        /// </summary>
        /// <param name="text">The text or link to encode.</param>
        /// <param name="size">The width and height of the image in pixels.</param>
        /// <returns>The encoded QR code image (PNG), or null if the text could not be encoded.</returns>
        Task<byte[]> GenerateQrPngAsync(string text, int size);

        /// <summary>
        /// Gives a short haptic feedback, e.g. after a code was scanned.
        /// </summary>
        void Vibrate();
    }
}
