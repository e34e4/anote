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
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using ImageButton = Android.Widget.ImageButton;
using Uri = Android.Net.Uri;

namespace SilentNotes
{
    /// <summary>
    /// Image viewer with left/right swipe to browse other images in the same folder.
    /// Opens fullscreen by default (Player gallery), or as a dialog-sized floating window
    /// when <see cref="ExtraFloating"/> is set (e.g. after a TG photo download).
    /// </summary>
    [Activity(
        Label = "aNote Gallery",
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        Exported = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/*",
        DataScheme = "content")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/*",
        DataScheme = "file")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/jpeg",
        DataScheme = "content")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/jpeg",
        DataScheme = "file")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/png",
        DataScheme = "content")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/png",
        DataScheme = "file")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/webp",
        DataScheme = "content")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/webp",
        DataScheme = "file")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/gif",
        DataScheme = "content")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "image/gif",
        DataScheme = "file")]
    public class ImageViewerActivity : Activity, GestureDetector.IOnGestureListener
    {
        public const string ExtraUri = "silentnotes.image.uri";
        public const string ExtraPath = "silentnotes.image.path";
        public const string ExtraTitle = "silentnotes.image.title";
        public const string ExtraPaths = "silentnotes.image.paths";
        public const string ExtraIndex = "silentnotes.image.index";
        public const string ExtraFloating = "silentnotes.image.floating";

        private ZoomableImageView _imageView;
        private TextView _titleView;
        private TextView _counterView;
        private GestureDetector _gestureDetector;
        private string[] _paths = Array.Empty<string>();
        private int _index;
        private bool _floating;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            // Must be requested before base.OnCreate / content; Dialog theme already has no title.
            _floating = Intent?.GetBooleanExtra(ExtraFloating, false) ?? false;
            base.OnCreate(savedInstanceState);
            if (_floating)
                ConfigureFloatingWindow();
            else
                ConfigureFullscreenWindow();

            _gestureDetector = new GestureDetector(this, this);

            FrameLayout root = new FrameLayout(this);
            root.SetBackgroundColor(Color.Black);
            _imageView = new ZoomableImageView(this);
            _imageView.LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);
            _imageView.Touch += (_, e) =>
            {
                _imageView.HandleTouch(e.Event);
                if (_imageView.IsAtDefaultZoom)
                    _gestureDetector.OnTouchEvent(e.Event);
                e.Handled = true;
            };

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

            var editButton = new Android.Widget.Button(this)
            {
                Text = "Edit",
                TextSize = 13,
            };
            editButton.SetTextColor(Color.White);
            editButton.SetBackgroundColor(Color.Argb(160, 36, 129, 204));
            FrameLayout.LayoutParams editLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent);
            editLp.Gravity = GravityFlags.Top | GravityFlags.Right;
            editLp.SetMargins(0, 12, 120, 0);
            editButton.LayoutParameters = editLp;
            editButton.Click += (_, __) => OpenEditor();

            root.AddView(_imageView);
            root.AddView(_titleView);
            root.AddView(_counterView);
            root.AddView(editButton);
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

            if (_paths.Length == 0 && Intent?.Data != null)
                _paths = new[] { Intent.Data.ToString() };

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
            if (e1 == null || e2 == null || _paths.Length <= 1 || !_imageView.IsAtDefaultZoom)
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
            _titleView.Text = ResolveDisplayTitle(pathOrUri);
            _counterView.Text = _paths.Length > 1
                ? string.Format("{0} / {1}   ·   swipe for next/prev", _index + 1, _paths.Length)
                : string.Empty;

            try
            {
                bool loaded = false;
                if (pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
                {
                    var androidUri = Uri.Parse(pathOrUri);
                    using (var stream = ContentResolver?.OpenInputStream(androidUri))
                    {
                        if (stream != null)
                        {
                            Bitmap bitmap = BitmapFactory.DecodeStream(stream);
                            if (bitmap != null)
                            {
                                _imageView.SetImageBitmap(bitmap);
                                loaded = true;
                            }
                        }
                    }

                    if (!loaded)
                        _imageView.SetImageURI(androidUri);
                }
                else if (pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    _imageView.SetImageURI(Uri.Parse(pathOrUri));
                }
                else if (System.IO.File.Exists(pathOrUri))
                {
                    // Prefer decode-from-path over file:// URIs (more reliable right after a download).
                    Bitmap bitmap = BitmapFactory.DecodeFile(pathOrUri);
                    if (bitmap != null)
                        _imageView.SetImageBitmap(bitmap);
                    else
                        _imageView.SetImageURI(Uri.FromFile(new Java.IO.File(pathOrUri)));
                }
                else
                {
                    Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                }

                _imageView.Post(() => _imageView.ResetTransform());
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Cannot open image: " + ex.Message, ToastLength.Long).Show();
            }
        }

        private string ResolveDisplayTitle(string pathOrUri)
        {
            string fromExtra = Intent?.GetStringExtra(ExtraTitle);
            if (!string.IsNullOrWhiteSpace(fromExtra) && _paths.Length == 1)
                return fromExtra;

            if (pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var androidUri = Uri.Parse(pathOrUri);
                    using var cursor = ContentResolver?.Query(androidUri, null, null, null, null);
                    if (cursor != null && cursor.MoveToFirst())
                    {
                        int nameIndex = cursor.GetColumnIndex(global::Android.Provider.OpenableColumns.DisplayName);
                        if (nameIndex >= 0)
                        {
                            string name = cursor.GetString(nameIndex);
                            if (!string.IsNullOrWhiteSpace(name))
                                return name;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            string fileName = System.IO.Path.GetFileName(pathOrUri);
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;

            return "Image";
        }

        private void ConfigureFullscreenWindow()
        {
            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            WindowManagerLayoutParams lp = Window?.Attributes;
            if (lp != null)
            {
                lp.Width = ViewGroup.LayoutParams.MatchParent;
                lp.Height = ViewGroup.LayoutParams.MatchParent;
                Window.Attributes = lp;
            }
        }

        private void ConfigureFloatingWindow()
        {
            if (Window == null)
                return;

            SetFinishOnTouchOutside(true);
            Window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            Window.AddFlags(WindowManagerFlags.DimBehind);
            Window.ClearFlags(WindowManagerFlags.Fullscreen);

            WindowManagerLayoutParams lp = Window.Attributes;
            if (lp != null)
            {
                DisplayMetrics metrics = Resources.DisplayMetrics;
                lp.DimAmount = 0.55f;
                lp.Width = (int)(metrics.WidthPixels * 0.94f);
                lp.Height = (int)(metrics.HeightPixels * 0.72f);
                lp.Gravity = GravityFlags.Center;
                Window.Attributes = lp;
            }
        }

        private void OpenEditor()
        {
            try
            {
                string pathOrUri = _paths[_index];
                string path = pathOrUri;
                if (pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    && System.Uri.TryCreate(pathOrUri, UriKind.Absolute, out System.Uri fileUri))
                {
                    path = fileUri.LocalPath;
                }

                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                {
                    Toast.MakeText(this, "Edit needs a local media file", ToastLength.Short).Show();
                    return;
                }

                if (IsVideoFile(path))
                    StartActivity(VideoEditorActivity.CreateIntent(this, path, _floating));
                else
                    StartActivity(ImageEditorActivity.CreateIntent(this, path, _floating));
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, ex.Message, ToastLength.Long).Show();
            }
        }

        private static bool IsVideoFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext is ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" or ".3gp" or ".3gpp" or ".m4v" or ".wmv" or ".flv" or ".ts" or ".mpeg" or ".mpg";
        }

        /// <summary>
        /// Starts the image viewer for a path, optionally with sibling images for swipe.
        /// Pass <paramref name="floating"/> to open as a dialog-sized overlay instead of fullscreen.
        /// </summary>
        public static void Start(
            Context context,
            string pathOrUri,
            string title,
            IList<string> galleryPaths = null,
            int index = 0,
            bool floating = false)
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
            intent.PutExtra(ExtraFloating, floating);
            context.StartActivity(intent);
        }
    }
}
