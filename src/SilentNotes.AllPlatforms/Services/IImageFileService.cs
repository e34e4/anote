// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Lets the app hand an image file over to other apps, and lets the user save an image to the
    /// pictures folder of the device.
    /// </summary>
    public interface IImageFileService
    {
        /// <summary>Gets a value indicating whether the platform can open and save images.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Opens an image file with an external viewer application of the operating system.
        /// </summary>
        /// <param name="filePath">The full path of the local image file.</param>
        /// <returns>True if a viewer was opened, otherwise false.</returns>
        Task<bool> OpenImageAsync(string filePath);

        /// <summary>
        /// Saves an image file as a JPEG picture into the picture folder of the device, the file
        /// name is built from the current date and time.
        /// </summary>
        /// <param name="filePath">The full path of the local image file.</param>
        /// <returns>The display path of the saved picture, or null if it could not be saved.</returns>
        Task<string> SaveImageToPicturesAsync(string filePath);
    }
}
