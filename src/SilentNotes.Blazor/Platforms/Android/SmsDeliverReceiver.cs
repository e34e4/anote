// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Receiver which is triggered when aNote is the default SMS app and a new message arrives.
    /// It stores the message in the system message store and shows a notification.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
    [IntentFilter(new[] { "android.provider.Telephony.SMS_DELIVER" })]
    public class SmsDeliverReceiver : BroadcastReceiver
    {
        /// <inheritdoc/>
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                global::Android.Telephony.SmsMessage[] messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
                if ((messages == null) || (messages.Length == 0))
                    return;

                string address = messages[0].OriginatingAddress;
                var body = new StringBuilder();
                foreach (global::Android.Telephony.SmsMessage message in messages)
                    body.Append(message.MessageBody);

                string text = body.ToString();
                StoreInboxMessage(context, address, text);
                ShowNotification(context, address, text);
                NotifyRunningApp();
            }
            catch (Exception)
            {
            }
        }

        private static void StoreInboxMessage(Context context, string address, string text)
        {
            long threadId = Telephony.Threads.GetOrCreateThreadId(context, address);
            var values = new ContentValues();
            values.Put("address", address);
            values.Put("body", text);
            values.Put("date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            values.Put("read", 0);
            values.Put("type", 1); // inbox
            values.Put("thread_id", threadId);
            context.ContentResolver.Insert(Telephony.Sms.Inbox.ContentUri, values);
        }

        private static void ShowNotification(Context context, string address, string text)
        {
            Notification.Builder builder = AppNotifications.CreateBuilder(context, NotificationChannels.SmsChannelId);
            builder.SetContentTitle(string.IsNullOrWhiteSpace(address) ? "New message" : address);
            builder.SetContentText(text);
            builder.SetStyle(new Notification.BigTextStyle().BigText(text));
            builder.SetSmallIcon(AppNotifications.SmallIcon(context));
            builder.SetAutoCancel(true);
            builder.SetPriority((int)NotificationPriority.High);
            builder.SetContentIntent(AppNotifications.BuildOpenAppIntent(context, AppNotifications.SmsNotificationId));

            AppNotifications.GetManager(context)?.Notify(AppNotifications.SmsNotificationId, builder.Build());
        }

        private static void NotifyRunningApp()
        {
            try
            {
                Ioc.Instance.GetService<IMessengerService>()?.Send(new SmsChangedMessage());
            }
            catch (Exception)
            {
            }
        }
    }
}
