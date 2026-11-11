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
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using MlKitBarcodeScanning = Xamarin.Google.MLKit.Vision.BarCode.BarcodeScanning;
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
using SilentNotes;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using Color = Android.Graphics.Color;
using Image = Android.Media.Image;
using Orientation = Android.Widget.Orientation;
using Path = System.IO.Path;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;
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

        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;
        private readonly IQrService _qrService;
        private readonly IQrHistoryService _qrHistoryService;

        public CameraService(
            IAppContextService appContextService,
            IActivityResultAwaiter activityResultAwaiter,
            IQrService qrService = null,
            IQrHistoryService qrHistoryService = null)
        {
            _appContext = appContextService;
            _activityResultAwaiter = activityResultAwaiter;
            _qrService = qrService;
            _qrHistoryService = qrHistoryService;
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
                        _qrService,
                        _qrHistoryService,
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

        private bool HasAudioPermission
        {
            get
            {
                Context context = _appContext.Context;
                return (context != null)
                    && (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.RecordAudio) == Permission.Granted);
            }
        }

        private async Task<bool> RequestAudioPermissionAsync()
        {
            try
            {
                if (HasAudioPermission)
                    return true;
                PermissionStatus status = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<Permissions.Microphone>());
                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<CameraPhotoModel> RecordVideoAsync(
            string resolution,
            int bitrateMbps,
            bool useFrontCamera,
            string flashMode,
            bool recordAudio,
            bool autoSaveToGallery)
        {
            if (!HasPermission && !await RequestPermissionAsync())
                return null;

            bool includeAudio = recordAudio;
            if (includeAudio && !HasAudioPermission)
                includeAudio = await RequestAudioPermissionAsync();

            Activity activity = _appContext.RootActivity;
            if (activity == null)
                return null;

            string normalizedResolution = NormalizeVideoResolution(resolution);
            int clampedBitrate = NormalizeVideoBitrateMbps(bitrateMbps);
            string photosDir = GetPhotosDirectory();
            Directory.CreateDirectory(photosDir);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    var dialog = new LiveVideoCameraDialog(
                        activity,
                        photosDir,
                        normalizedResolution,
                        clampedBitrate,
                        useFrontCamera,
                        flashMode,
                        includeAudio,
                        tcs);
                    dialog.Show();
                }
                catch (Exception)
                {
                    tcs.TrySetResult(null);
                }
            });

            string savedPath = await tcs.Task;
            if (string.IsNullOrEmpty(savedPath) || !File.Exists(savedPath))
                return null;

            return await Task.Run(() => BuildVideoModelFromFile(savedPath, clampedBitrate, autoSaveToGallery));
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
                global::Android.Net.Uri captureUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
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
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Prefer private app photos, then fill in older captures from the public gallery.
                AddPhotosFromDirectory(GetPhotosDirectory(), result, seenNames);
                AddPhotosFromDirectory(GetPublicGalleryDirectory(), result, seenNames);
            }
            catch (Exception)
            {
            }

            return result.OrderByDescending(p => p.CreatedAtUtc).ToList();
        }

        private static void AddPhotosFromDirectory(
            string dir,
            List<CameraPhotoModel> result,
            HashSet<string> seenNames)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;

            var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp", ".mp4",
            };

            foreach (string filePath in Directory.EnumerateFiles(dir))
            {
                string ext = Path.GetExtension(filePath);
                if (!supportedExts.Contains(ext))
                    continue;

                var info = new FileInfo(filePath);
                if (!seenNames.Add(info.Name))
                    continue;

                string fmt = ext.TrimStart('.').ToLowerInvariant();
                if (fmt == "jpeg")
                    fmt = "jpg";

                int width = 0;
                int height = 0;
                int quality = ParseQualityFromFileName(info.Name);

                if (fmt == "mp4")
                {
                    quality = ParseVideoBitrateFromFileName(info.Name);
                    TryReadVideoDimensions(filePath, out width, out height);
                }
                else
                {
                    var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(filePath, options);
                    width = Math.Max(0, options.OutWidth);
                    height = Math.Max(0, options.OutHeight);
                }

                result.Add(new CameraPhotoModel
                {
                    FileName = info.Name,
                    FilePath = filePath,
                    Format = fmt,
                    Quality = quality,
                    Width = width,
                    Height = height,
                    SizeBytes = info.Length,
                    CreatedAtUtc = info.CreationTimeUtc,
                    PublicGalleryPath = IsPublicPicDirectory(dir)
                        ? filePath
                        : null,
                });
            }
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
                    string path = ResolvePhotoPath(fileName);
                    if (path == null)
                        return null;

                    if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                        return BuildVideoThumbnailDataUrl(path, maxDimension);

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
        public string GetPhotoFilePath(string fileName)
        {
            return ResolvePhotoPath(fileName);
        }

        /// <inheritdoc/>
        public Task<bool> OpenPhotoAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = ResolvePhotoPath(fileName);
                    if (sourcePath == null)
                        return false;

                    string ext = Path.GetExtension(fileName);
                    if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            SilentNotes.VideoPlayerActivity.Start(
                                _appContext.Context,
                                sourcePath,
                                Path.GetFileNameWithoutExtension(fileName));
                        });
                        return true;
                    }

                    Context context = _appContext.Context;
                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
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
                    string sourcePath = ResolvePhotoPath(fileName);
                    if (sourcePath == null)
                        return null;

                    string ext = Path.GetExtension(fileName);
                    if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                        return WriteFileToPublicVideoGallery(sourcePath, Path.GetFileName(fileName));
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
                    string sourcePath = ResolvePhotoPath(fileName);
                    if (sourcePath == null)
                        return false;

                    Context context = _appContext.Context;
                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
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

                string sourcePath = ResolvePhotoPath(fileName);
                if (sourcePath == null)
                    return false;

                string dir = Path.GetDirectoryName(sourcePath) ?? GetPhotosDirectory();
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

                string path = ResolvePhotoPath(fileName);
                if (path == null)
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
                string publicDir = AnotePaths.Pic;
                Directory.CreateDirectory(publicDir);
                string target = Path.Combine(publicDir, displayName);
                File.Copy(sourcePath, target, overwrite: true);
                return target;
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
                case ".mp4":
                    return "video/mp4";
                default:
                    return "image/jpeg";
            }
        }

        private static string NormalizeVideoResolution(string resolution)
        {
            string res = (resolution ?? "720").Trim();
            return res == "1080" ? "1080" : "720";
        }

        private static int NormalizeVideoBitrateMbps(int bitrateMbps)
        {
            return bitrateMbps switch
            {
                1 => 1,
                4 => 4,
                8 => 8,
                _ => 2,
            };
        }

        private static int ParseVideoBitrateFromFileName(string fileName)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                int idx = name.LastIndexOf("_", StringComparison.Ordinal);
                if (idx >= 0 && idx + 1 < name.Length)
                {
                    string tail = name.Substring(idx + 1);
                    if (tail.EndsWith("mbps", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(tail.Substring(0, tail.Length - 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mbps))
                    {
                        return NormalizeVideoBitrateMbps(mbps);
                    }
                }
            }
            catch (Exception)
            {
            }

            return 2;
        }

        private static void TryReadVideoDimensions(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;
            MediaMetadataRetriever retriever = null;
            try
            {
                retriever = new MediaMetadataRetriever();
                retriever.SetDataSource(filePath);
                string w = retriever.ExtractMetadata(MetadataKey.VideoWidth);
                string h = retriever.ExtractMetadata(MetadataKey.VideoHeight);
                if (int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedW))
                    width = parsedW;
                if (int.TryParse(h, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedH))
                    height = parsedH;
            }
            catch (Exception)
            {
            }
            finally
            {
                try { retriever?.Release(); } catch (Exception) { }
            }
        }

        private static string BuildVideoThumbnailDataUrl(string filePath, int maxDimension)
        {
            MediaMetadataRetriever retriever = null;
            Bitmap frame = null;
            Bitmap scaled = null;
            try
            {
                retriever = new MediaMetadataRetriever();
                retriever.SetDataSource(filePath);
                frame = retriever.GetFrameAtTime(0, Option.ClosestSync);
                if (frame == null)
                    return null;

                int w = frame.Width;
                int h = frame.Height;
                Bitmap target = frame;
                if (Math.Max(w, h) > maxDimension && w > 0 && h > 0)
                {
                    float ratio = (float)maxDimension / Math.Max(w, h);
                    int nw = Math.Max(1, (int)(w * ratio));
                    int nh = Math.Max(1, (int)(h * ratio));
                    scaled = Bitmap.CreateScaledBitmap(frame, nw, nh, true);
                    target = scaled ?? frame;
                }

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
                frame?.Recycle();
                try { retriever?.Release(); } catch (Exception) { }
            }
        }

        private CameraPhotoModel BuildVideoModelFromFile(string fullPath, int bitrateMbps, bool autoSaveToGallery)
        {
            TryReadVideoDimensions(fullPath, out int width, out int height);
            string fileName = Path.GetFileName(fullPath);
            string publicPath = null;
            if (autoSaveToGallery)
                publicPath = WriteFileToPublicVideoGallery(fullPath, fileName);

            var info = new FileInfo(fullPath);
            return new CameraPhotoModel
            {
                FileName = fileName,
                FilePath = fullPath,
                Format = "mp4",
                Quality = bitrateMbps,
                Width = width,
                Height = height,
                SizeBytes = info.Exists ? info.Length : 0,
                CreatedAtUtc = DateTime.UtcNow,
                PublicGalleryPath = publicPath,
            };
        }

        private string WriteFileToPublicVideoGallery(string sourcePath, string displayName)
        {
            try
            {
                string publicDir = AnotePaths.Pic;
                Directory.CreateDirectory(publicDir);
                string target = Path.Combine(publicDir, displayName);
                File.Copy(sourcePath, target, overwrite: true);
                return target;
            }
            catch (Exception)
            {
                return null;
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
                    int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)global::Android.Media.Orientation.Undefined);
                    switch (orientation)
                    {
                        case (int)global::Android.Media.Orientation.Normal:
                            // If the camera HAL already rotated the pixel data to upright and wrote
                            // Orientation=Normal, only apply fallbackRotationDegrees if the pixel
                            // dimensions are still unrotated (landscape buffer when upright is portrait).
                            if ((fallbackRotationDegrees == 90 || fallbackRotationDegrees == 270) && decoded.Width > decoded.Height)
                                rotation = fallbackRotationDegrees;
                            else
                                rotation = 0;
                            break;
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
                            rotation = 0;
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

        private static string GetPublicGalleryDirectory() => AnotePaths.Pic;

        private static bool IsPublicPicDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return false;
            try
            {
                string pic = Path.GetFullPath(AnotePaths.Pic.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string normalized = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.Equals(normalized, pic, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string ResolvePhotoPath(string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
                return null;

            string privatePath = Path.Combine(GetPhotosDirectory(), safeName);
            if (File.Exists(privatePath))
                return privatePath;

            string publicDir = GetPublicGalleryDirectory();
            if (!string.IsNullOrEmpty(publicDir))
            {
                string publicPath = Path.Combine(publicDir, safeName);
                if (File.Exists(publicPath))
                    return publicPath;
            }

            return null;
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.Where(c => !invalid.Contains(c)).ToArray();
            return new string(chars).Trim();
        }

        private static bool CanUseMlKitForLiveScan(Context context)
        {
            try
            {
                MainApplication.EnsureMlKitInitialized(context);
                using IBarcodeScanner scanner = MlKitBarcodeScanning.GetClient(
                    new BarcodeScannerOptions.Builder()
                        .SetBarcodeFormats(Barcode.FormatAllFormats)
                        .Build());
                return scanner != null;
            }
            catch (Exception)
            {
                return false;
            }
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
            private ImageReader _imageReader;
            private Size _previewSize = new Size(1920, 1080);
            private Size _captureSize = new Size(1920, 1080);
            private bool _isCapturingStill;
            private bool _isCompleted;

            private readonly IQrService _qrService;
            private readonly IQrHistoryService _qrHistoryService;
            private readonly bool _liveBarcodeEnabled;
            private LinearLayout _barcodeBanner;
            private TextView _barcodeValueText;
            private TextView _barcodeOpenButton;
            private string _shownBarcodeText;
            private string _pendingBarcodeText;
            private int _barcodeMissStreak;
            private bool _barcodeScanInFlight;
            private bool _barcodeBannerDismissed;
            private long _lastBarcodeScanMs;
            private const int BarcodeScanIntervalMs = 550;
            private const int BarcodeHideAfterMisses = 4;
            private const int BarcodePreviewMaxSide = 720;

            public LiveCameraDialog(
                Activity activity,
                string initialFormat,
                int initialQuality,
                bool useFrontCamera,
                string flashMode,
                IQrService qrService,
                IQrHistoryService qrHistoryService,
                TaskCompletionSource<CapturedRawResult> tcs)
                : base(activity, global::Android.Resource.Style.ThemeBlackNoTitleBarFullScreen)
            {
                _activity = activity;
                _selectedFormat = NormalizeFormat(initialFormat);
                _selectedQuality = Math.Clamp(initialQuality, 1, 100);
                _useFrontCamera = useFrontCamera;
                _flashMode = string.IsNullOrEmpty(flashMode) ? "auto" : flashMode;
                _qrService = qrService;
                _qrHistoryService = qrHistoryService;
                _liveBarcodeEnabled = _qrService != null && CanUseMlKitForLiveScan(activity);
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

                if (_liveBarcodeEnabled)
                {
                    _barcodeBanner = CreateBarcodeBanner(activity, density, pad);
                    _barcodeBanner.Visibility = ViewStates.Gone;
                    root.AddView(_barcodeBanner, new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        ViewGroup.LayoutParams.WrapContent,
                        GravityFlags.Center));
                }

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
                ConfigureTransform(width, height);
                OpenCamera();
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
                ConfigureTransform(width, height);
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                CloseCamera();
                return true;
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
                if (!_liveBarcodeEnabled || _isCompleted || _isCapturingStill)
                    return;

                long now = SystemClock.ElapsedRealtime();
                if (_barcodeScanInFlight || (now - _lastBarcodeScanMs) < BarcodeScanIntervalMs)
                    return;

                _lastBarcodeScanMs = now;
                ScheduleLiveBarcodeScan();
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
                if (_isCapturingStill || _isCompleted)
                    return;

                try
                {
                    if (_cameraDevice != null && _captureSession != null && _imageReader?.Surface != null)
                    {
                        _isCapturingStill = true;
                        CaptureRequest.Builder stillBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                        stillBuilder.AddTarget(_imageReader.Surface);
                        stillBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);

                        if (_flashMode == "torch")
                        {
                            stillBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                            stillBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Torch);
                        }
                        else if (_flashMode == "on")
                        {
                            stillBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.OnAlwaysFlash);
                            stillBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Single);
                        }
                        else if (_flashMode == "auto")
                        {
                            stillBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.OnAutoFlash);
                            stillBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Off);
                        }
                        else
                        {
                            stillBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                            stillBuilder.Set(CaptureRequest.FlashMode, (int)global::Android.Hardware.Camera2.FlashMode.Off);
                        }

                        if (_sensorArraySize != null && _zoomLevel > 1.0f)
                        {
                            int cropW = (int)(_sensorArraySize.Width() / _zoomLevel);
                            int cropH = (int)(_sensorArraySize.Height() / _zoomLevel);
                            int cropX = (_sensorArraySize.Width() - cropW) / 2;
                            int cropY = (_sensorArraySize.Height() - cropH) / 2;
                            stillBuilder.Set(
                                CaptureRequest.ScalerCropRegion,
                                new Rect(cropX, cropY, cropX + cropW, cropY + cropH));
                        }

                        stillBuilder.Set(CaptureRequest.JpegQuality, (sbyte)100);
                        stillBuilder.Set(CaptureRequest.JpegOrientation, GetJpegOrientation());

                        _captureSession.Capture(stillBuilder.Build(), null, _backgroundHandler);
                        return;
                    }
                }
                catch (Exception)
                {
                    _isCapturingStill = false;
                }

                TakeTextureFallbackSnapshot();
            }

            private void TakeTextureFallbackSnapshot()
            {
                try
                {
                    if (!_textureView.IsAvailable)
                        return;

                    var (effW, effH) = GetEffectivePreviewSize();
                    Bitmap snapshot = (effW > 0 && effH > 0)
                        ? _textureView.GetBitmap(effW, effH)
                        : _textureView.Bitmap;
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

            private void OnStillJpegAvailable(ImageReader reader)
            {
                Image img = null;
                try
                {
                    img = reader.AcquireLatestImage();
                    if (img == null)
                        return;

                    Image.Plane[] planes = img.GetPlanes();
                    if (planes == null || planes.Length == 0 || planes[0].Buffer == null)
                        return;

                    Java.Nio.ByteBuffer buffer = planes[0].Buffer;
                    byte[] bytes = new byte[buffer.Remaining()];
                    buffer.Get(bytes);

                    if (_liveBarcodeEnabled)
                        _ = TryDecodeBarcodeFromJpegAsync(bytes, showToastIfNew: true);

                    int fallbackRot = GetJpegOrientation();
                    bool mirror = _useFrontCamera;

                    _activity.RunOnUiThread(() =>
                    {
                        FinishWithResult(new CapturedRawResult
                        {
                            JpegBytes = bytes,
                            Format = _selectedFormat,
                            Quality = _selectedQuality,
                            RotationDegrees = fallbackRot,
                            MirrorHorizontal = mirror,
                        });
                    });
                }
                catch (Exception)
                {
                    _activity.RunOnUiThread(TakeTextureFallbackSnapshot);
                }
                finally
                {
                    try { img?.Close(); } catch (Exception) { }
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
                        Size[] jpegSizes = map.GetOutputSizes((int)ImageFormatType.Jpeg);
                        if (jpegSizes != null && jpegSizes.Length > 0)
                        {
                            _captureSize = jpegSizes
                                .OrderByDescending(s => (long)s.Width * s.Height)
                                .FirstOrDefault(s => s.Width <= 4096 && s.Height <= 4096) ?? jpegSizes[0];
                        }

                        Size[] sizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
                        if (sizes != null && sizes.Length > 0)
                        {
                            double targetRatio = (_captureSize.Height > 0)
                                ? (double)_captureSize.Width / _captureSize.Height
                                : (4.0 / 3.0);

                            _previewSize = sizes
                                .Where(s => s.Width <= 2560 && s.Height <= 2560)
                                .OrderBy(s => Math.Abs(((double)s.Width / s.Height) - targetRatio))
                                .ThenByDescending(s => (long)s.Width * s.Height)
                                .FirstOrDefault() ?? sizes[0];
                        }
                    }

                    _activity.RunOnUiThread(() => ConfigureTransform(_textureView.Width, _textureView.Height));

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

                    _imageReader?.Close();
                    _imageReader = ImageReader.NewInstance(_captureSize.Width, _captureSize.Height, ImageFormatType.Jpeg, 2);
                    _imageReader.SetOnImageAvailableListener(new StillImageAvailableListener(this), _backgroundHandler);

                    _previewRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    _previewRequestBuilder.AddTarget(surface);

                    var outputs = new List<Surface> { surface };
                    if (_imageReader.Surface != null)
                        outputs.Add(_imageReader.Surface);

                    #pragma warning disable CS0618
                    _cameraDevice.CreateCaptureSession(
                        outputs,
                        new CaptureSessionListener(this),
                        _backgroundHandler);
                    #pragma warning restore CS0618
                }
                catch (Exception)
                {
                }
            }

            private (int Width, int Height) GetEffectivePreviewSize()
            {
                SurfaceOrientation displayRot = GetDisplayRotation();
                bool swapped = false;
                switch (displayRot)
                {
                    case SurfaceOrientation.Rotation0:
                    case SurfaceOrientation.Rotation180:
                        if (_sensorOrientation == 90 || _sensorOrientation == 270)
                            swapped = true;
                        break;
                    case SurfaceOrientation.Rotation90:
                    case SurfaceOrientation.Rotation270:
                        if (_sensorOrientation == 0 || _sensorOrientation == 180)
                            swapped = true;
                        break;
                }

                return swapped
                    ? (_previewSize.Height, _previewSize.Width)
                    : (_previewSize.Width, _previewSize.Height);
            }

            private void ConfigureTransform(int viewWidth, int viewHeight)
            {
                if (_textureView == null || viewWidth <= 0 || viewHeight <= 0 || _previewSize == null)
                    return;

                try
                {
                    SurfaceOrientation rotation = GetDisplayRotation();
                    var matrix = new Matrix();
                    var viewRect = new RectF(0, 0, viewWidth, viewHeight);
                    float centerX = viewRect.CenterX();
                    float centerY = viewRect.CenterY();

                    if (rotation == SurfaceOrientation.Rotation90 || rotation == SurfaceOrientation.Rotation270)
                    {
                        var bufferRect = new RectF(0, 0, _previewSize.Height, _previewSize.Width);
                        bufferRect.Offset(centerX - bufferRect.CenterX(), centerY - bufferRect.CenterY());
                        matrix.SetRectToRect(viewRect, bufferRect, Matrix.ScaleToFit.Fill);
                        float scale = Math.Max(
                            (float)viewHeight / _previewSize.Height,
                            (float)viewWidth / _previewSize.Width);
                        matrix.PostScale(scale, scale, centerX, centerY);
                        matrix.PostRotate(90 * ((int)rotation - 2), centerX, centerY);
                    }
                    else
                    {
                        var (effW, effH) = GetEffectivePreviewSize();
                        if (effW > 0 && effH > 0)
                        {
                            float viewRatio = (float)viewWidth / viewHeight;
                            float previewRatio = (float)effW / effH;
                            // Letterbox/fit inside the view so the preview and fallback snapshot are never squished
                            if (Math.Abs(viewRatio - previewRatio) > 0.01f)
                            {
                                float scaleX = 1f;
                                float scaleY = 1f;
                                if (viewRatio < previewRatio)
                                {
                                    // View is narrower/taller than preview -> scale Y down to preserve aspect ratio
                                    scaleY = viewRatio / previewRatio;
                                }
                                else
                                {
                                    scaleX = previewRatio / viewRatio;
                                }
                                matrix.PostScale(scaleX, scaleY, centerX, centerY);
                            }
                        }

                        if (rotation == SurfaceOrientation.Rotation180)
                            matrix.PostRotate(180, centerX, centerY);
                    }

                    _textureView.SetTransform(matrix);
                }
                catch (Exception)
                {
                }
            }

            private SurfaceOrientation GetDisplayRotation()
            {
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                        return _activity.Display?.Rotation ?? SurfaceOrientation.Rotation0;
                    #pragma warning disable CS0618
                    return _activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0;
                    #pragma warning restore CS0618
                }
                catch (Exception)
                {
                    return SurfaceOrientation.Rotation0;
                }
            }

            private int GetJpegOrientation()
            {
                int degrees = GetDisplayRotation() switch
                {
                    SurfaceOrientation.Rotation0 => 0,
                    SurfaceOrientation.Rotation90 => 90,
                    SurfaceOrientation.Rotation180 => 180,
                    SurfaceOrientation.Rotation270 => 270,
                    _ => 0,
                };

                if (_useFrontCamera)
                    return (_sensorOrientation + degrees) % 360;
                return (_sensorOrientation - degrees + 360) % 360;
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

                try
                {
                    _imageReader?.Close();
                    _imageReader = null;
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

            private LinearLayout CreateBarcodeBanner(Activity activity, float density, int pad)
            {
                var container = new LinearLayout(activity)
                {
                    Orientation = Orientation.Vertical,
                };
                container.SetBackgroundColor(Color.ParseColor("#DD1A1A1A"));
                container.SetPadding(pad, pad, pad, pad);

                var bg = new GradientDrawable();
                bg.SetCornerRadius(12 * density);
                bg.SetColor(Color.ParseColor("#DD1A1A1A"));
                bg.SetStroke((int)(1 * density), Color.ParseColor("#66FFFFFF"));
                container.Background = bg;

                _barcodeValueText = new TextView(activity)
                {
                    TextSize = 13f,
                };
                _barcodeValueText.SetMaxLines(2);
                _barcodeValueText.SetTextColor(Color.White);
                container.AddView(_barcodeValueText, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent));

                var actions = new LinearLayout(activity)
                {
                    Orientation = Orientation.Horizontal,
                };
                actions.SetGravity(GravityFlags.CenterVertical);
                actions.SetPadding(0, (int)(8 * density), 0, 0);

                _barcodeOpenButton = CreateHudPill(activity, "Open", density);
                _barcodeOpenButton.Click += (s, e) => TryOpenBarcodeUrl(_shownBarcodeText);
                actions.AddView(_barcodeOpenButton);

                actions.AddView(CreateSpacer(activity));

                TextView copyBtn = CreateHudPill(activity, "Copy", density);
                copyBtn.Click += (s, e) => CopyBarcodeText(_shownBarcodeText);
                actions.AddView(copyBtn);

                actions.AddView(CreateSpacer(activity));

                TextView historyBtn = CreateHudPill(activity, "QR history", density);
                historyBtn.Click += (s, e) => SaveBarcodeToHistory(_shownBarcodeText);
                actions.AddView(historyBtn);

                actions.AddView(CreateSpacer(activity));

                TextView dismissBtn = CreateHudPill(activity, "✕", density);
                dismissBtn.Click += (s, e) =>
                {
                    _barcodeBannerDismissed = true;
                    HideBarcodeBanner();
                };
                actions.AddView(dismissBtn);

                container.AddView(actions, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent));

                int margin = (int)(16 * density);
                container.LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent)
                {
                    LeftMargin = margin,
                    RightMargin = margin,
                };

                return container;
            }

            private void ScheduleLiveBarcodeScan()
            {
                if (_barcodeScanInFlight || !_textureView.IsAvailable)
                    return;

                int viewW = _textureView.Width;
                int viewH = _textureView.Height;
                if (viewW <= 0 || viewH <= 0)
                    return;

                _barcodeScanInFlight = true;
                float scale = Math.Min(1f, BarcodePreviewMaxSide / (float)Math.Max(viewW, viewH));
                int bmpW = Math.Max(1, (int)(viewW * scale));
                int bmpH = Math.Max(1, (int)(viewH * scale));

                Bitmap preview = null;
                try
                {
                    preview = _textureView.GetBitmap(bmpW, bmpH);
                }
                catch (Exception)
                {
                    _barcodeScanInFlight = false;
                    return;
                }

                if (preview == null)
                {
                    _barcodeScanInFlight = false;
                    return;
                }

                Bitmap captured = preview;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string decoded = await EncodeBitmapAndDecodeAsync(captured);
                        captured?.Recycle();
                        _activity.RunOnUiThread(() => ApplyLiveBarcodeResult(decoded));
                    }
                    catch (Exception)
                    {
                        captured?.Recycle();
                        _activity.RunOnUiThread(() => ApplyLiveBarcodeResult(null));
                    }
                    finally
                    {
                        _barcodeScanInFlight = false;
                    }
                });
            }

            private async Task TryDecodeBarcodeFromJpegAsync(byte[] jpegBytes, bool showToastIfNew)
            {
                if (jpegBytes == null || jpegBytes.Length == 0 || _qrService == null)
                    return;

                try
                {
                    string decoded = await _qrService.DecodeImageAsync(jpegBytes);
                    if (string.IsNullOrWhiteSpace(decoded))
                        return;

                    _activity.RunOnUiThread(() =>
                    {
                        ApplyLiveBarcodeResult(decoded);
                        if (showToastIfNew && !string.Equals(decoded, _shownBarcodeText, StringComparison.Ordinal))
                        {
                            Toast.MakeText(
                                _activity,
                                TruncateForDisplay(decoded, 80),
                                ToastLength.Short)?.Show();
                        }
                    });
                }
                catch (Exception)
                {
                }
            }

            private async Task<string> EncodeBitmapAndDecodeAsync(Bitmap bitmap)
            {
                if (bitmap == null || _qrService == null)
                    return null;

                using (var ms = new MemoryStream())
                {
                    if (!bitmap.Compress(Bitmap.CompressFormat.Jpeg, 72, ms))
                        return null;
                    return await _qrService.DecodeImageAsync(ms.ToArray());
                }
            }

            private void ApplyLiveBarcodeResult(string decoded)
            {
                if (_isCompleted || _barcodeBanner == null)
                    return;

                if (string.IsNullOrWhiteSpace(decoded))
                {
                    _barcodeMissStreak++;
                    if (_barcodeMissStreak >= BarcodeHideAfterMisses)
                        HideBarcodeBanner();
                    return;
                }

                _barcodeMissStreak = 0;
                _pendingBarcodeText = decoded.Trim();
                if (_barcodeBannerDismissed
                    && string.Equals(_pendingBarcodeText, _shownBarcodeText, StringComparison.Ordinal))
                {
                    return;
                }

                if (!string.Equals(_pendingBarcodeText, _shownBarcodeText, StringComparison.Ordinal))
                    _barcodeBannerDismissed = false;

                ShowBarcodeBanner(_pendingBarcodeText);
            }

            private void ShowBarcodeBanner(string text)
            {
                if (_barcodeBanner == null || string.IsNullOrWhiteSpace(text))
                    return;

                _shownBarcodeText = text;
                _barcodeValueText.Text = TruncateForDisplay(text, 160);
                if (_barcodeOpenButton != null)
                {
                    _barcodeOpenButton.Visibility = LooksLikeHttpUrl(text.Trim())
                        ? ViewStates.Visible
                        : ViewStates.Gone;
                }
                _barcodeBanner.Visibility = ViewStates.Visible;
            }

            private void HideBarcodeBanner()
            {
                if (_barcodeBanner == null)
                    return;
                _barcodeBanner.Visibility = ViewStates.Gone;
                _shownBarcodeText = null;
                _pendingBarcodeText = null;
            }

            private void CopyBarcodeText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                try
                {
                    var clipboard = (ClipboardManager)_activity.GetSystemService(Context.ClipboardService);
                    clipboard.PrimaryClip = ClipData.NewPlainText("barcode", text);
                    Toast.MakeText(_activity, "Copied", ToastLength.Short)?.Show();
                }
                catch (Exception)
                {
                }
            }

            private void SaveBarcodeToHistory(string text)
            {
                if (string.IsNullOrWhiteSpace(text) || _qrHistoryService == null)
                    return;

                try
                {
                    _qrHistoryService.AddText(text, QrHistoryModel.MaxHistoryItems);
                    Toast.MakeText(_activity, "Saved to QR history", ToastLength.Short)?.Show();
                }
                catch (Exception)
                {
                }
            }

            private void TryOpenBarcodeUrl(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                string trimmed = text.Trim();
                if (!LooksLikeHttpUrl(trimmed))
                {
                    CopyBarcodeText(trimmed);
                    return;
                }

                try
                {
                    var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(trimmed));
                    intent.AddFlags(ActivityFlags.NewTask);
                    _activity.StartActivity(intent);
                }
                catch (Exception)
                {
                    Toast.MakeText(_activity, "Could not open link", ToastLength.Short)?.Show();
                }
            }

            private static bool LooksLikeHttpUrl(string text)
            {
                return Uri.TryCreate(text, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            }

            private static string TruncateForDisplay(string text, int maxLen)
            {
                if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
                    return text;
                return text.Substring(0, maxLen - 1) + "…";
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

            private class StillImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
            {
                private readonly LiveCameraDialog _dialog;

                public StillImageAvailableListener(LiveCameraDialog dialog)
                {
                    _dialog = dialog;
                }

                public void OnImageAvailable(ImageReader reader)
                {
                    _dialog.OnStillJpegAvailable(reader);
                }
            }
        }

        /// <summary>
        /// Full-screen Camera2 viewfinder for recording H.264 MP4 video via <see cref="MediaRecorder"/>.
        /// </summary>
        private class LiveVideoCameraDialog : Dialog, TextureView.ISurfaceTextureListener
        {
            private const int VideoFrameRate = 30;

            private readonly Activity _activity;
            private readonly string _photosDir;
            private readonly string _targetResolution;
            private readonly int _bitrateMbps;
            private readonly bool _recordAudio;
            private readonly TaskCompletionSource<string> _tcs;

            private readonly TextureView _textureView;
            private readonly TextView _recordButton;
            private readonly TextView _flashButton;
            private readonly TextView _hintText;

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
            private Surface _previewSurface;
            private Size _previewSize = new Size(1280, 720);
            private Size _videoSize = new Size(1280, 720);

            private MediaRecorder _mediaRecorder;
            private string _outputPath;
            private bool _isRecording;
            private bool _isCompleted;

            public LiveVideoCameraDialog(
                Activity activity,
                string photosDir,
                string targetResolution,
                int bitrateMbps,
                bool useFrontCamera,
                string flashMode,
                bool recordAudio,
                TaskCompletionSource<string> tcs)
                : base(activity, global::Android.Resource.Style.ThemeBlackNoTitleBarFullScreen)
            {
                _activity = activity;
                _photosDir = photosDir;
                _targetResolution = targetResolution;
                _bitrateMbps = bitrateMbps;
                _useFrontCamera = useFrontCamera;
                _flashMode = string.IsNullOrEmpty(flashMode) ? "auto" : flashMode;
                _recordAudio = recordAudio;
                _tcs = tcs;

                int targetW = _targetResolution == "1080" ? 1920 : 1280;
                int targetH = _targetResolution == "1080" ? 1080 : 720;
                _videoSize = new Size(targetW, targetH);

                float density = activity.Resources.DisplayMetrics.Density;
                int pad = (int)(12 * density);

                var root = new FrameLayout(activity);
                root.SetBackgroundColor(Color.Black);

                _textureView = new TextureView(activity) { SurfaceTextureListener = this };
                root.AddView(_textureView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent,
                    GravityFlags.Center));

                var topContainer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
                topContainer.SetBackgroundColor(Color.ParseColor("#AA000000"));
                topContainer.SetPadding(pad, pad, pad, pad);

                var topRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
                topRow.SetGravity(GravityFlags.CenterVertical);

                TextView closeBtn = CreateHudPill(activity, "✕", density);
                closeBtn.Click += (s, e) => FinishWithResult(null);
                topRow.AddView(closeBtn);
                topRow.AddView(CreateSpacer(activity));

                _flashButton = CreateHudPill(activity, GetFlashLabel(_flashMode), density);
                _flashButton.Click += (s, e) => CycleFlash();
                topRow.AddView(_flashButton);
                topRow.AddView(CreateSpacer(activity));

                TextView switchCamBtn = CreateHudPill(activity, "🔄", density);
                switchCamBtn.Click += (s, e) => SwitchCamera();
                topRow.AddView(switchCamBtn);
                topContainer.AddView(topRow);

                _hintText = new TextView(activity)
                {
                    Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}p · {1} Mbps · {2} fps{3}",
                        _targetResolution,
                        _bitrateMbps,
                        VideoFrameRate,
                        _recordAudio ? " · mic" : string.Empty),
                    TextSize = 13f,
                };
                _hintText.SetTextColor(Color.ParseColor("#CCCCCC"));
                _hintText.SetPadding(0, (int)(6 * density), 0, 0);
                topContainer.AddView(_hintText);

                root.AddView(topContainer, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent,
                    GravityFlags.Top));

                var bottomContainer = new LinearLayout(activity)
                {
                    Orientation = Orientation.Vertical,
                };
                bottomContainer.SetBackgroundColor(Color.ParseColor("#AA000000"));
                bottomContainer.SetPadding(pad, pad, pad, (int)(24 * density));
                bottomContainer.SetGravity(GravityFlags.CenterHorizontal);

                _recordButton = new TextView(activity)
                {
                    Text = "● REC",
                    TextSize = 18f,
                    Gravity = GravityFlags.Center,
                };
                _recordButton.SetTextColor(Color.White);
                var recordBg = new GradientDrawable();
                recordBg.SetShape(ShapeType.Rectangle);
                recordBg.SetCornerRadius(12 * density);
                recordBg.SetColor(Color.ParseColor("#CCB91C1C"));
                recordBg.SetStroke((int)(2 * density), Color.White);
                _recordButton.Background = recordBg;
                int btnH = (int)(54 * density);
                _recordButton.Click += (s, e) => ToggleRecording();
                bottomContainer.AddView(_recordButton, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, btnH));

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
                if (_isRecording)
                    StopRecording();
                else
                    FinishWithResult(null);
            }

            public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            {
                ConfigureTransform(width, height);
                OpenCamera();
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
                ConfigureTransform(width, height);
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                CloseCamera();
                return true;
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
            }

            private void ToggleRecording()
            {
                if (_isRecording)
                    StopRecording();
                else
                    StartRecording();
            }

            private void StartRecording()
            {
                if (_isCompleted || _isRecording || _mediaRecorder == null)
                    return;

                try
                {
                    _mediaRecorder.Start();
                    _isRecording = true;
                    _recordButton.Text = "■ STOP";
                    _recordButton.SetTextColor(Color.ParseColor("#FFEEEE"));
                }
                catch (Exception)
                {
                    FinishWithResult(null);
                }
            }

            private void StopRecording()
            {
                if (!_isRecording)
                    return;

                _isRecording = false;
                string path = _outputPath;
                try
                {
                    _mediaRecorder?.Stop();
                }
                catch (Exception)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            File.Delete(path);
                    }
                    catch (Exception)
                    {
                    }
                    path = null;
                }

                FinishWithResult(path);
            }

            private void FinishWithResult(string path)
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

                _tcs.TrySetResult(path);
            }

            private void CycleFlash()
            {
                if (_isRecording)
                    return;
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

            private void SwitchCamera()
            {
                if (_isRecording)
                    return;
                _useFrontCamera = !_useFrontCamera;
                CloseCamera();
                OpenCamera();
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
                        Size[] recorderSizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(MediaRecorder)));
                        if (recorderSizes != null && recorderSizes.Length > 0)
                            _videoSize = ChooseVideoSize(recorderSizes, _videoSize.Width, _videoSize.Height);

                        Size[] sizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
                        if (sizes != null && sizes.Length > 0)
                        {
                            double targetRatio = (_videoSize.Height > 0)
                                ? (double)_videoSize.Width / _videoSize.Height
                                : (16.0 / 9.0);
                            _previewSize = sizes
                                .Where(s => s.Width <= 1920 && s.Height <= 1920)
                                .OrderBy(s => Math.Abs(((double)s.Width / s.Height) - targetRatio))
                                .ThenByDescending(s => (long)s.Width * s.Height)
                                .FirstOrDefault() ?? sizes[0];
                        }
                    }

                    _activity.RunOnUiThread(() => ConfigureTransform(_textureView.Width, _textureView.Height));
                    manager.OpenCamera(selectedId, new VideoCameraStateListener(this), _backgroundHandler);
                }
                catch (Exception)
                {
                }
            }

            private static Size ChooseVideoSize(Size[] choices, int targetWidth, int targetHeight)
            {
                if (choices == null || choices.Length == 0)
                    return new Size(targetWidth, targetHeight);

                return choices
                    .OrderBy(s => Math.Abs(s.Width - targetWidth) + Math.Abs(s.Height - targetHeight))
                    .ThenByDescending(s => (long)s.Width * s.Height)
                    .First();
            }

            private void StartPreviewSession()
            {
                try
                {
                    if (_cameraDevice == null || !_textureView.IsAvailable)
                        return;

                    if (!SetupMediaRecorder())
                        return;

                    SurfaceTexture texture = _textureView.SurfaceTexture;
                    if (texture == null)
                        return;

                    texture.SetDefaultBufferSize(_previewSize.Width, _previewSize.Height);
                    _previewSurface?.Release();
                    _previewSurface = new Surface(texture);

                    _previewRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Record);
                    _previewRequestBuilder.AddTarget(_previewSurface);
                    _previewRequestBuilder.AddTarget(_mediaRecorder.Surface);

                    var outputs = new List<Surface> { _previewSurface, _mediaRecorder.Surface };

                    #pragma warning disable CS0618
                    _cameraDevice.CreateCaptureSession(
                        outputs,
                        new VideoCaptureSessionListener(this),
                        _backgroundHandler);
                    #pragma warning restore CS0618
                }
                catch (Exception)
                {
                    FinishWithResult(null);
                }
            }

            private bool SetupMediaRecorder()
            {
                try
                {
                    ReleaseMediaRecorder();

                    Directory.CreateDirectory(_photosDir);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                    string fileName = string.Format(
                        CultureInfo.InvariantCulture,
                        "VID_{0}_{1}p_{2}mbps.mp4",
                        timestamp,
                        _targetResolution,
                        _bitrateMbps);
                    _outputPath = Path.Combine(_photosDir, fileName);

#pragma warning disable CA1422
                    _mediaRecorder = Build.VERSION.SdkInt >= BuildVersionCodes.S
                        ? new MediaRecorder(_activity)
                        : new MediaRecorder();
#pragma warning restore CA1422

                    if (_recordAudio)
                    {
                        _mediaRecorder.SetAudioSource(AudioSource.Mic);
                    }

                    _mediaRecorder.SetVideoSource(VideoSource.Surface);
                    _mediaRecorder.SetOutputFormat(OutputFormat.Mpeg4);
                    if (_recordAudio)
                    {
                        _mediaRecorder.SetAudioEncoder(AudioEncoder.Aac);
                        _mediaRecorder.SetAudioEncodingBitRate(128000);
                        _mediaRecorder.SetAudioSamplingRate(44100);
                    }

                    _mediaRecorder.SetVideoEncoder(VideoEncoder.H264);
                    _mediaRecorder.SetVideoSize(_videoSize.Width, _videoSize.Height);
                    _mediaRecorder.SetVideoFrameRate(VideoFrameRate);
                    _mediaRecorder.SetVideoEncodingBitRate(_bitrateMbps * 1_000_000);
                    _mediaRecorder.SetOutputFile(_outputPath);
                    _mediaRecorder.SetOrientationHint(GetVideoOrientationHint());
                    _mediaRecorder.Prepare();
                    return true;
                }
                catch (Exception)
                {
                    ReleaseMediaRecorder();
                    return false;
                }
            }

            private void ReleaseMediaRecorder()
            {
                if (_mediaRecorder == null)
                    return;
                try
                {
                    if (_isRecording)
                        _mediaRecorder.Stop();
                }
                catch (Exception)
                {
                }

                try { _mediaRecorder.Reset(); } catch (Exception) { }
                try { _mediaRecorder.Release(); } catch (Exception) { }
                _mediaRecorder = null;
                _isRecording = false;
            }

            private void ApplyPreviewParameters()
            {
                try
                {
                    if (_captureSession == null || _previewRequestBuilder == null)
                        return;

                    _previewRequestBuilder.Set(
                        CaptureRequest.ControlAfMode,
                        (int)ControlAFMode.ContinuousVideo);
                    _previewRequestBuilder.Set(
                        CaptureRequest.ControlAeTargetFpsRange,
                        new Android.Util.Range(VideoFrameRate, VideoFrameRate));

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

            private int GetVideoOrientationHint()
            {
                int degrees = GetDisplayRotation() switch
                {
                    SurfaceOrientation.Rotation0 => 0,
                    SurfaceOrientation.Rotation90 => 90,
                    SurfaceOrientation.Rotation180 => 180,
                    SurfaceOrientation.Rotation270 => 270,
                    _ => 0,
                };

                if (_useFrontCamera)
                    return (_sensorOrientation + degrees) % 360;
                return (_sensorOrientation - degrees + 360) % 360;
            }

            private void ConfigureTransform(int viewWidth, int viewHeight)
            {
                if (_textureView == null || viewWidth <= 0 || viewHeight <= 0 || _previewSize == null)
                    return;

                try
                {
                    SurfaceOrientation rotation = GetDisplayRotation();
                    var matrix = new Matrix();
                    var viewRect = new RectF(0, 0, viewWidth, viewHeight);
                    float centerX = viewRect.CenterX();
                    float centerY = viewRect.CenterY();

                    if (rotation == SurfaceOrientation.Rotation90 || rotation == SurfaceOrientation.Rotation270)
                    {
                        var bufferRect = new RectF(0, 0, _previewSize.Height, _previewSize.Width);
                        bufferRect.Offset(centerX - bufferRect.CenterX(), centerY - bufferRect.CenterY());
                        matrix.SetRectToRect(viewRect, bufferRect, Matrix.ScaleToFit.Fill);
                        float scale = Math.Max(
                            (float)viewHeight / _previewSize.Height,
                            (float)viewWidth / _previewSize.Width);
                        matrix.PostScale(scale, scale, centerX, centerY);
                        matrix.PostRotate(90 * ((int)rotation - 2), centerX, centerY);
                    }
                    else if (rotation == SurfaceOrientation.Rotation180)
                    {
                        matrix.PostRotate(180, centerX, centerY);
                    }

                    _textureView.SetTransform(matrix);
                }
                catch (Exception)
                {
                }
            }

            private SurfaceOrientation GetDisplayRotation()
            {
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                        return _activity.Display?.Rotation ?? SurfaceOrientation.Rotation0;
#pragma warning disable CS0618
                    return _activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0;
#pragma warning restore CS0618
                }
                catch (Exception)
                {
                    return SurfaceOrientation.Rotation0;
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

                ReleaseMediaRecorder();

                try
                {
                    _previewSurface?.Release();
                    _previewSurface = null;
                }
                catch (Exception)
                {
                }
            }

            private void StartBackgroundThread()
            {
                if (_backgroundThread != null)
                    return;
                _backgroundThread = new HandlerThread("aNoteVideoCameraThread");
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

            private static TextView CreateHudPill(Activity context, string text, float density)
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

            private class VideoCameraStateListener : CameraDevice.StateCallback
            {
                private readonly LiveVideoCameraDialog _dialog;

                public VideoCameraStateListener(LiveVideoCameraDialog dialog) => _dialog = dialog;

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

            private class VideoCaptureSessionListener : CameraCaptureSession.StateCallback
            {
                private readonly LiveVideoCameraDialog _dialog;

                public VideoCaptureSessionListener(LiveVideoCameraDialog dialog) => _dialog = dialog;

                public override void OnConfigured(CameraCaptureSession session)
                {
                    if (_dialog._cameraDevice == null)
                        return;
                    _dialog._captureSession = session;
                    _dialog.ApplyPreviewParameters();
                }

                public override void OnConfigureFailed(CameraCaptureSession session)
                {
                    _dialog.FinishWithResult(null);
                }
            }
        }
    }
}
