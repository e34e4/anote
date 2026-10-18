// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using Path = System.IO.Path;
using Rect = Android.Graphics.Rect;
using Size = Android.Util.Size;
using Stream = System.IO.Stream;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="ICameraService"/> with a live Camera2 viewfinder
    /// dialog, front/back lens switching, flash/torch modes, digital zoom, system camera fallback,
    /// and photo encoding in JPG, PNG, or WEBP with configurable quality (1..100%).
    /// </summary>
    internal class CameraService : ICameraService
    {
        private const string PhotosFolderName = "photos";
        private const string ShareFolder = "clipboard";
        private const string PublicGalleryFolder = "aNote";

        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        public CameraService(
            IAppContextService appContextService,
            IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContextService;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasPermission
        {
            get
            {
                Context context = _appContext.Context;
                return (context != null)
                    && (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.Camera) == Permission.Granted);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestPermissionAsync()
        {
            try
            {
                if (HasPermission)
                    return true;
                PermissionStatus status = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<Permissions.Camera>());
                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<CameraPhotoModel> CapturePhotoAsync(
            string format,
            int quality,
            bool useFrontCamera,
            string flashMode,
            bool autoSaveToGallery)
        {
            if (!HasPermission && !await RequestPermissionAsync())
                return null;

            Activity activity = _appContext.RootActivity;
            if (activity == null)
                return null;

            string normalizedFormat = NormalizeFormat(format);
            int clampedQuality = Math.Clamp(quality, 1, 100);

            var tcs = new TaskCompletionSource<CapturedRawResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    var dialog = new LiveCameraDialog(
                        activity,
                        normalizedFormat,
                        clampedQuality,
                        useFrontCamera,
                        flashMode,
                        tcs);
                    dialog.Show();
                }
                catch (Exception)
                {
                    tcs.TrySetResult(null);
                }
            });

            CapturedRawResult raw = await tcs.Task;
            if (raw == null)
                return null;

            if (raw.UseSystemCameraFallback)
            {
                return await CaptureWithSystemCameraAsync(raw.Format, raw.Quality, autoSaveToGallery);
            }

            if (raw.Bitmap == null && (raw.JpegBytes == null || raw.JpegBytes.Length == 0))
                return null;

            return await Task.Run(() =>
            {
                Bitmap bitmap = raw.Bitmap;
                try
                {
                    if (bitmap == null && raw.JpegBytes != null)
                    {
                        bitmap = DecodeBitmapWithExifRotation(raw.JpegBytes, raw.RotationDegrees, raw.MirrorHorizontal);
                    }
                    if (bitmap == null)
                        return null;

                    return SaveEncodedBitmap(bitmap, raw.Format, raw.Quality, autoSaveToGallery);
                }
                finally
                {
                    bitmap?.Recycle();
                }
            });
        }

        /// <inheritdoc/>
        public async Task<CameraPhotoModel> CaptureWithSystemCameraAsync(
            string format,
            int quality,
            bool autoSaveToGallery)
        {
            if (!HasPermission && !await RequestPermissionAsync())
                return null;

            Activity activity = _appContext.RootActivity;
            Context context = _appContext.Context;
            if (activity == null || context == null || _activityResultAwaiter == null)
                return null;

            string normalizedFormat = NormalizeFormat(format);
            int clampedQuality = Math.Clamp(quality, 1, 100);

            var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
            shareDir.Mkdirs();
            var tempCaptureFile = new Java.IO.File(shareDir, "cam_capture_" + Guid.NewGuid().ToString("N") + ".jpg");

            try
            {
                global::Android.Net.Uri captureUri = FileProvider.GetUriForFile(
                    context, context.PackageName + ".fileprovider", tempCaptureFile);

                var captureIntent = new Intent(MediaStore.ActionImageCapture);
                captureIntent.PutExtra(MediaStore.ExtraOutput, captureUri);
                captureIntent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);

                ActivityResult result = await _activityResultAwaiter.StartActivityAndWaitForResult(activity, captureIntent);
                if (result == null || result.ResultCode != Result.Ok)
                    return null;

                return await Task.Run(() =>
                {
                    Bitmap bitmap = null;
                    try
                    {
                        if (tempCaptureFile.Exists() && tempCaptureFile.Length() > 0)
                        {
                            byte[] rawBytes = File.ReadAllBytes(tempCaptureFile.AbsolutePath);
                            bitmap = DecodeBitmapWithExifRotation(rawBytes, 0, false);
                        }
                        else if (result.Data?.Extras != null)
                        {
                            #pragma warning disable CS0618
                            bitmap = result.Data.Extras.Get("data") as Bitmap;
                            #pragma warning restore CS0618
                        }

                        if (bitmap == null)
                            return null;

                        return SaveEncodedBitmap(bitmap, normalizedFormat, clampedQuality, autoSaveToGallery);
                    }
                    finally
                    {
                        bitmap?.Recycle();
                    }
                });
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try
                {
                    if (tempCaptureFile.Exists())
                        tempCaptureFile.Delete();
                }
                catch (Exception)
                {
                }
            }
        }

        /// <inheritdoc/>
        public Task<CameraPhotoModel> ConvertExistingImageAsync(
            byte[] imageBytes,
            string format,
            int quality,
            bool autoSaveToGallery)
        {
            return Task.Run(() =>
            {
                if (imageBytes == null || imageBytes.Length == 0)
                    return null;

                Bitmap bitmap = null;
                try
                {
                    bitmap = DecodeBitmapWithExifRotation(imageBytes, 0, false);
                    if (bitmap == null)
                        return null;

                    return SaveEncodedBitmap(
                        bitmap,
                        NormalizeFormat(format),
                        Math.Clamp(quality, 1, 100),
                        autoSaveToGallery);
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    bitmap?.Recycle();
                }
            });
        }

        /// <inheritdoc/>
        public IReadOnlyList<CameraPhotoModel> GetPhotos()
        {
            var result = new List<CameraPhotoModel>();
            try
            {
                string dir = GetPhotosDirectory();
                if (!Directory.Exists(dir))
                    return result;

                var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".webp",
                };

                foreach (string filePath in Directory.EnumerateFiles(dir))
                {
                    string ext = Path.GetExtension(filePath);
                    if (!supportedExts.Contains(ext))
                        continue;

                    var info = new FileInfo(filePath);
                    var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(filePath, options);

                    string fmt = ext.TrimStart('.').ToLowerInvariant();
                    if (fmt == "jpeg")
                        fmt = "jpg";

                    result.Add(new CameraPhotoModel
                    {
                        FileName = info.Name,
                        FilePath = filePath,
                        Format = fmt,
                        Quality = ParseQualityFromFileName(info.Name),
                        Width = Math.Max(0, options.OutWidth),
                        Height = Math.Max(0, options.OutHeight),
                        SizeBytes = info.Length,
                        CreatedAtUtc = info.CreationTimeUtc,
                    });
                }
            }
            catch (Exception)
            {
            }

            return result.OrderByDescending(p => p.CreatedAtUtc).ToList();
        }

        /// <inheritdoc/>
        public Task<string> GetPhotoPreviewDataUrlAsync(string fileName, int maxDimension = 360)
        {
            return Task.Run(() =>
            {
                Bitmap full = null;
                Bitmap scaled = null;
                try
                {
                    string path = Path.Combine(GetPhotosDirectory(), Path.GetFileName(fileName));
                    if (!File.Exists(path))
                        return null;

                    var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(path, bounds);
                    int sample = 1;
                    int maxSide = Math.Max(bounds.OutWidth, bounds.OutHeight);
                    while (maxSide / (sample * 2) >= maxDimension)
                        sample *= 2;

                    var decodeOpts = new BitmapFactory.Options { InSampleSize = sample };
                    full = BitmapFactory.DecodeFile(path, decodeOpts);
                    if (full == null)
                        return null;

                    int w = full.Width;
                    int h = full.Height;
                    if (Math.Max(w, h) > maxDimension && w > 0 && h > 0)
                    {
                        float ratio = (float)maxDimension / Math.Max(w, h);
                        int nw = Math.Max(1, (int)(w * ratio));
                        int nh = Math.Max(1, (int)(h * ratio));
                        scaled = Bitmap.CreateScaledBitmap(full, nw, nh, true);
                    }

                    Bitmap target = scaled ?? full;
                    using (var ms = new MemoryStream())
                    {
                        target.Compress(Bitmap.CompressFormat.Jpeg, 80, ms);
                        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    scaled?.Recycle();
                    full?.Recycle();
                }
            });
        }

        /// <inheritdoc/>
        public Task<bool> OpenPhotoAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetPhotosDirectory(), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return false;

                    Context context = _appContext.Context;
                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = FileProvider.GetUriForFile(
                        context, context.PackageName + ".fileprovider", shareFile);

                    string mime = GetMimeTypeForExtension(Path.GetExtension(fileName));
                    var intent = new Intent(Intent.ActionView);
                    intent.SetDataAndType(uri, mime);
                    intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
                    context.StartActivity(intent);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public Task<string> SaveToPublicGalleryAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetPhotosDirectory(), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return null;

                    return WriteFileToPublicGallery(sourcePath, Path.GetFileName(fileName));
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public Task<bool> SharePhotoAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetPhotosDirectory(), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return false;

                    Context context = _appContext.Context;
                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = FileProvider.GetUriForFile(
                        context, context.PackageName + ".fileprovider", shareFile);

                    string mime = GetMimeTypeForExtension(Path.GetExtension(fileName));
                    var shareIntent = new Intent(Intent.ActionSend);
                    shareIntent.SetType(mime);
                    shareIntent.PutExtra(Intent.ExtraStream, uri);
                    shareIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

                    Intent chooser = Intent.CreateChooser(shareIntent, Path.GetFileName(fileName));
                    chooser.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(chooser);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public bool RenamePhoto(string fileName, string newNameWithoutExtension)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(newNameWithoutExtension))
                    return false;

                string dir = GetPhotosDirectory();
                string sourcePath = Path.Combine(dir, Path.GetFileName(fileName));
                if (!File.Exists(sourcePath))
                    return false;

                string ext = Path.GetExtension(fileName);
                string sanitized = SanitizeFileName(newNameWithoutExtension.Trim());
                if (string.IsNullOrEmpty(sanitized))
                    return false;

                string targetPath = Path.Combine(dir, sanitized + ext);
                if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (File.Exists(targetPath))
                    return false;

                File.Move(sourcePath, targetPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool DeletePhoto(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                string path = Path.Combine(GetPhotosDirectory(), Path.GetFileName(fileName));
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private CameraPhotoModel SaveEncodedBitmap(
            Bitmap bitmap,
            string format,
            int quality,
            bool autoSaveToGallery)
        {
            string normalizedFormat = NormalizeFormat(format);
            int clampedQuality = Math.Clamp(quality, 1, 100);
            string ext = "." + normalizedFormat;

            string dir = GetPhotosDirectory();
            Directory.CreateDirectory(dir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "IMG_{0}_q{1}{2}",
                timestamp,
                clampedQuality,
                ext);
            string fullPath = Path.Combine(dir, fileName);

            Bitmap targetBitmap = bitmap;
            Bitmap scaledPngBitmap = null;
            try
            {
                // For PNG (which is lossless in Bitmap.Compress), if the user explicitly lowers the
                // quality percentage below 100%, we scale resolution proportionally so the file size
                // reflects the requested quality percentage while keeping lossless PNG encoding.
                if (normalizedFormat == "png" && clampedQuality < 100 && bitmap.Width > 64 && bitmap.Height > 64)
                {
                    double scale = Math.Max(0.2, clampedQuality / 100.0);
                    int nw = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                    int nh = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                    if (nw != bitmap.Width || nh != bitmap.Height)
                    {
                        scaledPngBitmap = Bitmap.CreateScaledBitmap(bitmap, nw, nh, true);
                        if (scaledPngBitmap != null)
                            targetBitmap = scaledPngBitmap;
                    }
                }

                Bitmap.CompressFormat compressFormat = GetCompressFormat(normalizedFormat, clampedQuality);
                using (FileStream fs = File.Create(fullPath))
                {
                    targetBitmap.Compress(compressFormat, clampedQuality, fs);
                }

                string publicPath = null;
                if (autoSaveToGallery)
                {
                    publicPath = WriteFileToPublicGallery(fullPath, fileName);
                }

                var info = new FileInfo(fullPath);
                return new CameraPhotoModel
                {
                    FileName = fileName,
                    FilePath = fullPath,
                    Format = normalizedFormat,
                    Quality = clampedQuality,
                    Width = targetBitmap.Width,
                    Height = targetBitmap.Height,
                    SizeBytes = info.Exists ? info.Length : 0,
                    CreatedAtUtc = DateTime.UtcNow,
                    PublicGalleryPath = publicPath,
                };
            }
            finally
            {
                scaledPngBitmap?.Recycle();
            }
        }

        private string WriteFileToPublicGallery(string sourcePath, string displayName)
        {
            try
            {
                Context context = _appContext.Context;
                string ext = Path.GetExtension(displayName);
                string mime = GetMimeTypeForExtension(ext);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    var values = new ContentValues();
                    values.Put("_display_name", displayName);
                    values.Put("mime_type", mime);
                    values.Put("relative_path", "Pictures/" + PublicGalleryFolder);

                    global::Android.Net.Uri uri = context.ContentResolver.Insert(
                        MediaStore.Images.Media.ExternalContentUri, values);
                    if (uri == null)
                        return null;

                    using (Stream input = File.OpenRead(sourcePath))
                    using (Stream output = context.ContentResolver.OpenOutputStream(uri))
                    {
                        input.CopyTo(output);
                    }
                    return "Pictures/" + PublicGalleryFolder + "/" + displayName;
                }
                else
                {
                    string publicDir = Path.Combine(
                        global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                            global::Android.OS.Environment.DirectoryPictures).AbsolutePath,
                        PublicGalleryFolder);
                    Directory.CreateDirectory(publicDir);
                    string target = Path.Combine(publicDir, displayName);
                    File.Copy(sourcePath, target, overwrite: true);
                    return target;
                }
            }
            catch (Exception)
            {
                return null;
            }
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

        internal static string NormalizeFormat(string format)
        {
            string lower = (format ?? "jpg").Trim().TrimStart('.').ToLowerInvariant();
            if (lower == "png")
                return "png";
            if (lower == "webp")
                return "webp";
            return "jpg";
        }

        private static int ParseQualityFromFileName(string fileName)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                int idx = name.LastIndexOf("_q", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && idx + 2 < name.Length)
                {
                    if (int.TryParse(name.Substring(idx + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int q))
                        return Math.Clamp(q, 1, 100);
                }
            }
            catch (Exception)
            {
            }
            return 90;
        }

        private static Bitmap DecodeBitmapWithExifRotation(byte[] bytes, int fallbackRotationDegrees, bool mirrorHorizontal)
        {
            Bitmap decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (decoded == null)
                return null;

            int rotation = fallbackRotationDegrees;
            bool flipH = mirrorHorizontal;

            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var exif = new ExifInterface(ms);
                    int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)global::Android.Media.Orientation.Normal);
                    switch (orientation)
                    {
                        case (int)global::Android.Media.Orientation.Rotate90:
                            rotation = 90;
                            break;
                        case (int)global::Android.Media.Orientation.Rotate180:
                            rotation = 180;
                            break;
                        case (int)global::Android.Media.Orientation.Rotate270:
                            rotation = 270;
                            break;
                        case (int)global::Android.Media.Orientation.FlipHorizontal:
                            flipH = true;
                            break;
                    }
                }
            }
            catch (Exception)
            {
            }

            if (rotation == 0 && !flipH)
                return decoded;

            var matrix = new Matrix();
            if (rotation != 0)
                matrix.PostRotate(rotation);
            if (flipH)
                matrix.PostScale(-1f, 1f);

            Bitmap rotated = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
            if (!ReferenceEquals(rotated, decoded))
                decoded.Recycle();
            return rotated;
        }

        private string GetPhotosDirectory()
        {
            string baseDir = ContextCompat.GetNoBackupFilesDir(_appContext.Context).AbsolutePath;
            return Path.Combine(baseDir, PhotosFolderName);
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.Where(c => !invalid.Contains(c)).ToArray();
            return new string(chars).Trim();
        }

        private class CapturedRawResult
        {
            public Bitmap Bitmap { get; set; }
            public byte[] JpegBytes { get; set; }
            public int RotationDegrees { get; set; }
            public bool MirrorHorizontal { get; set; }
            public string Format { get; set; }
            public int Quality { get; set; }
            public bool UseSystemCameraFallback { get; set; }
        }

        /// <summary>
        /// Full-screen native Android Camera2 viewfinder dialog with live preview, lens switching,
        /// flash/torch control, digital zoom slider, and live format (JPG/PNG/WEBP) + quality (1-100%)
        /// controls.
        /// </summary>
        private class LiveCameraDialog : Dialog, TextureView.ISurfaceTextureListener
        {
            private readonly Activity _activity;
            private readonly TaskCompletionSource<CapturedRawResult> _tcs;
            private readonly TextureView _textureView;
            private readonly TextView _formatButton;
            private readonly TextView _qualityLabel;
            private readonly SeekBar _qualitySeek;
            private readonly TextView _flashButton;
            private readonly TextView _zoomLabel;
            private readonly SeekBar _zoomSeek;

            private string _selectedFormat;
            private int _selectedQuality;
            private bool _useFrontCamera;
            private string _flashMode;
            private float _zoomLevel = 1.0f;
            private float _maxZoom = 4.0f;
            private Rect _sensorArraySize;
            private int _sensorOrientation = 90;

            private HandlerThread _backgroundThread;
            private Handler _backgroundHandler;
            private CameraDevice _cameraDevice;
            private CameraCaptureSession _captureSession;
            private CaptureRequest.Builder _previewRequestBuilder;
            private Size _previewSize = new Size(1920, 1080);
            private bool _isCompleted;

            public LiveCameraDialog(
                Activity activity,
                string initialFormat,
                int initialQuality,
                bool useFrontCamera,
                string flashMode,
                TaskCompletionSource<CapturedRawResult> tcs)
                : base(activity, global::Android.Resource.Style.ThemeBlackNoTitleBarFullScreen)
            {
                _activity = activity;
                _selectedFormat = NormalizeFormat(initialFormat);
                _selectedQuality = Math.Clamp(initialQuality, 1, 100);
                _useFrontCamera = useFrontCamera;
                _flashMode = string.IsNullOrEmpty(flashMode) ? "auto" : flashMode;
                _tcs = tcs;

                float density = activity.Resources.DisplayMetrics.Density;
                int pad = (int)(12 * density);

                var root = new FrameLayout(activity);
                root.SetBackgroundColor(Color.Black);

                _textureView = new TextureView(activity)
                {
                    SurfaceTextureListener = this,
                };
                root.AddView(_textureView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent,
                    GravityFlags.Center));

                // Top HUD bar: Close | Format cycle | Flash | System Cam | Lens Switch
                var topContainer = new LinearLayout(activity)
                {
                    Orientation = Orientation.Vertical,
                };
                topContainer.SetBackgroundColor(Color.ParseColor("#AA000000"));
                topContainer.SetPadding(pad, pad, pad, pad);

                var topRow = new LinearLayout(activity)
                {
                    Orientation = Orientation.Horizontal,
                };
                topRow.SetGravity(GravityFlags.CenterVertical);

                TextView closeBtn = CreateHudPill(activity, "✕", density);
                closeBtn.Click += (s, e) => FinishWithResult(null);
                topRow.AddView(closeBtn);

                topRow.AddView(CreateSpacer(activity));

                _formatButton = CreateHudPill(activity, _selectedFormat.ToUpperInvariant(), density);
                _formatButton.Click += (s, e) => CycleFormat();
                topRow.AddView(_formatButton);

                topRow.AddView(CreateSpacer(activity));

                _flashButton = CreateHudPill(activity, GetFlashLabel(_flashMode), density);
                _flashButton.Click += (s, e) => CycleFlash();
                topRow.AddView(_flashButton);

                topRow.AddView(CreateSpacer(activity));

                TextView sysCamBtn = CreateHudPill(activity, "SYS", density);
                sysCamBtn.Click += (s, e) => FinishWithResult(new CapturedRawResult
                {
                    Format = _selectedFormat,
                    Quality = _selectedQuality,
                    UseSystemCameraFallback = true,
                });
                topRow.AddView(sysCamBtn);

                topRow.AddView(CreateSpacer(activity));

                TextView switchCamBtn = CreateHudPill(activity, "🔄", density);
                switchCamBtn.Click += (s, e) => SwitchCamera();
                topRow.AddView(switchCamBtn);

                topContainer.AddView(topRow);

                // Quality row inside camera HUD
                var qualityRow = new LinearLayout(activity)
                {
                    Orientation = Orientation.Horizontal,
                };
                qualityRow.SetGravity(GravityFlags.CenterVertical);
                qualityRow.SetPadding(0, (int)(8 * density), 0, 0);

                _qualityLabel = new TextView(activity)
                {
                    Text = string.Format(CultureInfo.InvariantCulture, "Quality: {0}%", _selectedQuality),
                    TextSize = 13f,
                };
                _qualityLabel.SetTextColor(Color.White);
                qualityRow.AddView(_qualityLabel, new LinearLayout.LayoutParams(
                    (int)(110 * density), ViewGroup.LayoutParams.WrapContent));

                _qualitySeek = new SeekBar(activity)
                {
                    Max = 99,
                    Progress = _selectedQuality - 1,
                };
                _qualitySeek.ProgressChanged += (s, e) =>
                {
                    _selectedQuality = Math.Clamp(e.Progress + 1, 1, 100);
                    _qualityLabel.Text = string.Format(CultureInfo.InvariantCulture, "Quality: {0}%", _selectedQuality);
                };
                qualityRow.AddView(_qualitySeek, new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WrapContent, 1f));

                topContainer.AddView(qualityRow);

                root.AddView(topContainer, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent,
                    GravityFlags.Top));

                // Bottom HUD: Zoom slider + Shutter button
                var bottomContainer = new LinearLayout(activity)
                {
                    Orientation = Orientation.Vertical,
                };
                bottomContainer.SetBackgroundColor(Color.ParseColor("#AA000000"));
                bottomContainer.SetPadding(pad, pad, pad, (int)(24 * density));
                bottomContainer.SetGravity(GravityFlags.CenterHorizontal);

                var zoomRow = new LinearLayout(activity)
                {
                    Orientation = Orientation.Horizontal,
                };
                zoomRow.SetGravity(GravityFlags.CenterVertical);
                zoomRow.SetPadding(0, 0, 0, (int)(10 * density));

                _zoomLabel = new TextView(activity)
                {
                    Text = "Zoom 1.0x",
                    TextSize = 13f,
                };
                _zoomLabel.SetTextColor(Color.White);
                zoomRow.AddView(_zoomLabel, new LinearLayout.LayoutParams(
                    (int)(90 * density), ViewGroup.LayoutParams.WrapContent));

                _zoomSeek = new SeekBar(activity)
                {
                    Max = 100,
                    Progress = 0,
                };
                _zoomSeek.ProgressChanged += (s, e) =>
                {
                    float range = Math.Max(0f, _maxZoom - 1.0f);
                    _zoomLevel = 1.0f + (range * (e.Progress / 100f));
                    _zoomLabel.Text = string.Format(CultureInfo.InvariantCulture, "Zoom {0:0.0}x", _zoomLevel);
                    ApplyPreviewParameters();
                };
                zoomRow.AddView(_zoomSeek, new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WrapContent, 1f));

                bottomContainer.AddView(zoomRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                // Circular shutter button
                var shutterBtn = new TextView(activity)
                {
                    Text = "●",
                    TextSize = 36f,
                    Gravity = GravityFlags.Center,
                };
                shutterBtn.SetTextColor(Color.White);
                var shutterBg = new GradientDrawable();
                shutterBg.SetShape(ShapeType.Oval);
                shutterBg.SetColor(Color.ParseColor("#33FFFFFF"));
                shutterBg.SetStroke((int)(4 * density), Color.White);
                shutterBtn.Background = shutterBg;
                int shutterSize = (int)(74 * density);
                shutterBtn.Click += (s, e) => TakeSnapshot();
                bottomContainer.AddView(shutterBtn, new LinearLayout.LayoutParams(shutterSize, shutterSize));

                root.AddView(bottomContainer, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent,
                    GravityFlags.Bottom));

                SetContentView(root);
                SetCanceledOnTouchOutside(false);
            }

            protected override void OnStart()
            {
                base.OnStart();
                StartBackgroundThread();
                if (_textureView.IsAvailable)
                    OpenCamera();
            }

            protected override void OnStop()
            {
                CloseCamera();
                StopBackgroundThread();
                base.OnStop();
            }

            public override void OnBackPressed()
            {
                FinishWithResult(null);
            }

            public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            {
                OpenCamera();
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                CloseCamera();
                return true;
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
            }

            private void CycleFormat()
            {
                _selectedFormat = _selectedFormat switch
                {
                    "jpg" => "png",
                    "png" => "webp",
                    _ => "jpg",
                };
                _formatButton.Text = _selectedFormat.ToUpperInvariant();
            }

            private void CycleFlash()
            {
                _flashMode = _flashMode switch
                {
                    "auto" => "on",
                    "on" => "torch",
                    "torch" => "off",
                    _ => "auto",
                };
                _flashButton.Text = GetFlashLabel(_flashMode);
                ApplyPreviewParameters();
            }

            private static string GetFlashLabel(string mode)
            {
                return mode switch
                {
                    "on" => "⚡ ON",
                    "torch" => "🔦 TORCH",
                    "off" => "⚡ OFF",
                    _ => "⚡ AUTO",
                };
            }

            private void SwitchCamera()
            {
                _useFrontCamera = !_useFrontCamera;
                _zoomLevel = 1.0f;
                _zoomSeek.Progress = 0;
                CloseCamera();
                OpenCamera();
            }

            private void TakeSnapshot()
            {
                try
                {
                    if (!_textureView.IsAvailable)
                        return;

                    Bitmap snapshot = _textureView.Bitmap;
                    if (snapshot == null)
                        return;

                    FinishWithResult(new CapturedRawResult
                    {
                        Bitmap = snapshot,
                        Format = _selectedFormat,
                        Quality = _selectedQuality,
                        RotationDegrees = 0,
                        MirrorHorizontal = false,
                    });
                }
                catch (Exception)
                {
                    FinishWithResult(null);
                }
            }

            private void FinishWithResult(CapturedRawResult result)
            {
                if (_isCompleted)
                    return;
                _isCompleted = true;

                try
                {
                    CloseCamera();
                    StopBackgroundThread();
                    Dismiss();
                }
                catch (Exception)
                {
                }
                _tcs.TrySetResult(result);
            }

            private void OpenCamera()
            {
                try
                {
                    var manager = (CameraManager)_activity.GetSystemService(Context.CameraService);
                    if (manager == null)
                        return;

                    string selectedId = SelectCameraId(manager, _useFrontCamera);
                    if (string.IsNullOrEmpty(selectedId))
                        return;

                    CameraCharacteristics chars = manager.GetCameraCharacteristics(selectedId);
                    _sensorArraySize = chars.Get(CameraCharacteristics.SensorInfoActiveArraySize) as Rect;
                    var maxZoomFloat = chars.Get(CameraCharacteristics.ScalerAvailableMaxDigitalZoom) as Java.Lang.Float;
                    _maxZoom = maxZoomFloat != null ? Math.Max(1.0f, maxZoomFloat.FloatValue()) : 4.0f;
                    var orientationInt = chars.Get(CameraCharacteristics.SensorOrientation) as Java.Lang.Integer;
                    _sensorOrientation = orientationInt != null ? orientationInt.IntValue() : 90;

                    var map = chars.Get(CameraCharacteristics.ScalerStreamConfigurationMap) as StreamConfigurationMap;
                    if (map != null)
                    {
                        Size[] sizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
                        if (sizes != null && sizes.Length > 0)
                        {
                            _previewSize = sizes
                                .OrderByDescending(s => (long)s.Width * s.Height)
                                .FirstOrDefault(s => s.Width <= 2560 && s.Height <= 2560) ?? sizes[0];
                        }
                    }

                    manager.OpenCamera(selectedId, new CameraStateListener(this), _backgroundHandler);
                }
                catch (Exception)
                {
                }
            }

            private static string SelectCameraId(CameraManager manager, bool wantFront)
            {
                string[] ids = manager.GetCameraIdList();
                if (ids == null || ids.Length == 0)
                    return null;

                LensFacing targetFacing = wantFront ? LensFacing.Front : LensFacing.Back;
                foreach (string id in ids)
                {
                    CameraCharacteristics chars = manager.GetCameraCharacteristics(id);
                    var facing = chars.Get(CameraCharacteristics.LensFacing) as Java.Lang.Integer;
                    if (facing != null && facing.IntValue() == (int)targetFacing)
                        return id;
                }
                return ids[0];
            }

            private void StartPreviewSession()
            {
                try
                {
                    if (_cameraDevice == null || !_textureView.IsAvailable)
                        return;

                    SurfaceTexture texture = _textureView.SurfaceTexture;
                    if (texture == null)
                        return;

                    texture.SetDefaultBufferSize(_previewSize.Width, _previewSize.Height);
                    var surface = new Surface(texture);

                    _previewRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    _previewRequestBuilder.AddTarget(surface);

                    #pragma warning disable CS0618
                    _cameraDevice.CreateCaptureSession(
                        new List<Surface> { surface },
                        new CaptureSessionListener(this),
                        _backgroundHandler);
                    #pragma warning restore CS0618
                }
                catch (Exception)
                {
                }
            }

            private void ApplyPreviewParameters()
            {
                try
                {
                    if (_captureSession == null || _previewRequestBuilder == null)
                        return;

                    _previewRequestBuilder.Set(
                        CaptureRequest.ControlAfMode,
                        (int)ControlAFMode.ContinuousPicture);

                    if (_flashMode == "torch")
                    {
                        _previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                        _previewRequestBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Torch);
                    }
                    else if (_flashMode == "on")
                    {
                        _previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.OnAlwaysFlash);
                        _previewRequestBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Off);
                    }
                    else if (_flashMode == "auto")
                    {
                        _previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.OnAutoFlash);
                        _previewRequestBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Off);
                    }
                    else
                    {
                        _previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                        _previewRequestBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Off);
                    }

                    if (_sensorArraySize != null && _zoomLevel > 1.0f)
                    {
                        int cropW = (int)(_sensorArraySize.Width() / _zoomLevel);
                        int cropH = (int)(_sensorArraySize.Height() / _zoomLevel);
                        int cropX = (_sensorArraySize.Width() - cropW) / 2;
                        int cropY = (_sensorArraySize.Height() - cropH) / 2;
                        _previewRequestBuilder.Set(
                            CaptureRequest.ScalerCropRegion,
                            new Rect(cropX, cropY, cropX + cropW, cropY + cropH));
                    }

                    _captureSession.SetRepeatingRequest(_previewRequestBuilder.Build(), null, _backgroundHandler);
                }
                catch (Exception)
                {
                }
            }

            private void CloseCamera()
            {
                try
                {
                    _captureSession?.Close();
                    _captureSession = null;
                }
                catch (Exception)
                {
                }

                try
                {
                    _cameraDevice?.Close();
                    _cameraDevice = null;
                }
                catch (Exception)
                {
                }
            }

            private void StartBackgroundThread()
            {
                if (_backgroundThread != null)
                    return;
                _backgroundThread = new HandlerThread("aNoteCameraThread");
                _backgroundThread.Start();
                _backgroundHandler = new Handler(_backgroundThread.Looper);
            }

            private void StopBackgroundThread()
            {
                if (_backgroundThread == null)
                    return;
                try
                {
                    _backgroundThread.QuitSafely();
                    _backgroundThread.Join(500);
                }
                catch (Exception)
                {
                }
                _backgroundThread = null;
                _backgroundHandler = null;
            }

            private static TextView CreateHudPill(Context context, string text, float density)
            {
                var tv = new TextView(context)
                {
                    Text = text,
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                tv.SetTextColor(Color.White);
                int hPad = (int)(12 * density);
                int vPad = (int)(6 * density);
                tv.SetPadding(hPad, vPad, hPad, vPad);

                var bg = new GradientDrawable();
                bg.SetCornerRadius(8 * density);
                bg.SetColor(Color.ParseColor("#33FFFFFF"));
                bg.SetStroke((int)(1 * density), Color.ParseColor("#66FFFFFF"));
                tv.Background = bg;
                return tv;
            }

            private static global::Android.Views.View CreateSpacer(Context context)
            {
                return new global::Android.Views.View(context)
                {
                    LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f),
                };
            }

            private class CameraStateListener : CameraDevice.StateCallback
            {
                private readonly LiveCameraDialog _dialog;

                public CameraStateListener(LiveCameraDialog dialog)
                {
                    _dialog = dialog;
                }

                public override void OnOpened(CameraDevice camera)
                {
                    _dialog._cameraDevice = camera;
                    _dialog.StartPreviewSession();
                }

                public override void OnDisconnected(CameraDevice camera)
                {
                    camera.Close();
                    _dialog._cameraDevice = null;
                }

                public override void OnError(CameraDevice camera, CameraError error)
                {
                    camera.Close();
                    _dialog._cameraDevice = null;
                }
            }

            private class CaptureSessionListener : CameraCaptureSession.StateCallback
            {
                private readonly LiveCameraDialog _dialog;

                public CaptureSessionListener(LiveCameraDialog dialog)
                {
                    _dialog = dialog;
                }

                public override void OnConfigured(CameraCaptureSession session)
                {
                    if (_dialog._cameraDevice == null)
                        return;
                    _dialog._captureSession = session;
                    _dialog.ApplyPreviewParameters();
                }

                public override void OnConfigureFailed(CameraCaptureSession session)
                {
                }
            }
        }
    }
}
