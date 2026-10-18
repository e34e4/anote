// Copyright © 2024 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Shows a floating button on top of other applications, which opens a tab/keyboard menu or
    /// executes configured gestures. On Android this is realized with an accessibility service,
    /// on other platforms this is not available.
    /// </summary>
    public interface IFloatingButtonService
    {
        /// <summary>
        /// Shows or hides the floating button, if the accessibility service is running. This is a
        /// no-op when the service is not enabled.
        /// </summary>
        /// <param name="visible">Set true to show the floating button, false to hide it.</param>
        void SetFloatingButtonVisible(bool visible);

        /// <summary>
        /// Opens the operating system accessibility settings, so the user can enable or disable
        /// the accessibility service.
        /// </summary>
        void OpenAccessibilitySettings();

        /// <summary>
        /// Returns all launchable applications installed on the device so the user can assign them
        /// to floating button gestures.
        /// </summary>
        IReadOnlyList<InstalledAppInfoModel> GetInstalledApps() => Array.Empty<InstalledAppInfoModel>();

        /// <summary>
        /// Opens the system input method (keyboard) switcher dialog.
        /// </summary>
        void ShowKeyboardSwitcher()
        {
        }
    }
}
