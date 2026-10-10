// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using AndroidX.Core.Content;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Restarts the clipboard monitor after the device has booted or the app was updated, when the
    /// user has switched the monitoring on.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
    public class ClipboardMonitorBootReceiver : BroadcastReceiver
    {
        private const string LogTag = "aNoteClipboard";

        /// <inheritdoc/>
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                if (!global::Android.Provider.Settings.CanDrawOverlays(context))
                    return;

                var services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider serviceProvider = services.BuildServiceProvider();
                serviceProvider.GetService<IAppContextService>().InitializeWithContextOnly(context);

                SettingsModel settings = serviceProvider.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                if (settings?.ClipboardMonitoringEnabled != true)
                    return;

                var serviceIntent = new Intent(context, typeof(ClipboardMonitorService));
                serviceIntent.SetAction(ClipboardMonitorService.ActionStart);
                ContextCompat.StartForegroundService(context, serviceIntent);
            }
            catch (System.Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not restart the clipboard monitor: " + ex);
            }
        }
    }
}
