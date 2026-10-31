// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Opens the light multi-tab "Abro" web browser, which keeps its history, cookies, cache and
    /// other files in a visible folder on the device storage.
    /// </summary>
    public interface IAbroBrowserService
    {
        /// <summary>Gets a value indicating whether the Abro browser is supported on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Shows the browser full screen.</summary>
        Task PresentAsync();

        /// <summary>Shows the browser and navigates to <paramref name="url"/> in a new or current tab.</summary>
        Task PresentUrlAsync(string url);

        /// <summary>Shows the browser and opens the history picker.</summary>
        Task PresentHistoryAsync();

        /// <summary>Shows the browser and opens the bookmarks picker.</summary>
        Task PresentBookmarksAsync();
    }
}
