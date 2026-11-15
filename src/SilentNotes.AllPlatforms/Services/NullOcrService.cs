// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Fallback OCR service for platforms where no text recognition engine is available.
    /// </summary>
    public class NullOcrService : IOcrService
    {
        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public Task<string> RecognizeTextAsync(byte[] imageBytes)
        {
            return Task.FromResult<string>(null);
        }
    }
}
