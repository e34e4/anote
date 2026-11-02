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
    /// Manages screenshots saved by the floating-button capture for the Rec → Shots sub-tab.
    /// </summary>
    public interface IScreenshotGalleryService
    {
        /// <summary>Gets whether screenshot gallery access is available on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Lists saved screenshots, newest first.</summary>
        IReadOnlyList<AudioRecordingModel> GetScreenshots();

        /// <summary>Renames a screenshot file.</summary>
        bool RenameScreenshot(string oldFileName, string newFileName);

        /// <summary>Deletes a screenshot file.</summary>
        bool DeleteScreenshot(string fileName);

        /// <summary>Exports a copy to public Pictures storage.</summary>
        Task<string> SaveToPublicStorageAsync(string fileName);

        /// <summary>Opens the system share sheet for the screenshot.</summary>
        Task<bool> ShareScreenshotAsync(string fileName);
    }
}
