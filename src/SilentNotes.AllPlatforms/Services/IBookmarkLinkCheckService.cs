// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Schedules background bookmark link checks (Android WorkManager).
    /// </summary>
    public interface IBookmarkLinkCheckService
    {
        /// <summary>
        /// Enqueues a one-time background run that checks all bookmark URLs.
        /// </summary>
        void EnqueueOneTimeCheck();

        /// <summary>
        /// Ensures a periodic background check is registered (e.g. every 7 days).
        /// </summary>
        void EnsurePeriodicScheduled();
    }
}