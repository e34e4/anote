// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.OS;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Receiver which is triggered by the AlarmManager when an alarm or a timer is due. It starts
    /// the ringing service for alarms, and shows a notification when a timer finished.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public class AlarmReceiver : BroadcastReceiver
    {
        /// <summary>Extra which specifies whether an "alarm" or a "timer" fired.</summary>
        public const string ExtraKind = "anote_clock_kind";

        /// <summary>Extra which carries the id of the alarm or timer.</summary>
        public const string ExtraId = "anote_clock_id";

        /// <summary>Extra which carries the label of the timer.</summary>
        public const string ExtraLabel = "anote_clock_label";

        /// <summary>Extra which carries the total seconds of the timer.</summary>
        public const string ExtraSeconds = "anote_clock_seconds";

        /// <summary>Kind value for an alarm.</summary>
        public const string KindAlarm = "alarm";

        /// <summary>Kind value for a timer.</summary>
        public const string KindTimer = "timer";

        /// <inheritdoc/>
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent == null)
                return;

            string kind = intent.GetStringExtra(ExtraKind) ?? KindAlarm;
            if (kind == KindTimer)
            {
                PostTimerNotification(context, intent);
                return;
            }

            string alarmId = intent.GetStringExtra(ExtraId);
            var serviceIntent = new Intent(context, typeof(AlarmRingingService));
            serviceIntent.SetAction(AlarmRingingService.ActionRing);
            serviceIntent.PutExtra(ExtraId, alarmId);
            serviceIntent.PutExtra(ExtraLabel, intent.GetStringExtra(ExtraLabel));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(serviceIntent);
            else
                context.StartService(serviceIntent);

            RescheduleOrDisable(alarmId);
        }

        private static void PostTimerNotification(Context context, Intent intent)
        {
            string label = intent.GetStringExtra(ExtraLabel);
            if (string.IsNullOrWhiteSpace(label))
                label = "Timer";

            Notification.Builder builder = AppNotifications.CreateBuilder(context, NotificationChannels.TimerChannelId);
            builder.SetContentTitle(label);
            builder.SetContentText("Timer finished");
            builder.SetSmallIcon(AppNotifications.SmallIcon(context));
            builder.SetAutoCancel(true);
            builder.SetCategory(Notification.CategoryAlarm);
            builder.SetPriority((int)NotificationPriority.High);
            builder.SetContentIntent(AppNotifications.BuildOpenAppIntent(context, AlarmRingingService.OpenClockRequestCode));

            NotificationManager manager = AppNotifications.GetManager(context);
            manager?.Notify(AppNotifications.TimerNotificationId, builder.Build());
        }

        /// <summary>
        /// Reschedules a repeating alarm for its next occurrence, or disables a one shot alarm.
        /// </summary>
        private static void RescheduleOrDisable(string alarmIdText)
        {
            if (!Guid.TryParse(alarmIdText, out Guid alarmId))
                return;

            try
            {
                IClockService clockService = Ioc.Instance.GetService<IClockService>();
                if (clockService == null)
                    return;

                ClockDataModel data = clockService.LoadData();
                AlarmModel alarm = data.Alarms.Find(a => a.Id == alarmId);
                if (alarm == null)
                    return;

                if (alarm.Repeats)
                {
                    _ = clockService.ScheduleAlarmAsync(alarm);
                }
                else
                {
                    alarm.Enabled = false;
                    clockService.TrySaveData(data);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
