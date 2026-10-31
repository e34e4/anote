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
using Activity = Android.App.Activity;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;
using Path = System.IO.Path;
using Stream = System.IO.Stream;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IScreenRecorderService"/> using MediaProjection.
    /// </summary>
    internal class ScreenRecorderService : IScreenRecorderService
    {
        public const string FolderName = "screen_recordings";
        private const string ShareFolder = "clipboard";
        private const string PublicSubFolder = "aNote";

        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;
        private readonly System.Timers.Timer _timer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScreenRecorderService"/> class.
        /// </summary>
        public ScreenRecorderService(IAppContextService appContext, IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContext;
            _activityResultAwaiter = activityResultAwaiter;
            _timer = new System.Timers.Timer(250);
            _timer.Elapsed += (s, e) => StateChanged?.Invoke();
            ScreenRecordService.StateChanged += OnNativeStateChanged;
        }

        /// <inheritdoc/>
        public event Action StateChanged;

        /// <inheritdoc/>
        public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop;

        /// <inheritdoc/>
        public bool IsRecording => ScreenRecordService.IsRecording;

        /// <inheritdoc/>
        public TimeSpan RecordingElapsed => ScreenRecordService.Elapsed;

        /// <inheritdoc/>
        public async Task<bool> StartRecordingAsync()
        {
            if (!IsSupported || IsRecording)
                return false;

            Activity activity = _appContext.RootActivity;
            if (activity == null || _activityResultAwaiter == null)
                return false;

            try
            {
                // Mic permission improves chance of capturing device/mix audio with AAC.
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => Permissions.RequestAsync<Permissions.Microphone>());
                }
                catch (Exception)
                {
                }

                var mgr = (MediaProjectionManager)activity.GetSystemService(Context.MediaProjectionService);
                Intent captureIntent = mgr.CreateScreenCaptureIntent();
                ActivityResult result = await _activityResultAwaiter.StartActivityAndWaitForResult(activity, captureIntent);
                if (result == null || result.ResultCode != Result.Ok || result.Data == null)
                    return false;

                var serviceIntent = new Intent(activity, typeof(ScreenRecordService));
                serviceIntent.SetAction(ScreenRecordService.ActionStart);
                serviceIntent.PutExtra(ScreenRecordService.ExtraResultCode, (int)result.ResultCode);
                serviceIntent.PutExtra(ScreenRecordService.ExtraResultData, result.Data);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    activity.StartForegroundService(serviceIntent);
                else
                    activity.StartService(serviceIntent);

                // Brief wait for the service to enter recording state.
                for (int i = 0; i < 20 && !ScreenRecordService.IsRecording; i++)
                    await Task.Delay(100);

                if (ScreenRecordService.IsRecording)
                    _timer.Start();

                StateChanged?.Invoke();
                return ScreenRecordService.IsRecording;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<AudioRecordingModel> StopRecordingAsync()
        {
            if (!IsRecording)
                return null;

            Context context = _appContext.Context;
            ScreenRecordService.RequestStop(context);

            for (int i = 0; i < 30 && ScreenRecordService.IsRecording; i++)
                await Task.Delay(100);

            _timer.Stop();
            string path = ScreenRecordService.LastOutputPath;
            StateChanged?.Invoke();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var info = new FileInfo(path);
            return new AudioRecordingModel
            {
                FileName = info.Name,
                FilePath = info.FullName,
                SizeBytes = info.Length,
                DurationSeconds = ReadDurationSeconds(path),
                CreatedAtUtc = info.LastWriteTimeUtc,
            };
        }

        /// <inheritdoc/>
        public void CancelRecording()
        {
            if (!IsRecording)
                return;
            Context context = _appContext.Context;
            // Stop then delete last file if service saved it — send stop and remove unfinished file.
            string dir = GetRecordingsDirectory(context);
            ScreenRecordService.RequestStop(context);
            _timer.Stop();
            try
            {
                // Best-effort: remove newest incomplete file if zero-length.
                if (Directory.Exists(dir))
                {
                    FileInfo newest = new DirectoryInfo(dir).GetFiles("*.mp4")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (newest != null && newest.Length < 1024)
                        newest.Delete();
                }
            }
            catch (Exception)
            {
            }
            StateChanged?.Invoke();
        }

        /// <inheritdoc/>
        public IReadOnlyList<AudioRecordingModel> GetRecordings()
        {
            var result = new List<AudioRecordingModel>();
            try
            {
                string dir = GetRecordingsDirectory(_appContext.Context);
                if (!Directory.Exists(dir))
                    return result;

                foreach (FileInfo file in new DirectoryInfo(dir).GetFiles("*.mp4").OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    result.Add(new AudioRecordingModel
                    {
                        FileName = file.Name,
                        FilePath = file.FullName,
                        SizeBytes = file.Length,
                        DurationSeconds = ReadDurationSeconds(file.FullName),
                        CreatedAtUtc = file.LastWriteTimeUtc,
                    });
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        /// <inheritdoc/>
        public bool RenameRecording(string oldFileName, string newFileName)
        {
            if (string.IsNullOrWhiteSpace(oldFileName) || string.IsNullOrWhiteSpace(newFileName))
                return false;
            try
            {
                string dir = GetRecordingsDirectory(_appContext.Context);
                string oldPath = Path.Combine(dir, Path.GetFileName(oldFileName));
                if (!File.Exists(oldPath))
                    return false;

                string sanitized = SanitizeFileName(newFileName.Trim());
                if (string.IsNullOrEmpty(sanitized))
                    return false;
                if (!sanitized.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    sanitized += ".mp4";

                string newPath = Path.Combine(dir, sanitized);
                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (File.Exists(newPath))
                    return false;

                File.Move(oldPath, newPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool DeleteRecording(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            try
            {
                string path = Path.Combine(GetRecordingsDirectory(_appContext.Context), Path.GetFileName(fileName));
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

        /// <inheritdoc/>
        public Task<string> SaveToPublicStorageAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetRecordingsDirectory(_appContext.Context), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return null;

                    Context context = _appContext.Context;
                    string displayName = Path.GetFileName(fileName);

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var values = new ContentValues();
                        values.Put("_display_name", displayName);
                        values.Put("mime_type", "video/mp4");
                        values.Put("relative_path", "Movies/" + PublicSubFolder);

                        global::Android.Net.Uri uri = context.ContentResolver.Insert(
                            MediaStore.Video.Media.ExternalContentUri, values);
                        if (uri == null)
                            return null;

                        using (Stream input = File.OpenRead(sourcePath))
                        using (Stream output = context.ContentResolver.OpenOutputStream(uri))
                        {
                            input.CopyTo(output);
                        }
                        return "Movies/" + PublicSubFolder + "/" + displayName;
                    }

                    string dir = Path.Combine(
                        global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                            global::Android.OS.Environment.DirectoryMovies).AbsolutePath,
                        PublicSubFolder);
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, displayName);
                    File.Copy(sourcePath, target, overwrite: true);
                    return target;
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public Task<bool> ShareRecordingAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetRecordingsDirectory(_appContext.Context), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return false;

                    Context context = _appContext.Context;
                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                        context, context.PackageName + ".fileprovider", shareFile);

                    var shareIntent = new Intent(Intent.ActionSend);
                    shareIntent.SetType("video/mp4");
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

        internal static string GetRecordingsDirectory(Context context)
        {
            string root = context?.NoBackupFilesDir?.AbsolutePath
                ?? context?.FilesDir?.AbsolutePath
                ?? Path.GetTempPath();
            return Path.Combine(root, FolderName);
        }

        private void OnNativeStateChanged()
        {
            if (!ScreenRecordService.IsRecording)
                _timer.Stop();
            else if (!_timer.Enabled)
                _timer.Start();
            StateChanged?.Invoke();
        }

        private static double ReadDurationSeconds(string path)
        {
            try
            {
                var retriever = new MediaMetadataRetriever();
                retriever.SetDataSource(path);
                string duration = retriever.ExtractMetadata(MetadataKey.Duration);
                retriever.Release();
                if (long.TryParse(duration, out long ms) && ms > 0)
                    return ms / 1000.0;
            }
            catch (Exception)
            {
            }
            return 0;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
