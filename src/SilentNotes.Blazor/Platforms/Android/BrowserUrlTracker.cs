// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Stores the url of the browser page which was seen most recently. It is updated by the
    /// accessibility service and read when a bookmark is created, because Android does not pass
    /// the page url along with a text selection (PROCESS_TEXT).
    /// </summary>
    internal static class BrowserUrlTracker
    {
        private static readonly object SyncRoot = new object();
        private static string _lastUrl;
        private static string _lastPackage;
        private static long _updatedAtMs;

        /// <summary>
        /// Gets or sets a value indicating whether the accessibility service is connected.
        /// </summary>
        public static bool ServiceEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether url capture is enabled by the user.
        /// </summary>
        public static bool CaptureEnabled { get; set; }

        /// <summary>
        /// Stores a newly captured browser url.
        /// </summary>
        /// <param name="url">The captured url.</param>
        /// <param name="package">The package name of the browser.</param>
        public static void Update(string url, string package)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            lock (SyncRoot)
            {
                _lastUrl = url.Trim();
                _lastPackage = package;
                _updatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        /// <summary>
        /// Gets the most recently captured browser url, if it is not older than the given age.
        /// </summary>
        /// <param name="maxAgeMs">Maximum age of the url in milliseconds.</param>
        /// <returns>The url, or null.</returns>
        public static string LastBrowserUrl(int maxAgeMs = 15 * 60 * 1000)
        {
            lock (SyncRoot)
            {
                if (string.IsNullOrEmpty(_lastUrl))
                    return null;
                long age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _updatedAtMs;
                return (age <= maxAgeMs) ? _lastUrl : null;
            }
        }
    }
}
