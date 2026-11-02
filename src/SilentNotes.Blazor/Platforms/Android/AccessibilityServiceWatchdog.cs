// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
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
        /// <summary>
        /// File written for the root watchdog script so CRC class renames do not break heal.
        /// </summary>
        internal const string ComponentHintPath = "/data/local/tmp/anote_a11y_component";

        private static DateTime _lastHealUtc = DateTime.MinValue;
        private static string _cachedComponent;

        /// <summary>
        /// Resolves the current accessibility service component id for this build
        /// (package + .NET Android CRC class name).
        /// </summary>
        internal static string ResolveServiceComponent(Context context)
        {
            if (!string.IsNullOrEmpty(_cachedComponent))
                return _cachedComponent;

            try
            {
                Context app = context?.ApplicationContext ?? context;
                if (app == null)
                    return null;

                string component = new ComponentName(
                    app,
                    Java.Lang.Class.FromType(typeof(FloatingButtonAccessibilityService))).FlattenToString();
                if (!string.IsNullOrEmpty(component))
                    _cachedComponent = component;
                return component;
            }
            catch (Exception)
            {
                return _cachedComponent;
            }
        }

        /// <summary>
        /// Persists the live component id for the root shell watchdog (best-effort).
        /// </summary>
        internal static void PublishComponentHint(Context context)
        {
            string component = ResolveServiceComponent(context);
            if (string.IsNullOrEmpty(component))
                return;

            try
            {
                // World-readable hint for the Magisk/KSU script (same path as install helper).
                File.WriteAllText(ComponentHintPath, component);
            }
            catch (Exception)
            {
                try
                {
                    string script =
                        "printf '%s\\n' '" + component.Replace("'", "'\\''") + "' > "
                        + ComponentHintPath + " && chmod 644 " + ComponentHintPath;
                    using var process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "su", "-c", script });
                    process.WaitFor(3L, Java.Util.Concurrent.TimeUnit.Seconds);
                }
                catch (Exception)
                {
                }
            }
        }

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
                PublishComponentHint(context);

                if (IsServiceEnabled(context))
                {
                    // Service is listed as enabled; if Instance is up, make sure the overlay matches settings.
                    FloatingButtonAccessibilityService.Instance?.EnsureOverlayMatchesSettings();
                    return;
                }

                if ((DateTime.UtcNow - _lastHealUtc).TotalSeconds < 45)
                    return;

                string component = ResolveServiceComponent(context);
                if (string.IsNullOrEmpty(component))
                    return;

                if (!TryReenableWithRoot(component))
                    return;

                _lastHealUtc = DateTime.UtcNow;
                global::Android.Util.Log.Info("aNoteFloating", "Re-enabled accessibility service via root: " + component);
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

                string component = ResolveServiceComponent(context);
                if (string.IsNullOrEmpty(component))
                    return false;

                string needle = ":" + component + ":";
                return (":" + enabled + ":").IndexOf(needle, StringComparison.Ordinal) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReenableWithRoot(string serviceComponent)
        {
            // settings get/put need root or WRITE_SECURE_SETTINGS.
            // Rebuild the list without duplicate copies of our component, then append once.
            string escaped = serviceComponent.Replace("'", "'\\''");
            string script =
                "COMPONENT='" + escaped + "'; "
                + "enabled=$(settings get secure enabled_accessibility_services); "
                + "new=''; "
                + "OLD_IFS=$IFS; IFS=':'; "
                + "for part in $enabled; do "
                + "[ -z \"$part\" ] && continue; "
                + "[ \"$part\" = null ] && continue; "
                + "[ \"$part\" = \"$COMPONENT\" ] && continue; "
                + "if [ -z \"$new\" ]; then new=\"$part\"; else new=\"${new}:$part\"; fi; "
                + "done; IFS=$OLD_IFS; "
                + "if [ -z \"$new\" ]; then new=\"$COMPONENT\"; else new=\"${new}:$COMPONENT\"; fi; "
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
