// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;

namespace SilentNotes
{
    /// <summary>
    /// Foreground service which plays the ringing alarm and shows a full screen notification with
    /// the options to stop or snooze it. It is started by the <see cref="AlarmReceiver"/>.
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
    public class AlarmRingingService : Service
    {
        /// <summary>Action which starts the ringing.</summary>
        public const string ActionRing = "app.anote.action.RING";

        /// <summary>Action which stops the ringing.</summary>
        public const string ActionStop = "app.anote.action.STOP";

        /// <summary>Action which snoozes the alarm for a few minutes.</summary>
        public const string ActionSnooze = "app.anote.action.SNOOZE";

        /// <summary>Extra which tells the main activity to open the clock tab.</summary>
        public const string ExtraOpenClock = "anote_open_clock";

        /// <summary>Request code of the pending intent which opens the app.</summary>
        public const int OpenClockRequestCode = 7501;

        private const int StopRequestCode = 7502;
        private const int SnoozeRequestCode = 7503;
        private static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(5);

        private Ringtone _ringtone;
        private Vibrator _vibrator;

        /// <inheritdoc/>
        public override void OnCreate()
        {
            base.OnCreate();
            _vibrator = (Vibrator)GetSystemService(VibratorService);
        }

        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string action = intent?.Action ?? ActionRing;
            if (action == ActionStop)
            {
                StopRinging();
                return StartCommandResult.NotSticky;
            }
            if (action == ActionSnooze)
            {
                StopRinging();
                ScheduleSnooze();
                return StartCommandResult.NotSticky;
            }

            StartRinging();
            return StartCommandResult.NotSticky;
        }

        private void StartRinging()
        {
            Notification notification = BuildNotification();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(AppNotifications.AlarmNotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(AppNotifications.AlarmNotificationId, notification);

            try
            {
                _ringtone = RingtoneManager.GetRingtone(this, RingtoneManager.GetDefaultUri(RingtoneType.Alarm));
                if (_ringtone != null)
                {
                    try
                    {
                        _ringtone.AudioAttributes = NotificationChannels.AlarmAudioAttributes();
                    }
                    catch (Exception)
                    {
                    }

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                    {
                        try
                        {
                            _ringtone.Looping = true;
                        }
                        catch (Exception)
                        {
                        }
                    }
                    _ringtone.Play();
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    long[] pattern = new long[] { 0, 1000, 1000 };
                    _vibrator?.Vibrate(VibrationEffect.CreateWaveform(pattern, 0));
                }
                else
                {
#pragma warning disable CS0618
                    _vibrator?.Vibrate(new long[] { 0, 1000, 1000 }, 0);
#pragma warning restore CS0618
                }
            }
            catch (Exception)
            {
            }
        }

        private Notification BuildNotification()
        {
            PendingIntent stopIntent = BuildServiceIntent(ActionStop, StopRequestCode);
            PendingIntent snoozeIntent = BuildServiceIntent(ActionSnooze, SnoozeRequestCode);

            Notification.Builder builder = AppNotifications.CreateBuilder(this, NotificationChannels.AlarmChannelId);
            builder.SetContentTitle("Alarm");
            builder.SetContentText("Tap to open aNote");
            builder.SetSmallIcon(AppNotifications.SmallIcon(this));
            builder.SetCategory(Notification.CategoryAlarm);
            builder.SetPriority((int)NotificationPriority.Max);
            builder.SetOngoing(true);
            builder.SetAutoCancel(false);
            builder.SetContentIntent(AppNotifications.BuildOpenAppIntent(this, OpenClockRequestCode));
            builder.SetFullScreenIntent(AppNotifications.BuildOpenAppIntent(this, OpenClockRequestCode), true);

            var icon = Icon.CreateWithResource(this, AppNotifications.SmallIcon(this));
            builder.AddAction(new Notification.Action.Builder(icon, "Stop", stopIntent).Build());
            builder.AddAction(new Notification.Action.Builder(icon, "Snooze", snoozeIntent).Build());
            return builder.Build();
        }

        private PendingIntent BuildServiceIntent(string action, int requestCode)
        {
            var intent = new Intent(this, typeof(AlarmRingingService));
            intent.SetAction(action);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            return PendingIntent.GetService(this, requestCode, intent, flags);
        }

        private void ScheduleSnooze()
        {
            try
            {
                var intent = new Intent(this, typeof(AlarmReceiver));
                intent.SetAction("app.anote.action.SNOOZE_RING");
                intent.PutExtra(AlarmReceiver.ExtraKind, AlarmReceiver.KindAlarm);

                var flags = PendingIntentFlags.UpdateCurrent;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    flags |= PendingIntentFlags.Immutable;
                PendingIntent operation = PendingIntent.GetBroadcast(this, SnoozeRequestCode, intent, flags);

                var manager = (AlarmManager)GetSystemService(AlarmService);
                long triggerMs = DateTimeOffset.UtcNow.Add(SnoozeTime).ToUnixTimeMilliseconds();
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    manager?.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, operation);
                else
                    manager?.Set(AlarmType.RtcWakeup, triggerMs, operation);
            }
            catch (Exception)
            {
            }
        }

        private void StopRinging()
        {
            try
            {
                _ringtone?.Stop();
            }
            catch (Exception)
            {
            }
            _ringtone = null;

            try
            {
                _vibrator?.Cancel();
            }
            catch (Exception)
            {
            }

            AppNotifications.GetManager(this)?.Cancel(AppNotifications.AlarmNotificationId);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);

            StopSelf();
        }

        /// <inheritdoc/>
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }
    }
}
