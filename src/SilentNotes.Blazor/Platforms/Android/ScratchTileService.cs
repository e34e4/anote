// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.QuickSettings;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Quick Settings tile which opens the scratchpad capture dialog over any active app.
    /// </summary>
    [Service(
        Exported = true,
        Permission = "android.permission.BIND_QUICK_SETTINGS_TILE",
        Label = "aNote scratchpad",
        Icon = "@mipmap/appicon")]
    [IntentFilter(new[] { "android.service.quicksettings.action.QS_TILE" })]
    public class ScratchTileService : TileService
    {
        public override void OnClick()
        {
            base.OnClick();

            var intent = new Intent(this, typeof(ScratchCaptureActivity));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
                {
                    PendingIntent pending = PendingIntent.GetActivity(
                        this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                    StartActivityAndCollapse(pending);
                }
                else
                {
#pragma warning disable CS0618
                    StartActivityAndCollapse(intent);
#pragma warning restore CS0618
                }
            }
            catch (Exception)
            {
            }
        }

        public override void OnStartListening()
        {
            base.OnStartListening();
            try
            {
                Tile tile = QsTile;
                if (tile != null)
                {
                    tile.Label = "aNote scratch";
                    tile.State = TileState.Active;
                    tile.UpdateTile();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
