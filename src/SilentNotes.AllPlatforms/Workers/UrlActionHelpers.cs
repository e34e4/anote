// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Services;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Helpers to detect http(s) links in plain text and open them in the system browser.
    /// </summary>
    public static class UrlActionHelpers
    {
        /// <summary>
        /// Determines whether the text contains an http(s) url that can be opened externally.
        /// </summary>
        public static bool ContainsOpenableUrl(string text)
        {
            return TryGetFirstOpenableUrl(text, out _);
        }

        /// <summary>
        /// Extracts the first openable http(s) url from the text, if any.
        /// </summary>
        public static bool TryGetFirstOpenableUrl(string text, out string url)
        {
            url = BookyUrlUtils.FirstUrl(text);
            if (string.IsNullOrEmpty(url) || !WebviewUtils.IsExternalUri(url))
            {
                url = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Opens the first http(s) url found in the text in the external browser.
        /// </summary>
        public static void OpenFirstUrl(INativeBrowserService browserService, string text)
        {
            if (browserService == null)
                return;

            if (TryGetFirstOpenableUrl(text, out string url))
                browserService.OpenWebsite(url);
        }
    }
}
