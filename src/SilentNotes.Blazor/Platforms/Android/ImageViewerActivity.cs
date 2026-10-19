// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using ImageButton = Android.Widget.ImageButton;
using Uri = Android.Net.Uri;

namespace SilentNotes
{
    /// <summary>
    /// Fullscreen image viewer with left/right swipe to browse other images in the same folder.
    /// Opened as a child of MainActivity (same task) so back/close returns to the Player tab.
    /// </summary>
    [Activity(
        Label = "aNote Gallery",
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class ImageViewerActivity : Activity, GestureDetector.IOnGestureListener
    {
        public const string ExtraUri = "silentnotes.image.uri";
        public const string ExtraPath = "silentnotes.image.path";
        public const string ExtraTitle = "silentnotes.image.title";
        public const string ExtraPaths = "silentnotes.image.paths";
        public const string ExtraIndex = "silentnotes.image.index";

        private ImageView _imageView;
        private TextView _titleView;
        private TextView _counterView;
        private GestureDetector _gestureDetector;
        private string[] _paths = Array.Empty<string>();
        private int _index;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            Window?.AddFlags(WindowManagerFlags.Fullscreen);

            _gestureDetector = new GestureDetector(this, this);

            FrameLayout root = new FrameLayout(this);
            root.SetBackgroundColor(Color.Black);
            root.Touch += (_, e) =>
            {
                _gestureDetector.OnTouchEvent(e.Event);
                e.Handled = true;
            };

            _imageView = new ImageView(this);
            _imageView.SetScaleType(ImageView.ScaleType.FitCenter);
            _imageView.LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);

            _titleView = new TextView(this)
            {
                TextSize = 15,
                Ellipsize = Android.Text.TextUtils.TruncateAt.End,
            };
            _titleView.SetSingleLine(true);
            _titleView.SetTextColor(Color.White);
            _titleView.SetPadding(24, 24, 120, 8);
            FrameLayout.LayoutParams titleLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            titleLp.Gravity = GravityFlags.Top;
            _titleView.LayoutParameters = titleLp;

            _counterView = new TextView(this)
            {
                TextSize = 13,
            };
            _counterView.SetTextColor(Color.LightGray);
            _counterView.SetPadding(24, 0, 24, 24);
            FrameLayout.LayoutParams counterLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            counterLp.Gravity = GravityFlags.Bottom;
            _counterView.LayoutParameters = counterLp;

            ImageButton closeButton = new ImageButton(this);
            closeButton.SetBackgroundColor(Color.Transparent);
            closeButton.SetImageResource(global::Android.Resource.Drawable.IcMenuCloseClearCancel);
            FrameLayout.LayoutParams closeLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent);
            closeLp.Gravity = GravityFlags.Top | GravityFlags.Right;
            closeLp.SetMargins(0, 12, 12, 0);
            closeButton.LayoutParameters = closeLp;
            closeButton.Click += (_, __) => Finish();

            root.AddView(_imageView);
            root.AddView(_titleView);
            root.AddView(_counterView);
            root.AddView(closeButton);
            SetContentView(root);

            _paths = Intent?.GetStringArrayExtra(ExtraPaths) ?? Array.Empty<string>();
            _index = Intent?.GetIntExtra(ExtraIndex, 0) ?? 0;

            if (_paths.Length == 0)
            {
                string uriText = Intent?.GetStringExtra(ExtraUri);
                string path = Intent?.GetStringExtra(ExtraPath);
                string single = !string.IsNullOrEmpty(path) ? path : uriText;
                if (!string.IsNullOrEmpty(single))
                    _paths = new[] { single };
            }

            if (_paths.Length == 0)
            {
                Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                Finish();
                return;
            }

            _index = Math.Clamp(_index, 0, _paths.Length - 1);
            ShowCurrent();
        }

        /// <inheritdoc/>
        public override bool OnTouchEvent(MotionEvent e)
        {
            _gestureDetector.OnTouchEvent(e);
            return base.OnTouchEvent(e);
        }

        public bool OnDown(MotionEvent e) => true;

        public bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
        {
            if (e1 == null || e2 == null || _paths.Length <= 1)
                return false;

            float dx = e2.GetX() - e1.GetX();
            float dy = e2.GetY() - e1.GetY();
            if (Math.Abs(dx) < 80 || Math.Abs(dx) < Math.Abs(dy) * 1.2f)
                return false;

            if (dx < 0)
                ShowAt(_index + 1); // swipe left → next
            else
                ShowAt(_index - 1); // swipe right → previous
            return true;
        }

        public void OnLongPress(MotionEvent e)
        {
        }

        public bool OnScroll(MotionEvent e1, MotionEvent e2, float distanceX, float distanceY) => false;

        public void OnShowPress(MotionEvent e)
        {
        }

        public bool OnSingleTapUp(MotionEvent e) => false;

        private void ShowAt(int index)
        {
            if (index < 0 || index >= _paths.Length)
                return;
            _index = index;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            string pathOrUri = _paths[_index];
            _titleView.Text = System.IO.Path.GetFileName(pathOrUri) ?? "Image";
            _counterView.Text = _paths.Length > 1
                ? string.Format("{0} / {1}   ·   swipe for next/prev", _index + 1, _paths.Length)
                : string.Empty;

            try
            {
                if (pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                    pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    _imageView.SetImageURI(Uri.Parse(pathOrUri));
                }
                else if (System.IO.File.Exists(pathOrUri))
                {
                    _imageView.SetImageURI(Uri.FromFile(new Java.IO.File(pathOrUri)));
                }
                else
                {
                    Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                }
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Cannot open image: " + ex.Message, ToastLength.Long).Show();
            }
        }

        /// <summary>
        /// Starts the fullscreen image viewer for a path, optionally with sibling images for swipe.
        /// </summary>
        public static void Start(Context context, string pathOrUri, string title, IList<string> galleryPaths = null, int index = 0)
        {
            Intent intent = new Intent(context, typeof(ImageViewerActivity));
            // Stay in the same task as MainActivity so back returns to the Player tab.
            if (!(context is Activity))
                intent.AddFlags(ActivityFlags.NewTask);

            string[] paths = galleryPaths?.Where(p => !string.IsNullOrEmpty(p)).ToArray() ?? Array.Empty<string>();
            if (paths.Length == 0 && !string.IsNullOrEmpty(pathOrUri))
                paths = new[] { pathOrUri };

            if (paths.Length > 0)
            {
                intent.PutExtra(ExtraPaths, paths);
                int startIndex = index;
                if (startIndex < 0 || startIndex >= paths.Length)
                {
                    startIndex = Array.FindIndex(paths, p =>
                        string.Equals(p, pathOrUri, StringComparison.OrdinalIgnoreCase));
                    if (startIndex < 0)
                        startIndex = 0;
                }
                intent.PutExtra(ExtraIndex, startIndex);
            }

            if (!string.IsNullOrEmpty(pathOrUri) &&
                (pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                 pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
            {
                intent.PutExtra(ExtraUri, pathOrUri);
            }
            else if (!string.IsNullOrEmpty(pathOrUri))
            {
                intent.PutExtra(ExtraPath, pathOrUri);
            }

            intent.PutExtra(ExtraTitle, title ?? "Image");
            context.StartActivity(intent);
        }
    }
}
