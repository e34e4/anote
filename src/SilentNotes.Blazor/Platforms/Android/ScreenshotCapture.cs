// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using Android.AccessibilityServices;
using Android.Content;
using Android.Graphics;
using Android.Hardware;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Java.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Services;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using Path = System.IO.Path;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Captures the screen via the accessibility service screenshot API (Android 11+), optionally
    /// crops a region, and saves under Pictures/aNote using the screenshot format/quality from settings.
    /// </summary>
    internal sealed class ScreenshotCapture
    {
        private const string PublicGalleryFolder = "aNote";
        private const string LogTag = "aNoteScreenshot";

        private readonly FloatingButtonAccessibilityService _service;
        private readonly Handler _handler;
        private IWindowManager _windowManager;
        private global::Android.Views.View _cropOverlay;
        private Bitmap _pendingBitmap;
        private bool _captureInProgress;

        public ScreenshotCapture(FloatingButtonAccessibilityService service)
        {
            _service = service;
            _handler = new Handler(Looper.MainLooper);
        }

        /// <summary>
        /// Hides floating UI, waits briefly, then captures the screen. When <paramref name="partial"/>
        /// is true, shows a crop overlay before saving.
        /// </summary>
        public void Start(bool partial)
        {
            if (_captureInProgress)
                return;

            if (Build.VERSION.SdkInt < BuildVersionCodes.R)
            {
                ShowToast(GetLanguageText("screenshot_unsupported", "Screenshots require Android 11 or newer"));
                return;
            }

            _captureInProgress = true;
            _service.HideMenuOverlay();
            _service.SetFloatingButtonTemporarilyHidden(true);

            // Let the menu and button disappear before the capture runs.
            _handler.PostDelayed(() => CaptureNow(partial), 180);
        }

        private void CaptureNow(bool partial)
        {
            try
            {
                int displayId = 0;
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R && _service.Display != null)
                        displayId = _service.Display.DisplayId;
                }
                catch
                {
                    displayId = 0;
                }

                IExecutor executor = Executors.NewSingleThreadExecutor();
                _service.TakeScreenshot(displayId, executor, new ScreenshotCallback(this, partial));
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "TakeScreenshot failed: " + ex);
                FinishCapture(success: false);
            }
        }

        private void OnScreenshotReady(Bitmap bitmap, bool partial)
        {
            if (bitmap == null)
            {
                FinishCapture(success: false);
                return;
            }

            if (partial)
            {
                _handler.Post(() => ShowCropOverlay(bitmap));
                return;
            }

            bool saved = SaveBitmap(bitmap);
            bitmap.Recycle();
            FinishCapture(saved);
        }

        private void ShowCropOverlay(Bitmap bitmap)
        {
            DisposeCropOverlay();
            _pendingBitmap = bitmap;
            _windowManager = _service.GetSystemService(Context.WindowService) as IWindowManager;
            if (_windowManager == null)
            {
                bitmap.Recycle();
                _pendingBitmap = null;
                FinishCapture(success: false);
                return;
            }

            float density = _service.Resources.DisplayMetrics.Density;
            var root = new FrameLayout(_service)
            {
                Clickable = true,
                Focusable = true,
            };
            root.SetBackgroundColor(Color.ParseColor("#CC000000"));

            var image = new ImageView(_service)
            {
                // Fit the full screenshot; crop coords are mapped from view to bitmap below.
            };
            image.SetScaleType(ImageView.ScaleType.FitCenter);
            image.SetImageBitmap(bitmap);
            root.AddView(image, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var selection = new SelectionOverlayView(_service);
            root.AddView(selection, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var controls = new LinearLayout(_service)
            {
                Orientation = Orientation.Horizontal,
            };
            controls.SetGravity(GravityFlags.Center);
            controls.SetPadding((int)(12 * density), (int)(12 * density), (int)(12 * density), (int)(24 * density));
            controls.SetBackgroundColor(Color.ParseColor("#E6101010"));

            string hint = GetLanguageText("screenshot_crop_hint", "Drag to select an area, then tap ✓");
            var hintView = new TextView(_service)
            {
                Text = hint,
                TextSize = 13f,
            };
            hintView.SetTextColor(Color.White);
            controls.AddView(hintView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var cancelBtn = CreateControlButton("✕", Color.ParseColor("#EF5350"), density);
            cancelBtn.Click += (s, e) =>
            {
                DisposeCropOverlay(recyclePending: true);
                FinishCapture(success: false);
            };
            controls.AddView(cancelBtn);

            var okBtn = CreateControlButton("✓", Color.ParseColor("#66BB6A"), density);
            okBtn.Click += (s, e) =>
            {
                RectF sel = selection.SelectionInView;
                Bitmap source = _pendingBitmap;
                _pendingBitmap = null;
                DisposeCropOverlay(recyclePending: false);
                if (source == null)
                {
                    FinishCapture(success: false);
                    return;
                }

                Bitmap cropped = CropFromImageView(source, image, sel);
                source.Recycle();
                if (cropped == null)
                {
                    FinishCapture(success: false);
                    return;
                }

                bool saved = SaveBitmap(cropped);
                cropped.Recycle();
                FinishCapture(saved);
            };
            controls.AddView(okBtn);

            var controlsParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom,
            };
            root.AddView(controls, controlsParams);

            var layoutParams = new WindowManagerLayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent,
                WindowManagerTypes.AccessibilityOverlay,
                WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start,
            };

            _cropOverlay = root;
            try
            {
                _windowManager.AddView(_cropOverlay, layoutParams);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show crop overlay: " + ex);
                DisposeCropOverlay(recyclePending: true);
                FinishCapture(success: false);
            }
        }

        private TextView CreateControlButton(string text, Color color, float density)
        {
            var btn = new TextView(_service)
            {
                Text = text,
                TextSize = 20f,
                Gravity = GravityFlags.Center,
            };
            btn.SetTextColor(color);
            int pad = (int)(14 * density);
            btn.SetPadding(pad, pad / 2, pad, pad / 2);
            return btn;
        }

        private static Bitmap CropFromImageView(Bitmap source, ImageView imageView, RectF selectionInView)
        {
            if (source == null || imageView == null || selectionInView == null)
                return null;
            if (selectionInView.Width() < 8 || selectionInView.Height() < 8)
                return null;

            // Map view coordinates to the FitCenter-drawn bitmap rectangle.
            float viewW = imageView.Width;
            float viewH = imageView.Height;
            if (viewW <= 0 || viewH <= 0)
                return null;

            float scale = Math.Min(viewW / source.Width, viewH / source.Height);
            float drawnW = source.Width * scale;
            float drawnH = source.Height * scale;
            float offsetX = (viewW - drawnW) / 2f;
            float offsetY = (viewH - drawnH) / 2f;

            float left = (selectionInView.Left - offsetX) / scale;
            float top = (selectionInView.Top - offsetY) / scale;
            float right = (selectionInView.Right - offsetX) / scale;
            float bottom = (selectionInView.Bottom - offsetY) / scale;

            int x = Math.Clamp((int)Math.Floor(left), 0, source.Width - 1);
            int y = Math.Clamp((int)Math.Floor(top), 0, source.Height - 1);
            int x2 = Math.Clamp((int)Math.Ceiling(right), x + 1, source.Width);
            int y2 = Math.Clamp((int)Math.Ceiling(bottom), y + 1, source.Height);
            int w = x2 - x;
            int h = y2 - y;
            if (w < 2 || h < 2)
                return null;

            try
            {
                return Bitmap.CreateBitmap(source, x, y, w, h);
            }
            catch
            {
                return null;
            }
        }

        private bool SaveBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                return false;

            try
            {
                SettingsModel settings = _service.LoadSettings();
                string format = NormalizeFormat(settings?.ScreenshotFormat);
                int quality = Math.Clamp(settings?.ScreenshotQuality > 0 ? settings.ScreenshotQuality : 70, 1, 100);

                string dir = Path.Combine(
                    _service.GetExternalFilesDir(global::Android.OS.Environment.DirectoryPictures)?.AbsolutePath
                        ?? _service.FilesDir.AbsolutePath,
                    "screenshots");
                Directory.CreateDirectory(dir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "SCR_{0}_q{1}.{2}",
                    timestamp,
                    quality,
                    format);
                string fullPath = Path.Combine(dir, fileName);

                Bitmap target = bitmap;
                Bitmap scaled = null;
                try
                {
                    if (format == "png" && quality < 100 && bitmap.Width > 64 && bitmap.Height > 64)
                    {
                        double scale = Math.Max(0.2, quality / 100.0);
                        int nw = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                        int nh = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                        if (nw != bitmap.Width || nh != bitmap.Height)
                        {
                            scaled = Bitmap.CreateScaledBitmap(bitmap, nw, nh, true);
                            if (scaled != null)
                                target = scaled;
                        }
                    }

                    using (FileStream fs = File.Create(fullPath))
                    {
                        target.Compress(GetCompressFormat(format, quality), quality, fs);
                    }

                    WriteFileToPublicGallery(fullPath, fileName);
                    return true;
                }
                finally
                {
                    scaled?.Recycle();
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not save screenshot: " + ex);
                return false;
            }
        }

        private void WriteFileToPublicGallery(string sourcePath, string displayName)
        {
            try
            {
                string ext = Path.GetExtension(displayName);
                string mime = GetMimeTypeForExtension(ext);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    var values = new ContentValues();
                    values.Put("_display_name", displayName);
                    values.Put("mime_type", mime);
                    values.Put("relative_path", "Pictures/" + PublicGalleryFolder);

                    global::Android.Net.Uri uri = _service.ContentResolver.Insert(
                        MediaStore.Images.Media.ExternalContentUri, values);
                    if (uri == null)
                        return;

                    using (Stream input = File.OpenRead(sourcePath))
                    using (Stream output = _service.ContentResolver.OpenOutputStream(uri))
                    {
                        input.CopyTo(output);
                    }
                }
                else
                {
                    string publicDir = Path.Combine(
                        global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                            global::Android.OS.Environment.DirectoryPictures).AbsolutePath,
                        PublicGalleryFolder);
                    Directory.CreateDirectory(publicDir);
                    File.Copy(sourcePath, Path.Combine(publicDir, displayName), overwrite: true);
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, "Gallery copy failed: " + ex.Message);
            }
        }

        private void FinishCapture(bool success)
        {
            _handler.Post(() =>
            {
                _service.SetFloatingButtonTemporarilyHidden(false);
                _captureInProgress = false;
                ShowToast(success
                    ? GetLanguageText("screenshot_saved", "Screenshot saved")
                    : GetLanguageText("screenshot_failed", "Screenshot failed"));
            });
        }

        private void DisposeCropOverlay(bool recyclePending = false)
        {
            if (_cropOverlay != null && _windowManager != null)
            {
                try
                {
                    _windowManager.RemoveView(_cropOverlay);
                }
                catch
                {
                }
            }
            _cropOverlay = null;
            if (recyclePending && _pendingBitmap != null)
            {
                _pendingBitmap.Recycle();
                _pendingBitmap = null;
            }
        }

        private void ShowToast(string message)
        {
            try
            {
                Toast.MakeText(_service, message, ToastLength.Short)?.Show();
            }
            catch
            {
            }
        }

        private string GetLanguageText(string key, string fallback)
        {
            try
            {
                ILanguageService language = _service.GetServiceProviderForScreenshot()?.GetService<ILanguageService>();
                string text = language?[key];
                if (!string.IsNullOrWhiteSpace(text) && text != key)
                    return text;
            }
            catch
            {
            }
            return fallback;
        }

        private static string NormalizeFormat(string format)
        {
            string fmt = (format ?? "jpg").Trim().ToLowerInvariant();
            return (fmt == "png" || fmt == "webp") ? fmt : "jpg";
        }

        private static Bitmap.CompressFormat GetCompressFormat(string format, int quality)
        {
            switch (format)
            {
                case "png":
                    return Bitmap.CompressFormat.Png;
                case "webp":
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                    {
                        return quality >= 100
                            ? Bitmap.CompressFormat.WebpLossless
                            : Bitmap.CompressFormat.WebpLossy;
                    }
                    #pragma warning disable CS0618
                    return Bitmap.CompressFormat.Webp;
                    #pragma warning restore CS0618
                default:
                    return Bitmap.CompressFormat.Jpeg;
            }
        }

        private static string GetMimeTypeForExtension(string ext)
        {
            switch (ext?.ToLowerInvariant())
            {
                case ".png":
                    return "image/png";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/jpeg";
            }
        }

        private sealed class ScreenshotCallback : Java.Lang.Object, AccessibilityService.ITakeScreenshotCallback
        {
            private readonly ScreenshotCapture _owner;
            private readonly bool _partial;

            public ScreenshotCallback(ScreenshotCapture owner, bool partial)
            {
                _owner = owner;
                _partial = partial;
            }

            public void OnSuccess(AccessibilityService.ScreenshotResult screenshot)
            {
                Bitmap bitmap = null;
                try
                {
                    HardwareBuffer buffer = screenshot?.HardwareBuffer;
                    if (buffer == null)
                    {
                        _owner._handler.Post(() => _owner.FinishCapture(false));
                        return;
                    }

                    using (buffer)
                    {
                        Bitmap hardware = Bitmap.WrapHardwareBuffer(buffer, null);
                        if (hardware == null)
                        {
                            _owner._handler.Post(() => _owner.FinishCapture(false));
                            return;
                        }

                        bitmap = hardware.Copy(Bitmap.Config.Argb8888, false);
                        hardware.Recycle();
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "Screenshot callback error: " + ex);
                }

                Bitmap ready = bitmap;
                _owner._handler.Post(() => _owner.OnScreenshotReady(ready, _partial));
            }

            public void OnFailure(int errorCode)
            {
                global::Android.Util.Log.Error(LogTag, "Screenshot failed, code=" + errorCode);
                _owner._handler.Post(() => _owner.FinishCapture(false));
            }
        }

        /// <summary>
        /// Full-screen touch layer that lets the user drag a selection rectangle.
        /// </summary>
        private sealed class SelectionOverlayView : global::Android.Views.View
        {
            private readonly Paint _fillPaint;
            private readonly Paint _strokePaint;
            private float _startX;
            private float _startY;
            private float _endX;
            private float _endY;
            private bool _hasSelection;

            public SelectionOverlayView(Context context)
                : base(context)
            {
                _fillPaint = new Paint { AntiAlias = true };
                _fillPaint.SetStyle(Paint.Style.Fill);
                _fillPaint.Color = Color.ParseColor("#5538BDF8");

                _strokePaint = new Paint { AntiAlias = true, StrokeWidth = 3f };
                _strokePaint.SetStyle(Paint.Style.Stroke);
                _strokePaint.Color = Color.ParseColor("#38BDF8");
            }

            public RectF SelectionInView
            {
                get
                {
                    if (!_hasSelection)
                        return new RectF(0, 0, 0, 0);
                    float left = Math.Min(_startX, _endX);
                    float top = Math.Min(_startY, _endY);
                    float right = Math.Max(_startX, _endX);
                    float bottom = Math.Max(_startY, _endY);
                    return new RectF(left, top, right, bottom);
                }
            }

            public override bool OnTouchEvent(MotionEvent e)
            {
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startX = _endX = e.GetX();
                        _startY = _endY = e.GetY();
                        _hasSelection = true;
                        Invalidate();
                        return true;
                    case MotionEventActions.Move:
                        _endX = e.GetX();
                        _endY = e.GetY();
                        Invalidate();
                        return true;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _endX = e.GetX();
                        _endY = e.GetY();
                        Invalidate();
                        return true;
                }
                return base.OnTouchEvent(e);
            }

            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);
                if (!_hasSelection)
                    return;
                RectF rect = SelectionInView;
                canvas.DrawRect(rect, _fillPaint);
                canvas.DrawRect(rect, _strokePaint);
            }
        }
    }
}
