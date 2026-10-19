// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using ImageButton = Android.Widget.ImageButton;
using Uri = Android.Net.Uri;
using WidgetOrientation = Android.Widget.Orientation;

namespace SilentNotes
{
    /// <summary>
    /// Fullscreen video player used by the Player tab. Supports file paths and content URIs.
    /// </summary>
    [Activity(
        Label = "aNote Player",
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.Sensor)]
    public class VideoPlayerActivity : Activity, MediaPlayer.IOnPreparedListener, MediaPlayer.IOnCompletionListener, MediaPlayer.IOnErrorListener
    {
        public const string ExtraUri = "silentnotes.video.uri";
        public const string ExtraPath = "silentnotes.video.path";
        public const string ExtraTitle = "silentnotes.video.title";

        private VideoView _videoView;
        private TextView _titleView;
        private ImageButton _playPauseButton;
        private SeekBar _seekBar;
        private TextView _timeView;
        private bool _trackingSeek;
        private readonly Handler _handler = new Handler(Looper.MainLooper);
        private Action _tickAction;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            Window?.AddFlags(WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn);

            LinearLayout root = new LinearLayout(this)
            {
                Orientation = WidgetOrientation.Vertical,
            };
            root.SetBackgroundColor(Color.Black);
            root.LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);

            _titleView = new TextView(this)
            {
                TextSize = 16,
                Ellipsize = Android.Text.TextUtils.TruncateAt.End,
            };
            _titleView.SetSingleLine(true);
            _titleView.SetTextColor(Color.White);
            _titleView.SetPadding(24, 24, 24, 8);
            _titleView.Text = Intent?.GetStringExtra(ExtraTitle) ?? "Video";

            FrameLayout videoFrame = new FrameLayout(this);
            videoFrame.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f);

            _videoView = new VideoView(this);
            FrameLayout.LayoutParams videoLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);
            videoLp.Gravity = GravityFlags.Center;
            _videoView.LayoutParameters = videoLp;
            videoFrame.AddView(_videoView);

            LinearLayout controls = new LinearLayout(this)
            {
                Orientation = WidgetOrientation.Horizontal,
            };
            controls.SetPadding(16, 12, 16, 24);
            controls.SetGravity(GravityFlags.CenterVertical);

            _playPauseButton = new ImageButton(this);
            _playPauseButton.SetBackgroundColor(Color.Transparent);
            _playPauseButton.SetImageResource(global::Android.Resource.Drawable.IcMediaPause);
            _playPauseButton.Click += (_, __) => TogglePlayPause();

            _seekBar = new SeekBar(this);
            _seekBar.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            _seekBar.Max = 1000;
            _seekBar.StartTrackingTouch += (_, __) => _trackingSeek = true;
            _seekBar.StopTrackingTouch += (_, __) =>
            {
                _trackingSeek = false;
                if (_videoView.Duration > 0)
                {
                    int pos = (int)(_seekBar.Progress / 1000.0 * _videoView.Duration);
                    _videoView.SeekTo(pos);
                }
            };

            _timeView = new TextView(this)
            {
                TextSize = 12,
            };
            _timeView.SetTextColor(Color.LightGray);
            _timeView.SetPadding(12, 0, 0, 0);
            _timeView.Text = "0:00 / 0:00";

            ImageButton closeButton = new ImageButton(this);
            closeButton.SetBackgroundColor(Color.Transparent);
            closeButton.SetImageResource(global::Android.Resource.Drawable.IcMenuCloseClearCancel);
            closeButton.Click += (_, __) => Finish();

            controls.AddView(_playPauseButton);
            controls.AddView(_seekBar);
            controls.AddView(_timeView);
            controls.AddView(closeButton);

            root.AddView(_titleView);
            root.AddView(videoFrame);
            root.AddView(controls);
            SetContentView(root);

            _videoView.SetOnPreparedListener(this);
            _videoView.SetOnCompletionListener(this);
            _videoView.SetOnErrorListener(this);

            string uriText = Intent?.GetStringExtra(ExtraUri);
            string path = Intent?.GetStringExtra(ExtraPath);
            try
            {
                if (!string.IsNullOrEmpty(uriText))
                    _videoView.SetVideoURI(Uri.Parse(uriText));
                else if (!string.IsNullOrEmpty(path))
                    _videoView.SetVideoPath(path);
                else
                {
                    Toast.MakeText(this, "No video to play", ToastLength.Short).Show();
                    Finish();
                    return;
                }
                _videoView.Start();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Playback failed: " + ex.Message, ToastLength.Long).Show();
                Finish();
            }

            _tickAction = Tick;
            _handler.PostDelayed(_tickAction, 400);
        }

        public void OnPrepared(MediaPlayer mp)
        {
            mp?.SetVideoScalingMode(VideoScalingMode.ScaleToFit);
            UpdatePlayPauseIcon();
            Tick();
        }

        public void OnCompletion(MediaPlayer mp)
        {
            UpdatePlayPauseIcon();
            Tick();
        }

        public bool OnError(MediaPlayer mp, MediaError what, int extra)
        {
            Toast.MakeText(this, "Cannot play this video format on this device", ToastLength.Long).Show();
            return true;
        }

        private void TogglePlayPause()
        {
            if (_videoView.IsPlaying)
                _videoView.Pause();
            else
                _videoView.Start();
            UpdatePlayPauseIcon();
        }

        private void UpdatePlayPauseIcon()
        {
            _playPauseButton.SetImageResource(
                _videoView.IsPlaying
                    ? global::Android.Resource.Drawable.IcMediaPause
                    : global::Android.Resource.Drawable.IcMediaPlay);
        }

        private void Tick()
        {
            try
            {
                int duration = Math.Max(0, _videoView.Duration);
                int position = Math.Max(0, _videoView.CurrentPosition);
                if (!_trackingSeek && duration > 0)
                    _seekBar.Progress = (int)(position * 1000.0 / duration);
                _timeView.Text = FormatMs(position) + " / " + FormatMs(duration);
                UpdatePlayPauseIcon();
            }
            catch (Exception)
            {
            }
            _handler.PostDelayed(_tickAction, 400);
        }

        private static string FormatMs(int ms)
        {
            TimeSpan t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            if (t.TotalHours >= 1)
                return string.Format("{0}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format("{0}:{1:D2}", (int)t.TotalMinutes, t.Seconds);
        }

        protected override void OnDestroy()
        {
            _handler.RemoveCallbacksAndMessages(null);
            try
            {
                _videoView?.StopPlayback();
            }
            catch (Exception)
            {
            }
            base.OnDestroy();
        }

        /// <summary>Starts the fullscreen video player for a path or content URI.</summary>
        public static void Start(Context context, string pathOrUri, string title)
        {
            Intent intent = new Intent(context, typeof(VideoPlayerActivity));
            // Stay in the same task as MainActivity so back returns to the Player tab.
            if (!(context is Activity))
                intent.AddFlags(ActivityFlags.NewTask);
            if (!string.IsNullOrEmpty(pathOrUri) &&
                (pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                 pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
            {
                intent.PutExtra(ExtraUri, pathOrUri);
            }
            else
            {
                intent.PutExtra(ExtraPath, pathOrUri);
            }
            intent.PutExtra(ExtraTitle, title ?? "Video");
            context.StartActivity(intent);
        }
    }
}
