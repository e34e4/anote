// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Foreground service which keeps audio playing when the app is in the background and shows a
    /// notification with play/pause, next, previous and stop actions.
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
    public class MediaPlaybackService : Service
    {
        /// <summary>Action which updates the notification with the current player state.</summary>
        public const string ActionUpdate = "app.anote.action.MEDIA_UPDATE";

        /// <summary>Action which hides the notification and stops the service.</summary>
        public const string ActionStop = "app.anote.action.MEDIA_STOP";

        /// <summary>Action which toggles play/pause.</summary>
        public const string ActionToggle = "app.anote.action.MEDIA_TOGGLE";

        /// <summary>Action which plays the next queue item.</summary>
        public const string ActionNext = "app.anote.action.MEDIA_NEXT";

        /// <summary>Action which plays the previous queue item.</summary>
        public const string ActionPrevious = "app.anote.action.MEDIA_PREVIOUS";

        private const string ExtraTitle = "anote_media_title";
        private const string ExtraPlaying = "anote_media_playing";
        private const string ExtraPaused = "anote_media_paused";

        private string _title = string.Empty;
        private bool _playing;
        private bool _paused;

        /// <summary>Gets a value indicating whether the foreground service is running.</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>Updates the notification for the current state, starting the service if needed.</summary>
        public static void NotifyState(Context context, string title, bool playing, bool paused)
        {
            if (context == null)
                return;
            try
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    Hide(context);
                    return;
                }

                var intent = new Intent(context, typeof(MediaPlaybackService));
                intent.SetAction(ActionUpdate);
                intent.PutExtra(ExtraTitle, title);
                intent.PutExtra(ExtraPlaying, playing);
                intent.PutExtra(ExtraPaused, paused);
                Start(context, intent);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Hides the media notification and stops the service.</summary>
        public static void Hide(Context context)
        {
            if (context == null || !IsRunning)
                return;
            try
            {
                var intent = new Intent(context, typeof(MediaPlaybackService));
                intent.SetAction(ActionStop);
                Start(context, intent);
            }
            catch (Exception)
            {
            }
        }

        private static void Start(Context context, Intent intent)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }

        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string action = intent?.Action ?? ActionUpdate;
            if (action == ActionStop)
            {
                StopMediaService();
                return StartCommandResult.NotSticky;
            }

            NotificationChannels.CreateAll(this);

            switch (action)
            {
                case ActionToggle:
                    TogglePlayback();
                    break;
                case ActionNext:
                    MediaPlaybackCommands.TryNext();
                    break;
                case ActionPrevious:
                    MediaPlaybackCommands.TryPrevious();
                    break;
            }

            if (action == ActionUpdate)
            {
                _title = intent?.GetStringExtra(ExtraTitle) ?? _title;
                _playing = intent?.GetBooleanExtra(ExtraPlaying, _playing) ?? _playing;
                _paused = intent?.GetBooleanExtra(ExtraPaused, _paused) ?? _paused;
            }
            else
            {
                ApplyStateFromPlayer();
            }

            if (string.IsNullOrWhiteSpace(_title))
            {
                StopMediaService();
                return StartCommandResult.NotSticky;
            }

            Notification notification = BuildNotification();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(AppNotifications.MediaNotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(AppNotifications.MediaNotificationId, notification);

            IsRunning = true;
            return StartCommandResult.NotSticky;
        }

        private void TogglePlayback()
        {
            try
            {
                IMediaPlayerService player = Ioc.Instance.GetService<IMediaPlayerService>();
                if (player == null)
                    return;
                if (player.IsPlaying)
                    player.Pause();
                else if (player.IsPaused)
                    player.Resume();
            }
            catch (Exception)
            {
            }
        }

        private void ApplyStateFromPlayer()
        {
            try
            {
                IMediaPlayerService player = Ioc.Instance.GetService<IMediaPlayerService>();
                if (player == null)
                    return;
                _title = player.ActiveTitle ?? _title;
                _playing = player.IsPlaying;
                _paused = player.IsPaused;
            }
            catch (Exception)
            {
            }
        }

        private Notification BuildNotification()
        {
            var builder = AppNotifications.CreateBuilder(this, NotificationChannels.MediaChannelId);
            builder.SetSmallIcon(AppNotifications.SmallIcon(this));
            builder.SetContentTitle(_title);
            builder.SetContentText(_playing ? "Playing" : (_paused ? "Paused" : "Stopped"));
            builder.SetOnlyAlertOnce(true);
            builder.SetShowWhen(false);
            builder.SetOngoing(_playing);
            builder.SetAutoCancel(false);
            builder.SetCategory(Notification.CategoryTransport);
            builder.SetVisibility(NotificationVisibility.Public);
            builder.SetContentIntent(AppNotifications.BuildOpenAppIntent(this, AppNotifications.MediaNotificationId));

            builder.AddAction(BuildAction(Android.Resource.Drawable.IcMediaPrevious, "Previous", ActionPrevious, 1));
            builder.AddAction(BuildAction(
                _playing ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay,
                _playing ? "Pause" : "Play",
                ActionToggle,
                2));
            builder.AddAction(BuildAction(Android.Resource.Drawable.IcMediaNext, "Next", ActionNext, 3));
            builder.AddAction(BuildAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", ActionStop, 4));

            return builder.Build();
        }

        private Notification.Action BuildAction(int icon, string title, string action, int requestCode)
        {
            var intent = new Intent(this, typeof(MediaPlaybackService));
            intent.SetAction(action);

            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            PendingIntent pendingIntent = PendingIntent.GetService(this, requestCode, intent, flags);
            return new Notification.Action.Builder(icon, title, pendingIntent).Build();
        }

        private void StopMediaService()
        {
            IsRunning = false;
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                    StopForeground(StopForegroundFlags.Remove);
                else
#pragma warning disable CS0618
                    StopForeground(true);
#pragma warning restore CS0618
            }
            catch (Exception)
            {
            }
            StopSelf();
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            IsRunning = false;
            base.OnDestroy();
        }

        /// <inheritdoc/>
        public override IBinder OnBind(Intent intent) => null;
    }
}
