// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Opens or shares decrypted attachment content with other apps. The content must be written
    /// to a location the platform can hand out through its FileProvider.
    /// </summary>
    public interface IAttachmentFileService
    {
        /// <summary>Opens the attachment in a viewer app.</summary>
        Task<bool> OpenAttachmentAsync(byte[] content, string fileName, string mimeType);

        /// <summary>Shares the attachment with other apps.</summary>
        Task<bool> ShareAttachmentAsync(byte[] content, string fileName, string mimeType);
    }
}
