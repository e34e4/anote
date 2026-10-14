// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IQrHistoryService"/> interface for the Windows platform.
    /// </summary>
    internal class QrHistoryService : QrHistoryServiceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QrHistoryService"/> class.
        /// </summary>
        /// <param name="xmlFileService">The xml file service used to store the history.</param>
        public QrHistoryService(IXmlFileService xmlFileService)
            : base(xmlFileService)
        {
        }

        /// <inheritdoc/>
        protected override string GetLocation()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "qr");
        }
    }
}
