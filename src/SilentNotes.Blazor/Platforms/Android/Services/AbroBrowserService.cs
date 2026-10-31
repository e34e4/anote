// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IAbroBrowserService"/> interface for the Android platform.
    /// </summary>
    internal class AbroBrowserService : IAbroBrowserService
    {
        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public Task PresentAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(
                () => AbroBrowserPage.Present(null, AbroBrowserPage.AbroStartAction.None));
        }

        /// <inheritdoc/>
        public Task PresentUrlAsync(string url)
        {
            return MainThread.InvokeOnMainThreadAsync(
                () => AbroBrowserPage.Present(url, AbroBrowserPage.AbroStartAction.None));
        }

        /// <inheritdoc/>
        public Task PresentHistoryAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(
                () => AbroBrowserPage.Present(null, AbroBrowserPage.AbroStartAction.History));
        }

        /// <inheritdoc/>
        public Task PresentBookmarksAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(
                () => AbroBrowserPage.Present(null, AbroBrowserPage.AbroStartAction.Bookmarks));
        }
    }
}
