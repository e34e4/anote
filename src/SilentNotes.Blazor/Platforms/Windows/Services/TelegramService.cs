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
    /// Windows implementation of <see cref="ITelegramService"/>.
    /// Store under AppData/telegram; downloads/sessions under AppData/tg.
    /// </summary>
    internal class TelegramService : TelegramServiceBase
    {
        public TelegramService(
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService,
            IInternetStateService internetStateService = null)
            : base(xmlFileService, dataProtectionService, internetStateService)
        {
        }

        /// <inheritdoc/>
        protected override string GetStoreDirectory()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "telegram");
        }

        /// <inheritdoc/>
        protected override string GetExternalTgRoot()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "tg");
        }

        /// <inheritdoc/>
        protected override void ShowIncomingMessageNotification(string title, string body, string chatKey, bool silent)
        {
            // No-op on Windows for now.
        }
    }
}
