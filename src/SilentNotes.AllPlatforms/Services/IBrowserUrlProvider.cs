// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Provides the url of the page currently shown in a supported browser. On Android this is
    /// captured by an accessibility service, on other platforms this feature is not available.
    /// </summary>
    public interface IBrowserUrlProvider
    {
        /// <summary>
        /// Gets the url of the browser page which was seen most recently, or null if no recent url
        /// is available.
        /// </summary>
        /// <returns>The last browser url, or null.</returns>
        string LastBrowserUrl();

        /// <summary>
        /// Gets a value indicating whether the accessibility service is running and url capture is
        /// enabled by the user.
        /// </summary>
        bool IsCaptureActive { get; }

        /// <summary>
        /// Enables or disables the capture of the browser url. Called when the user changes the
        /// setting while the accessibility service is already running.
        /// </summary>
        /// <param name="enabled">True to capture the browser url.</param>
        void SetCaptureEnabled(bool enabled);
    }
}
