// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Opens the built-in image editor (crop, draw, convert, EXIF) for a local image file.
    /// </summary>
    public interface IImageEditorService
    {
        /// <summary>Gets a value indicating whether an in-app image editor is available.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Opens the image editor for the given local filesystem path (or content/file uri string).
        /// </summary>
        /// <param name="filePath">Absolute path or content/file uri of the image.</param>
        /// <returns>True when the editor was launched.</returns>
        Task<bool> OpenEditorAsync(string filePath);
    }
}
