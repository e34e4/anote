// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Telephony;

namespace SilentNotes
{
    /// <summary>
    /// Service which sends an SMS in the name of another application (quick reply from a
    /// notification). It must be declared so aNote is eligible to be the default SMS app.
    /// </summary>
    [Service(Exported = true, Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
    [IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "sms")]
    [IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "smsto")]
    [IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "mms")]
    [IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "mmsto")]
    public class RespondViaMessageService : Service
    {
        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            try
            {
                string text = intent?.GetStringExtra(Intent.ExtraText);
                string address = intent?.Data?.SchemeSpecificPart;
                if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(address))
                    SmsManager.Default?.SendTextMessage(address, null, text, null, null);
            }
            catch (Exception)
            {
            }

            StopSelf();
            return StartCommandResult.NotSticky;
        }

        /// <inheritdoc/>
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }
    }
}
