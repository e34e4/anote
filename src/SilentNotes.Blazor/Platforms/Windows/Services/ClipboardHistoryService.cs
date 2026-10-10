// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IClipboardHistoryService"/> interface for the Windows
    /// platform. Windows has no background clipboard monitor, but the history store is still
    /// available, so the clipboard tab works.
    /// </summary>
    internal class ClipboardHistoryService : ClipboardHistoryServiceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardHistoryService"/> class.
        /// </summary>
        public ClipboardHistoryService(IXmlFileService xmlFileService)
            : base(xmlFileService)
        {
        }

        /// <inheritdoc/>
        protected override string GetLocation()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "clipboard");
        }
    }
}
