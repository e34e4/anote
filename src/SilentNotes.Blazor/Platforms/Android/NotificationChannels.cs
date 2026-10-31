// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;

namespace SilentNotes
{
    /// <summary>
    /// Creates the notification channels used by aNote. On Android 8 and above every notification
    /// must belong to a channel, the channels are created once when the application starts.
    /// </summary>
    internal static class NotificationChannels
    {
        /// <summary>Channel for ringing alarms, it is loud and uses the full screen intent.</summary>
        public const string AlarmChannelId = "anote_alarm";

        /// <summary>Channel for finished timers.</summary>
        public const string TimerChannelId = "anote_timer";

        /// <summary>Channel for received SMS messages.</summary>
        public const string SmsChannelId = "anote_sms";

        /// <summary>Channel for incoming and active phone calls.</summary>
        public const string CallChannelId = "anote_call";

        /// <summary>Channel for Telegram incoming messages.</summary>
        public const string TelegramChannelId = "anote_telegram";

        /// <summary>Channel for ongoing screen recording.</summary>
        public const string ScreenRecordChannelId = "anote_screen_record";

        /// <summary>Creates all notification channels, can be called repeatedly.</summary>
        public static void CreateAll(Context context)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService);
            if (manager == null)
                return;

            var alarmChannel = new NotificationChannel(AlarmChannelId, "Alarms", NotificationImportance.High);
            alarmChannel.Description = "Ringing alarms";
            alarmChannel.EnableVibration(true);
            alarmChannel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Alarm), AlarmAudioAttributes());
            alarmChannel.LockscreenVisibility = NotificationVisibility.Public;
            manager.CreateNotificationChannel(alarmChannel);

            var timerChannel = new NotificationChannel(TimerChannelId, "Timer", NotificationImportance.High);
            timerChannel.Description = "Finished timers";
            timerChannel.EnableVibration(true);
            timerChannel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Notification), AlarmAudioAttributes());
            manager.CreateNotificationChannel(timerChannel);

            var smsChannel = new NotificationChannel(SmsChannelId, "Messages", NotificationImportance.High);
            smsChannel.Description = "Received text messages";
            smsChannel.EnableVibration(true);
            smsChannel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Notification), NotificationAudioAttributes());
            manager.CreateNotificationChannel(smsChannel);

            var callChannel = new NotificationChannel(CallChannelId, "Phone calls", NotificationImportance.High);
            callChannel.Description = "Incoming and ongoing phone calls";
            callChannel.EnableVibration(true);
            callChannel.LockscreenVisibility = NotificationVisibility.Public;
            manager.CreateNotificationChannel(callChannel);

            var telegramChannel = new NotificationChannel(TelegramChannelId, "Telegram", NotificationImportance.High);
            telegramChannel.Description = "Incoming Telegram messages";
            telegramChannel.EnableVibration(true);
            telegramChannel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Notification), NotificationAudioAttributes());
            manager.CreateNotificationChannel(telegramChannel);

            var screenRecordChannel = new NotificationChannel(ScreenRecordChannelId, "Screen recording", NotificationImportance.Low);
            screenRecordChannel.Description = "Ongoing screen recording";
            screenRecordChannel.EnableVibration(false);
            screenRecordChannel.SetSound(null, null);
            manager.CreateNotificationChannel(screenRecordChannel);
        }

        /// <summary>Gets audio attributes which play on the alarm stream.</summary>
        public static AudioAttributes AlarmAudioAttributes()
        {
            return new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Alarm)
                .SetContentType(AudioContentType.Sonification)
                .Build();
        }

        /// <summary>Gets audio attributes which play on the notification stream.</summary>
        public static AudioAttributes NotificationAudioAttributes()
        {
            return new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification)
                .SetContentType(AudioContentType.Sonification)
                .Build();
        }
    }
}
