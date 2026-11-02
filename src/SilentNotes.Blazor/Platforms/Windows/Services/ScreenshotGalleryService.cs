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
    /// <summary>Windows stub — screenshots are Android-only.</summary>
    internal class ScreenshotGalleryService : IScreenshotGalleryService
    {
        public bool IsSupported => false;

        public IReadOnlyList<AudioRecordingModel> GetScreenshots() => Array.Empty<AudioRecordingModel>();

        public bool RenameScreenshot(string oldFileName, string newFileName) => false;

        public bool DeleteScreenshot(string fileName) => false;

        public Task<string> SaveToPublicStorageAsync(string fileName) => Task.FromResult<string>(null);

        public Task<bool> ShareScreenshotAsync(string fileName) => Task.FromResult(false);
    }
}
