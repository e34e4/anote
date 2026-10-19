// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using Microsoft.Maui.Storage;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Windows implementation of <see cref="IMailService"/>.
    /// </summary>
    internal class MailService : MailServiceBase
    {
        public MailService(
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService,
            ICloudStorageClientFactory cloudStorageClientFactory)
            : base(xmlFileService, dataProtectionService, cloudStorageClientFactory)
        {
        }

        /// <inheritdoc/>
        protected override string GetStorageDirectory()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "mail");
        }
    }
}
