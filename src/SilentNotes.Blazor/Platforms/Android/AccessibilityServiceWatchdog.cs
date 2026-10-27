// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.Content;
using Android.Provider;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Best-effort heal when Android disables aNote's accessibility service after a crash.
    /// Prefers a KernelSU/Magisk <c>service.d</c> script; this also re-enables on app resume via <c>su</c>.
    /// </summary>
    internal static class AccessibilityServiceWatchdog
    {
        internal const string ServiceComponent =
            "app.anote/crc6416b736807aca2ef0.FloatingButtonAccessibilityService";

        private static DateTime _lastHealUtc = DateTime.MinValue;

        /// <summary>
        /// If the floating-button accessibility service is not enabled, try to turn it back on with root.
        /// No-ops when already enabled, when root is unavailable, or when called too often.
        /// </summary>
        public static void EnsureEnabled(Context context)
        {
            if (context == null)
                return;

            try
            {
                if (IsServiceEnabled(context))
                    return;

                if ((DateTime.UtcNow - _lastHealUtc).TotalSeconds < 45)
                    return;

                if (!TryReenableWithRoot())
                    return;

                _lastHealUtc = DateTime.UtcNow;
                global::Android.Util.Log.Info("aNoteFloating", "Re-enabled accessibility service via root");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("aNoteFloating", "Accessibility heal failed: " + ex.Message);
            }
        }

        private static bool IsServiceEnabled(Context context)
        {
            try
            {
                if (FloatingButtonAccessibilityService.Instance != null)
                    return true;

                string enabled = Settings.Secure.GetString(
                    context.ContentResolver,
                    Settings.Secure.EnabledAccessibilityServices);
                if (string.IsNullOrEmpty(enabled))
                    return false;

                string needle = ":" + ServiceComponent + ":";
                return (":" + enabled + ":").IndexOf(needle, StringComparison.Ordinal) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReenableWithRoot()
        {
            // settings get/put need root or WRITE_SECURE_SETTINGS.
            string script =
                "enabled=$(settings get secure enabled_accessibility_services); "
                + "if [ -z \"$enabled\" ] || [ \"$enabled\" = null ]; then "
                + "new='" + ServiceComponent + "'; "
                + "else new=\"${enabled}:" + ServiceComponent + "\"; fi; "
                + "settings put secure enabled_accessibility_services \"$new\"; "
                + "settings put secure accessibility_enabled 1";

            try
            {
                using var process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "su", "-c", script });
                bool finished = process.WaitFor(8L, Java.Util.Concurrent.TimeUnit.Seconds);
                if (!finished)
                {
                    process.Destroy();
                    return false;
                }
                return process.ExitValue() == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
