// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;

namespace SilentNotes
{
    /// <summary>
    /// Helper methods to build the system notifications of aNote (ringing alarm, finished timer and
    /// received messages).
    /// </summary>
    internal static class AppNotifications
    {
        /// <summary>Notification id of the ringing alarm.</summary>
        public const int AlarmNotificationId = 7401;

        /// <summary>Notification id of the finished timer.</summary>
        public const int TimerNotificationId = 7402;

        /// <summary>Notification id of the received message.</summary>
        public const int SmsNotificationId = 7403;

        /// <summary>Notification id of an incoming or active phone call.</summary>
        public const int CallNotificationId = 7404;

        /// <summary>Base notification id for Telegram messages (per-chat ids are derived from this).</summary>
        public const int TelegramNotificationId = 7405;

        /// <summary>Stable per-chat notification id so parallel chats do not overwrite each other.</summary>
        public static int TelegramNotificationIdForChat(string chatKey)
        {
            if (string.IsNullOrEmpty(chatKey))
                return TelegramNotificationId;
            // Keep in a reserved band above the fixed app notification ids.
            int hash = chatKey.GetHashCode();
            return 7500 + (hash & 0x0FFF);
        }

        /// <summary>Gets the system notification manager.</summary>
        public static NotificationManager GetManager(Context context)
        {
            return (NotificationManager)context.GetSystemService(Context.NotificationService);
        }

        /// <summary>Builds an empty notification builder for a given channel.</summary>
        public static Notification.Builder CreateBuilder(Context context, string channelId)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                return new Notification.Builder(context, channelId);
#pragma warning disable CS0618
            return new Notification.Builder(context);
#pragma warning restore CS0618
        }

        /// <summary>Builds the pending intent which opens the clock tab of the application.</summary>
        public static PendingIntent BuildOpenAppIntent(Context context, int requestCode)
        {
            var intent = new Intent(context, typeof(MainActivity));
            intent.SetAction(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            // Telegram message notifications (base id or per-chat band 7500–11500) open the TG tab.
            if (requestCode == TelegramNotificationId || (requestCode >= 7500 && requestCode <= 11500))
                intent.PutExtra(Platforms.FloatingButtonAccessibilityService.ExtraOpenTab, 17);
            else
                intent.PutExtra(AlarmRingingService.ExtraOpenClock, true);
            intent.AddFlags(ActivityFlags.SingleTop);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            return PendingIntent.GetActivity(context, requestCode, intent, flags);
        }

        /// <summary>Gets the small icon used by all notifications.</summary>
        public static int SmallIcon(Context context)
        {
            return context.ApplicationInfo.Icon;
        }
    }
}
