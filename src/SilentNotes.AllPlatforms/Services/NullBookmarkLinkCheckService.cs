// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// No-op implementation used on platforms without WorkManager background workers.
    /// </summary>
    public sealed class NullBookmarkLinkCheckService : IBookmarkLinkCheckService
    {
        /// <inheritdoc/>
        public void EnqueueOneTimeCheck()
        {
        }

        /// <inheritdoc/>
        public void EnsurePeriodicScheduled()
        {
        }
    }
}