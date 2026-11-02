// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Hosts Abro in its own Android task so aNote remains usable in the main task without
    /// closing the browser.
    /// </summary>
    [Activity(
        Label = "Abro",
        Theme = "@style/Maui.SplashTheme",
        Exported = false,
        LaunchMode = LaunchMode.SingleTask,
        TaskAffinity = "app.anote.abro",
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class AbroBrowserActivity : MauiAppCompatActivity
    {
        /// <summary>Intent extra: optional URL to open.</summary>
        public const string ExtraUrl = "abro_url";

        /// <summary>Intent extra: <see cref="AbroBrowserPage.AbroStartAction"/> ordinal.</summary>
        public const string ExtraStartAction = "abro_start_action";

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
        }

        /// <inheritdoc/>
        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            AbroBrowserPage.HandleIncomingIntent(intent);
        }
    }
}
