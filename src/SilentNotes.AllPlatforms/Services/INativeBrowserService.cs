// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Allows the app to work with an external browser (standard browser of the platform) and
    /// optionally register aNote as the system's default URLCheck browser on Android.
    /// </summary>
    public interface INativeBrowserService
    {
        /// <summary>
        /// Opens a website in the external browser.
        /// </summary>
        /// <param name="url">Url to the website.</param>
        void OpenWebsite(string url);

        /// <summary>
        /// Opens a website in a new tab of the external browser. This is used for text-fragment
        /// urls, because an existing tab would restore its old scroll position and cancel the
        /// scroll-to-text behavior.
        /// </summary>
        /// <param name="url">Url to the website.</param>
        void OpenWebsiteInNewTab(string url);

        /// <summary>
        /// Opens a website using the external browser inside the app if possible.
        /// </summary>
        /// <remarks>
        /// OAuth2 authentication requires a login in an external browser, but we want to avoid
        /// switching between app and browser. With Androids "custom tabs" we can start the browser
        /// inside the app, thus the app remains active and the browser can be closed afterwards.
        /// </remarks>
        /// <param name="url">Url to the website.</param>
        void OpenWebsiteInApp(string url);

        /// <summary>
        /// Gets a value indicating whether the operating system supports setting aNote as the
        /// default system web browser (URLCheck mode).
        /// </summary>
        bool CanSetDefaultBrowser => false;

        /// <summary>
        /// Gets a value indicating whether aNote is currently set as the default system web browser.
        /// </summary>
        bool IsDefaultBrowserApp => false;

        /// <summary>
        /// Prompts the user to set aNote as the default system web browser (or opens the system
        /// default browser settings).
        /// </summary>
        Task<bool> RequestDefaultBrowserAppAsync() => Task.FromResult(false);
    }
}
