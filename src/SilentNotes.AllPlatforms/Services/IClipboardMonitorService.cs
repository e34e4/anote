// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Monitors the system clipboard in the background and stores copied content in the clipboard
    /// history. Android does not allow regular apps to read the clipboard in the background, so a
    /// small focusable overlay window is shown to temporarily get the focus (and with it the
    /// permission to read the clipboard). This property can be null, if the OS does not support
    /// monitoring the clipboard.
    /// </summary>
    public interface IClipboardMonitorService
    {
        /// <summary>
        /// Gets a value indicating whether the operating system supports monitoring the clipboard
        /// in the background.
        /// </summary>
        bool CanMonitor { get; }

        /// <summary>
        /// Gets a value indicating whether the background monitor is currently running.
        /// </summary>
        bool IsMonitoringActive { get; }

        /// <summary>
        /// Gets a value indicating whether the permission to draw an overlay window is granted.
        /// </summary>
        bool HasOverlayPermission { get; }

        /// <summary>
        /// Starts or stops the background monitor.
        /// </summary>
        /// <param name="enabled">Set true to capture clipboard changes, false to stop capturing.</param>
        void SetMonitoringEnabled(bool enabled);

        /// <summary>
        /// Tries to obtain the overlay permission, using root if available, and otherwise opens the
        /// system settings page where the user can grant it manually.
        /// </summary>
        void RequestOverlayPermission();

        /// <summary>
        /// Opens the system settings page where the user can grant the overlay permission manually.
        /// </summary>
        void OpenOverlayPermissionSettings();

        /// <summary>
        /// Tells a running monitor that the persisted settings have changed and should be reloaded.
        /// </summary>
        void RefreshSettings();
    }
}
