// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IClockService"/> interface for the Android platform. It
    /// schedules the alarms and timers with the AlarmManager, so they fire even when the app is
    /// closed or the device was rebooted.
    /// </summary>
    internal class ClockService : ClockServiceBase
    {
        private const int TimerRequestCode = 7600;
        private readonly IAppContextService _appContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClockService"/> class.
        /// </summary>
        public ClockService(IAppContextService appContextService, IXmlFileService xmlFileService)
            : base(xmlFileService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        protected override string GetLocation()
        {
            return ContextCompat.GetNoBackupFilesDir(_appContext.Context).AbsolutePath;
        }

        /// <inheritdoc/>
        public override bool IsSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public override bool HasNotificationPermission
        {
            get
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
                    return true;
                Context context = _appContext.Context;
                return (context != null)
                    && (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.PostNotifications) == Permission.Granted);
            }
        }

        /// <inheritdoc/>
        public override async Task<bool> RequestNotificationPermissionAsync()
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
                    return true;
                PermissionStatus status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>());
                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public override bool CanScheduleExactAlarms
        {
            get
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.S)
                    return true;
                AlarmManager manager = GetAlarmManager();
                return manager?.CanScheduleExactAlarms() ?? false;
            }
        }

        /// <inheritdoc/>
        public override Task<bool> RequestExactAlarmPermissionAsync()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                {
                    var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm);
                    intent.SetData(global::Android.Net.Uri.Parse("package:" + _appContext.Context.PackageName));
                    intent.AddFlags(ActivityFlags.NewTask);
                    _appContext.Context.StartActivity(intent);
                }
            }
            catch (Exception)
            {
            }
            return Task.FromResult(CanScheduleExactAlarms);
        }

        /// <inheritdoc/>
        public override Task ScheduleAlarmAsync(AlarmModel alarm)
        {
            if ((alarm == null) || !alarm.Enabled)
                return Task.CompletedTask;

            AlarmManager manager = GetAlarmManager();
            if (manager == null)
                return Task.CompletedTask;

            DateTime nextLocal = alarm.GetNextOccurrence(DateTime.Now);
            long triggerMs = new DateTimeOffset(nextLocal.ToUniversalTime()).ToUnixTimeMilliseconds();
            int requestCode = RequestCodeOf(alarm.Id);
            PendingIntent operation = BuildAlarmOperation(alarm.Id, alarm.Label, requestCode);

            if (CanScheduleExactAlarms)
            {
                PendingIntent showIntent = PendingIntent.GetActivity(
                    _appContext.Context, requestCode, BuildShowAlarmIntent(), Flags());
                manager.SetAlarmClock(new AlarmManager.AlarmClockInfo(triggerMs, showIntent), operation);
            }
            else
            {
                manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, operation);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task CancelAlarmAsync(Guid alarmId)
        {
            AlarmManager manager = GetAlarmManager();
            if (manager == null)
                return Task.CompletedTask;

            PendingIntent operation = BuildAlarmOperation(alarmId, null, RequestCodeOf(alarmId));
            manager.Cancel(operation);
            operation.Cancel();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task ScheduleTimerAsync(Guid timerId, DateTime fireAtUtc, int totalSeconds, string label)
        {
            AlarmManager manager = GetAlarmManager();
            if (manager == null)
                return Task.CompletedTask;

            DateTime fireUtc = DateTime.SpecifyKind(fireAtUtc, DateTimeKind.Utc);
            long triggerMs = new DateTimeOffset(fireUtc).ToUnixTimeMilliseconds();
            PendingIntent operation = BuildTimerOperation(totalSeconds, label);

            if (CanScheduleExactAlarms)
                manager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, operation);
            else
                manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, operation);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task CancelTimerAsync(Guid timerId)
        {
            AlarmManager manager = GetAlarmManager();
            if (manager == null)
                return Task.CompletedTask;

            PendingIntent operation = BuildTimerOperation(0, null);
            manager.Cancel(operation);
            operation.Cancel();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task RescheduleAllAsync()
        {
            try
            {
                ClockDataModel data = LoadData();
                foreach (AlarmModel alarm in data.Alarms)
                {
                    if (alarm.Enabled)
                        _ = ScheduleAlarmAsync(alarm);
                }

                if ((data.TimerId != Guid.Empty) && (data.TimerEndUtc > DateTime.UtcNow))
                    _ = ScheduleTimerAsync(data.TimerId, data.TimerEndUtc, data.TimerSeconds, data.TimerLabel);
            }
            catch (Exception)
            {
            }
            return Task.CompletedTask;
        }

        private AlarmManager GetAlarmManager()
        {
            return (AlarmManager)_appContext?.Context?.GetSystemService(Context.AlarmService);
        }

        private PendingIntent BuildAlarmOperation(Guid alarmId, string label, int requestCode)
        {
            var intent = new Intent(_appContext.Context, typeof(AlarmReceiver));
            intent.SetAction("app.anote.action.ALARM." + alarmId.ToString("N"));
            intent.PutExtra(AlarmReceiver.ExtraKind, AlarmReceiver.KindAlarm);
            intent.PutExtra(AlarmReceiver.ExtraId, alarmId.ToString());
            intent.PutExtra(AlarmReceiver.ExtraLabel, label);
            return PendingIntent.GetBroadcast(_appContext.Context, requestCode, intent, Flags());
        }

        private PendingIntent BuildTimerOperation(int totalSeconds, string label)
        {
            var intent = new Intent(_appContext.Context, typeof(AlarmReceiver));
            intent.SetAction("app.anote.action.TIMER");
            intent.PutExtra(AlarmReceiver.ExtraKind, AlarmReceiver.KindTimer);
            intent.PutExtra(AlarmReceiver.ExtraSeconds, totalSeconds);
            intent.PutExtra(AlarmReceiver.ExtraLabel, label);
            return PendingIntent.GetBroadcast(_appContext.Context, TimerRequestCode, intent, Flags());
        }

        private Intent BuildShowAlarmIntent()
        {
            var intent = new Intent(_appContext.Context, typeof(MainActivity));
            intent.SetAction(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            intent.PutExtra(AlarmRingingService.ExtraOpenClock, true);
            intent.AddFlags(ActivityFlags.SingleTop);
            return intent;
        }

        private static PendingIntentFlags Flags()
        {
            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            return flags;
        }

        private static int RequestCodeOf(Guid id)
        {
            byte[] bytes = id.ToByteArray();
            return BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
        }
    }
}
