// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using AndroidX.Core.Content;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IMailService"/>.
    /// </summary>
    internal class MailService : MailServiceBase
    {
        private readonly IAppContextService _appContext;

        public MailService(
            IAppContextService appContextService,
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService)
            : base(xmlFileService, dataProtectionService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        protected override string GetStorageDirectory()
        {
            string baseDir = ContextCompat.GetNoBackupFilesDir(_appContext.Context).AbsolutePath;
            return Path.Combine(baseDir, "mail");
        }
    }
}
