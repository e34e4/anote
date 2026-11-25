// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Fallback when no platform document crop UI is available.
    /// </summary>
    public class NullDocumentCropService : IDocumentCropService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public Task<byte[]> CropDocumentAsync(string filePath) => Task.FromResult<byte[]>(null);
    }
}