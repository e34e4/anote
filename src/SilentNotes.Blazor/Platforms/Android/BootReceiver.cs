// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Receiver which reschedules the alarms and a still running timer after the device was
    /// rebooted, after the app was updated, or after the time / time zone changed.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    [IntentFilter(new[]
    {
        Intent.ActionBootCompleted,
        Intent.ActionMyPackageReplaced,
        Intent.ActionTimeChanged,
        Intent.ActionTimezoneChanged,
    })]
    public class BootReceiver : BroadcastReceiver
    {
        /// <inheritdoc/>
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                IClockService clockService = Ioc.Instance.GetService<IClockService>();
                if (clockService != null)
                    _ = clockService.RescheduleAllAsync();
            }
            catch (Exception)
            {
            }
        }
    }
}
