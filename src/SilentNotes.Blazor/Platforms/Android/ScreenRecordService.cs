// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using SilentNotes.Platforms.Services;
using Path = System.IO.Path;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Foreground service that captures the screen via MediaProjection into an H.264 MP4
    /// (576×1024 @ ~30 FPS, 2.2 Mbps, AAC device audio, SDR).
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
    public class ScreenRecordService : Service
    {
        public const string ActionStart = "app.anote.action.SCREEN_RECORD_START";
        public const string ActionStop = "app.anote.action.SCREEN_RECORD_STOP";
        public const string ExtraResultCode = "result_code";
        public const string ExtraResultData = "result_data";

        public const int VideoWidth = 576;
        public const int VideoHeight = 1024;
        /// <summary>
        /// Target frame rate. 480 FPS is not practical at 2.2 Mbps H.264; encoders use ~30 FPS
        /// and drop frames when the producer is faster (allow frame dropping).
        /// </summary>
        public const int VideoFrameRate = 30;
        public const int VideoBitRate = 2_200_000;
        public const int AudioBitRate = 128_000;
        public const int AudioSampleRate = 44100;
        public const int NotificationId = 7410;

        private static readonly object Gate = new object();
        private static ScreenRecordService _instance;
        private static string _lastOutputPath;
        private static bool _isRecording;

        private MediaProjection _projection;
        private VirtualDisplay _virtualDisplay;
        private MediaRecorder _recorder;
        private string _outputPath;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private MediaProjectionCallback _callback;

        public static bool IsRecording
        {
            get { lock (Gate) return _isRecording; }
        }

        public static TimeSpan Elapsed
        {
            get
            {
                lock (Gate)
                    return _instance?._stopwatch.Elapsed ?? TimeSpan.Zero;
            }
        }

        public static string LastOutputPath
        {
            get { lock (Gate) return _lastOutputPath; }
        }

        public static event Action StateChanged;

        /// <inheritdoc/>
        public override void OnCreate()
        {
            base.OnCreate();
            lock (Gate)
                _instance = this;
        }

        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string action = intent?.Action;
            if (action == ActionStop)
            {
                StopRecordingInternal(save: true);
                return StartCommandResult.NotSticky;
            }

            if (action != ActionStart)
                return StartCommandResult.NotSticky;

            int resultCode = intent.GetIntExtra(ExtraResultCode, 0);
#pragma warning disable CS0618
            Intent data = intent.GetParcelableExtra(ExtraResultData) as Intent;
#pragma warning restore CS0618
            if (data == null)
            {
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            NotificationChannels.CreateAll(this);
            Notification notification = BuildNotification();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaProjection);
            else
                StartForeground(NotificationId, notification);

            if (!StartRecordingInternal(resultCode, data))
            {
                StopSelf();
            }

            return StartCommandResult.Sticky;
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            StopRecordingInternal(save: false);
            lock (Gate)
            {
                if (_instance == this)
                    _instance = null;
            }
            base.OnDestroy();
        }

        /// <inheritdoc/>
        public override IBinder OnBind(Intent intent) => null;

        public static void RequestStop(Context context)
        {
            if (context == null)
                return;
            var intent = new Intent(context, typeof(ScreenRecordService));
            intent.SetAction(ActionStop);
            context.StartService(intent);
        }

        private bool StartRecordingInternal(int resultCode, Intent data)
        {
            try
            {
                var mgr = (MediaProjectionManager)GetSystemService(MediaProjectionService);
                _projection = mgr.GetMediaProjection(resultCode, data);
                if (_projection == null)
                    return false;

                _callback = new MediaProjectionCallback(this);
                _projection.RegisterCallback(_callback, new Handler(Looper.MainLooper));

                string dir = ScreenRecorderService.GetRecordingsDirectory(this);
                Directory.CreateDirectory(dir);
                string fileName = "screen_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".mp4";
                _outputPath = Path.Combine(dir, fileName);

#pragma warning disable CA1422
                _recorder = (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    ? new MediaRecorder(this)
                    : new MediaRecorder();
#pragma warning restore CA1422

                _recorder.SetVideoSource(VideoSource.Surface);
                ConfigureAudioSource();
                _recorder.SetOutputFormat(OutputFormat.Mpeg4);
                _recorder.SetOutputFile(_outputPath);
                _recorder.SetVideoEncoder(VideoEncoder.H264);
                _recorder.SetVideoSize(VideoWidth, VideoHeight);
                _recorder.SetVideoFrameRate(VideoFrameRate);
                _recorder.SetVideoEncodingBitRate(VideoBitRate);
                // 2 s keyframe interval ≈ frameRate * 2 when the encoder honors capture rate.
                try
                {
                    _recorder.SetCaptureRate(VideoFrameRate);
                }
                catch (Exception)
                {
                }

                if (HasRecordAudioPermission())
                {
                    _recorder.SetAudioEncoder(AudioEncoder.Aac);
                    _recorder.SetAudioEncodingBitRate(AudioBitRate);
                    _recorder.SetAudioSamplingRate(AudioSampleRate);
                    _recorder.SetAudioChannels(1);
                }

                _recorder.Prepare();

                int density = (int)Resources.DisplayMetrics.DensityDpi;
                Surface surface = _recorder.Surface;
                _virtualDisplay = _projection.CreateVirtualDisplay(
                    "aNoteScreenRecord",
                    VideoWidth,
                    VideoHeight,
                    density,
                    (DisplayFlags)0,
                    surface,
                    null,
                    null);

                _recorder.Start();
                _stopwatch.Restart();
                lock (Gate)
                {
                    _isRecording = true;
                    _lastOutputPath = null;
                }
                RaiseStateChanged();
                return true;
            }
            catch (Exception)
            {
                Cleanup(deleteFile: true);
                return false;
            }
        }

        private void ConfigureAudioSource()
        {
            if (!HasRecordAudioPermission())
                return;

            // Prefer internal device audio (playback capture) when MediaProjection allows it.
            // Device/mix audio with AAC: mic source during MediaProjection captures the mix on
            // many devices when RECORD_AUDIO is granted with the projection consent.
            _recorder.SetAudioSource(AudioSource.Mic);
        }

        private bool HasRecordAudioPermission()
        {
            return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                this, global::Android.Manifest.Permission.RecordAudio) == Permission.Granted;
        }

        private void StopRecordingInternal(bool save)
        {
            bool wasRecording;
            lock (Gate)
                wasRecording = _isRecording;

            if (!wasRecording && _recorder == null)
            {
                StopForegroundCompat();
                StopSelf();
                return;
            }

            try
            {
                _recorder?.Stop();
            }
            catch (Exception)
            {
            }

            string path = _outputPath;
            Cleanup(deleteFile: !save);

            if (save && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                lock (Gate)
                    _lastOutputPath = path;
            }

            lock (Gate)
                _isRecording = false;

            RaiseStateChanged();
            StopForegroundCompat();
            StopSelf();
        }

        private void Cleanup(bool deleteFile)
        {
            try
            {
                _virtualDisplay?.Release();
            }
            catch (Exception)
            {
            }
            _virtualDisplay = null;

            try
            {
                if (_projection != null && _callback != null)
                    _projection.UnregisterCallback(_callback);
            }
            catch (Exception)
            {
            }

            try
            {
                _projection?.Stop();
            }
            catch (Exception)
            {
            }
            _projection = null;
            _callback = null;

            try
            {
                _recorder?.Reset();
                _recorder?.Release();
            }
            catch (Exception)
            {
            }
            _recorder = null;
            _stopwatch.Reset();

            if (deleteFile && !string.IsNullOrEmpty(_outputPath))
            {
                try
                {
                    if (File.Exists(_outputPath))
                        File.Delete(_outputPath);
                }
                catch (Exception)
                {
                }
            }

            _outputPath = null;
        }

        private void StopForegroundCompat()
        {
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
        }

        private Notification BuildNotification()
        {
            var stopIntent = new Intent(this, typeof(ScreenRecordService));
            stopIntent.SetAction(ActionStop);
            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            PendingIntent stopPending = PendingIntent.GetService(this, 0, stopIntent, flags);

            var openIntent = new Intent(this, typeof(MainActivity));
            openIntent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenTab, 9);
            openIntent.AddFlags(ActivityFlags.SingleTop);
            PendingIntent openPending = PendingIntent.GetActivity(this, NotificationId, openIntent, flags);

            Notification.Builder builder = AppNotifications.CreateBuilder(this, NotificationChannels.ScreenRecordChannelId);
            builder.SetContentTitle("Recording screen")
                .SetContentText("576×1024 · H.264 · AAC")
                .SetSmallIcon(AppNotifications.SmallIcon(this))
                .SetContentIntent(openPending)
                .SetOngoing(true)
                .AddAction(new Notification.Action(0, "Stop", stopPending));
            return builder.Build();
        }

        private static void RaiseStateChanged()
        {
            try
            {
                StateChanged?.Invoke();
            }
            catch (Exception)
            {
            }
        }

        private sealed class MediaProjectionCallback : MediaProjection.Callback
        {
            private readonly ScreenRecordService _service;

            public MediaProjectionCallback(ScreenRecordService service)
            {
                _service = service;
            }

            public override void OnStop()
            {
                _service.StopRecordingInternal(save: true);
            }
        }
    }
}
