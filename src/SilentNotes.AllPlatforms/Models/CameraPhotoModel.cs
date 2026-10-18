// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Metadata for a photo captured or converted in the Camera tab.
    /// </summary>
    public class CameraPhotoModel
    {
        /// <summary>Gets or sets the file name (including extension) inside the app's photos folder.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets the full local path to the photo file.</summary>
        public string FilePath { get; set; }

        /// <summary>Gets or sets the image format ("jpg", "png", or "webp").</summary>
        public string Format { get; set; }

        /// <summary>Gets or sets the compression quality percentage (1..100) used when saving.</summary>
        public int Quality { get; set; }

        /// <summary>Gets or sets the image width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>Gets or sets the image height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>Gets or sets the file size in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the UTC creation timestamp of the photo.</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Gets or sets the public gallery path if saved to device storage.</summary>
        public string PublicGalleryPath { get; set; }
    }
}
