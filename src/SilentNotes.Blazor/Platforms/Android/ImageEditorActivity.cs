// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.ExifInterface.Media;
using AButton = Android.Widget.Button;
using Color = Android.Graphics.Color;
using IOPath = System.IO.Path;
using MediaOrientation = Android.Media.Orientation;
using SysUri = System.Uri;

namespace SilentNotes
{
    /// <summary>
    /// Internal image editor: rotate, crop (center %), resize %, and basic EXIF edit/clear.
    /// </summary>
    [Activity(
        Label = "aNote Image Editor",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class ImageEditorActivity : Activity
    {
        public const string ExtraPath = "silentnotes.imageeditor.path";
        public const string ExtraUri = "silentnotes.imageeditor.uri";

        private ImageView _preview;
        private TextView _info;
        private string _path;
        private Bitmap _bitmap;
        private int _rotationDegrees;
        private int _cropPercent = 100;
        private int _resizePercent = 100;
        private string _exifDateTime = string.Empty;
        private bool _clearGps;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _path = Intent?.GetStringExtra(ExtraPath);
            if (string.IsNullOrWhiteSpace(_path))
            {
                string uriText = Intent?.GetStringExtra(ExtraUri);
                if (!string.IsNullOrWhiteSpace(uriText) && uriText.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    && SysUri.TryCreate(uriText, UriKind.Absolute, out SysUri fileUri))
                {
                    _path = fileUri.LocalPath;
                }
            }

            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            {
                Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                Finish();
                return;
            }

            try
            {
                _bitmap = BitmapFactory.DecodeFile(_path);
                LoadExif();
            }
            catch
            {
                _bitmap = null;
            }

            if (_bitmap == null)
            {
                Toast.MakeText(this, "Could not open image", ToastLength.Short).Show();
                Finish();
                return;
            }

            BuildUi();
            RefreshPreview();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            try { _bitmap?.Recycle(); } catch { }
            _bitmap = null;
        }

        private void BuildUi()
        {
            var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            root.SetBackgroundColor(Color.Black);
            root.SetPadding(16, 24, 16, 16);

            var header = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            var title = new TextView(this)
            {
                Text = "Crop / edit",
                TextSize = 18,
            };
            title.SetTextColor(Color.White);
            title.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            var close = new AButton(this) { Text = "✕" };
            close.Click += (_, __) => Finish();
            header.AddView(title);
            header.AddView(close);
            root.AddView(header);

            _preview = new ImageView(this);
            _preview.SetScaleType(ImageView.ScaleType.FitCenter);
            _preview.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f);
            root.AddView(_preview);

            _info = new TextView(this) { TextSize = 12 };
            _info.SetTextColor(Color.LightGray);
            root.AddView(_info);

            root.AddView(CreateButtonRow(
                ("Rotate 90°", () => { _rotationDegrees = (_rotationDegrees + 90) % 360; RefreshPreview(); }),
                ("Crop −10%", () => { _cropPercent = Math.Max(20, _cropPercent - 10); RefreshPreview(); }),
                ("Crop +10%", () => { _cropPercent = Math.Min(100, _cropPercent + 10); RefreshPreview(); })));

            root.AddView(CreateButtonRow(
                ("Size −10%", () => { _resizePercent = Math.Max(10, _resizePercent - 10); RefreshPreview(); }),
                ("Size +10%", () => { _resizePercent = Math.Min(200, _resizePercent + 10); RefreshPreview(); }),
                ("Clear GPS", () => { _clearGps = true; RefreshPreview(); })));

            var exifEdit = new AButton(this) { Text = "Set EXIF datetime (now)" };
            exifEdit.Click += (_, __) =>
            {
                _exifDateTime = DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                RefreshPreview();
            };
            root.AddView(exifEdit);

            var save = new AButton(this) { Text = "Save" };
            save.Click += (_, __) => SaveAndFinish();
            root.AddView(save);

            SetContentView(root);
        }

        private LinearLayout CreateButtonRow(params (string Label, Action Action)[] items)
        {
            var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            foreach ((string label, Action action) in items)
            {
                var btn = new AButton(this) { Text = label };
                btn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                Action captured = action;
                btn.Click += (_, __) => captured();
                row.AddView(btn);
            }
            return row;
        }

        private void LoadExif()
        {
            try
            {
                var exif = new ExifInterface(_path);
                _exifDateTime = exif.GetAttribute(ExifInterface.TagDatetime) ?? string.Empty;
            }
            catch
            {
                _exifDateTime = string.Empty;
            }
        }

        private void RefreshPreview()
        {
            using Bitmap preview = BuildResultBitmap(forSave: false);
            if (preview != null)
                _preview.SetImageBitmap(preview);

            _info.Text = string.Format(
                CultureInfo.InvariantCulture,
                "rot {0}° · crop {1}% · size {2}% · EXIF {3}{4}",
                _rotationDegrees,
                _cropPercent,
                _resizePercent,
                string.IsNullOrEmpty(_exifDateTime) ? "—" : _exifDateTime,
                _clearGps ? " · GPS cleared" : string.Empty);
        }

        private Bitmap BuildResultBitmap(bool forSave)
        {
            if (_bitmap == null)
                return null;

            Bitmap current = _bitmap;
            var owned = new System.Collections.Generic.List<Bitmap>();
            try
            {
                if (_rotationDegrees != 0)
                {
                    var matrix = new Matrix();
                    matrix.PostRotate(_rotationDegrees);
                    current = Bitmap.CreateBitmap(current, 0, 0, current.Width, current.Height, matrix, true);
                    owned.Add(current);
                }

                if (_cropPercent < 100)
                {
                    float factor = _cropPercent / 100f;
                    int w = Math.Max(1, (int)(current.Width * factor));
                    int h = Math.Max(1, (int)(current.Height * factor));
                    int x = Math.Max(0, (current.Width - w) / 2);
                    int y = Math.Max(0, (current.Height - h) / 2);
                    current = Bitmap.CreateBitmap(current, x, y, w, h);
                    owned.Add(current);
                }

                if (_resizePercent != 100)
                {
                    float factor = _resizePercent / 100f;
                    int w = Math.Max(1, (int)(current.Width * factor));
                    int h = Math.Max(1, (int)(current.Height * factor));
                    current = Bitmap.CreateScaledBitmap(current, w, h, true);
                    owned.Add(current);
                }

                if (forSave)
                {
                    // Detach the last bitmap so the caller can dispose it.
                    if (owned.Count > 0)
                        owned.RemoveAt(owned.Count - 1);
                    return current;
                }

                return current.Copy(current.GetConfig() ?? Bitmap.Config.Argb8888, false);
            }
            finally
            {
                foreach (Bitmap bmp in owned)
                {
                    try { bmp?.Recycle(); } catch { }
                }
            }
        }

        private void SaveAndFinish()
        {
            try
            {
                using Bitmap result = BuildResultBitmap(forSave: true);
                if (result == null)
                {
                    Toast.MakeText(this, "Nothing to save", ToastLength.Short).Show();
                    return;
                }

                string ext = IOPath.GetExtension(_path)?.ToLowerInvariant() ?? ".jpg";
                Bitmap.CompressFormat format = ext switch
                {
                    ".png" => Bitmap.CompressFormat.Png,
                    ".webp" => Bitmap.CompressFormat.Webp,
                    _ => Bitmap.CompressFormat.Jpeg,
                };

                string temp = _path + ".edit.tmp";
                using (var stream = File.Open(temp, FileMode.Create, FileAccess.Write))
                {
                    result.Compress(format, 92, stream);
                }

                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(temp, _path);

                try
                {
                    var exif = new ExifInterface(_path);
                    if (!string.IsNullOrEmpty(_exifDateTime))
                    {
                        exif.SetAttribute(ExifInterface.TagDatetime, _exifDateTime);
                        exif.SetAttribute(ExifInterface.TagDatetimeOriginal, _exifDateTime);
                    }
                    if (_clearGps)
                    {
                        exif.SetAttribute(ExifInterface.TagGpsLatitude, null);
                        exif.SetAttribute(ExifInterface.TagGpsLongitude, null);
                        exif.SetAttribute(ExifInterface.TagGpsLatitudeRef, null);
                        exif.SetAttribute(ExifInterface.TagGpsLongitudeRef, null);
                    }
                    exif.SetAttribute(ExifInterface.TagOrientation, ((int)MediaOrientation.Normal).ToString(CultureInfo.InvariantCulture));
                    exif.SaveAttributes();
                }
                catch
                {
                }

                Toast.MakeText(this, "Saved", ToastLength.Short).Show();
                SetResult(Result.Ok);
                Finish();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, ex.Message, ToastLength.Long).Show();
            }
        }

        public static Intent CreateIntent(Context context, string path)
        {
            var intent = new Intent(context, typeof(ImageEditorActivity));
            intent.PutExtra(ExtraPath, path);
            return intent;
        }
    }
}
