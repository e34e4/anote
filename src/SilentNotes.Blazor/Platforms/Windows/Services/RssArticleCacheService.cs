// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Windows implementation of <see cref="IRssArticleCacheService"/>.
    /// </summary>
    internal class RssArticleCacheService : RssArticleCacheServiceBase
    {
        /// <inheritdoc/>
        protected override string GetLocation()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "rss");
        }
    }
}
