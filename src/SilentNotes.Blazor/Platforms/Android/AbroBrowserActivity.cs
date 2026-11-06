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
        Exported = true,
        LaunchMode = LaunchMode.SingleTask,
        TaskAffinity = "app.anote.abro",
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "http", "https" })]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "http", "https" },
        DataMimeTypes = new[] { "text/html", "text/plain", "application/xhtml+xml" })]
    public class AbroBrowserActivity : MauiAppCompatActivity
    {
        /// <summary>Intent extra: optional URL to open.</summary>
        public const string ExtraUrl = "abro_url";

        /// <summary>Intent extra: <see cref="AbroBrowserPage.AbroStartAction"/> ordinal.</summary>
        public const string ExtraStartAction = "abro_start_action";

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            AbroBrowserPage.EnsureUrlExtraFromIntentData(Intent);
            base.OnCreate(savedInstanceState);
        }

        /// <inheritdoc/>
        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            AbroBrowserPage.EnsureUrlExtraFromIntentData(intent);
            AbroBrowserPage.HandleIncomingIntent(intent);
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            // Drop stale launch extras so a recycled activity cannot revive a closed session.
            try
            {
                Intent?.RemoveExtra(ExtraUrl);
                Intent?.RemoveExtra(ExtraStartAction);
            }
            catch
            {
            }

            base.OnDestroy();
        }
    }
}
