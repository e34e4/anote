// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Monitors the system clipboard and stores copied content in the clipboard history.
    /// On Android the monitoring is done by the accessibility service, because Android only allows
    /// the focused app or a running accessibility service to read the clipboard. This property can
    /// be null, if the OS does not support monitoring the clipboard.
    /// </summary>
    public interface IClipboardMonitorService
    {
        /// <summary>
        /// Gets a value indicating whether the operating system supports monitoring the clipboard.
        /// </summary>
        bool CanMonitor { get; }

        /// <summary>
        /// Gets a value indicating whether clipboard monitoring is currently active.
        /// </summary>
        bool IsMonitoringActive { get; }

        /// <summary>
        /// Gets a value indicating whether the accessibility service which is required to read the
        /// clipboard in the background is enabled.
        /// </summary>
        bool IsAccessibilityServiceEnabled { get; }

        /// <summary>
        /// Starts or stops the clipboard monitoring. When enabling it while the required
        /// accessibility service is not enabled yet, the user is directed to the system settings.
        /// </summary>
        /// <param name="enabled">Set true to capture clipboard changes, false to stop capturing.</param>
        void SetMonitoringEnabled(bool enabled);

        /// <summary>
        /// Tries to obtain the required permission, or opens the system settings page where the
        /// user can enable the accessibility service manually.
        /// </summary>
        void RequestAccessibilityPermission();

        /// <summary>
        /// Opens the system settings page where the user can enable the accessibility service.
        /// </summary>
        void OpenAccessibilitySettings();

        /// <summary>
        /// Tells a running monitor that the persisted settings have changed and should be reloaded.
        /// </summary>
        void RefreshSettings();

        /// <summary>
        /// Reads the current clipboard content once and stores it in the clipboard history. This is
        /// called when the application comes to the foreground, because a regular application is
        /// only allowed to read the clipboard while it owns the focus.
        /// </summary>
        void CaptureNow();
    }
}
