// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;

namespace SilentNotes
{
    /// <summary>
    /// Receiver for incoming MMS notifications. It must be declared so aNote is eligible to be the
    /// default SMS app on every Android version, but MMS bodies are intentionally not handled.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
    [IntentFilter(new[] { "android.provider.Telephony.WAP_PUSH_DELIVER" }, DataMimeType = "application/vnd.wap.mms-message")]
    public class MmsReceiver : BroadcastReceiver
    {
        /// <inheritdoc/>
        public override void OnReceive(Context context, Intent intent)
        {
            // MMS is not supported, the notification is intentionally ignored.
        }
    }
}
