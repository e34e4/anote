// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Provider;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using Path = System.IO.Path;

namespace SilentNotes.Platforms.Services
{
    /// <inheritdoc/>
    internal class ScreenshotGalleryService : IScreenshotGalleryService
    {
        private const string FolderName = "screenshots";
        private const string ShareFolder = "shot_share";
        private readonly IAppContextService _appContext;

        public ScreenshotGalleryService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public IReadOnlyList<AudioRecordingModel> GetScreenshots()
        {
            string dir = GetScreenshotsDirectory(_appContext.Context);
            if (!Directory.Exists(dir))
                return Array.Empty<AudioRecordingModel>();

            return Directory.GetFiles(dir)
                .Where(IsImageFile)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new AudioRecordingModel
                    {
                        FileName = info.Name,
                        FilePath = info.FullName,
                        CreatedAtUtc = info.CreationTimeUtc,
                        SizeBytes = info.Length,
                        DurationSeconds = 0,
                    };
                })
                .OrderByDescending(m => m.CreatedAtUtc)
                .ToList();
        }

        /// <inheritdoc/>
        public bool RenameScreenshot(string oldFileName, string newFileName)
        {
            try
            {
                string dir = GetScreenshotsDirectory(_appContext.Context);
                string oldPath = Path.Combine(dir, Path.GetFileName(oldFileName));
                if (!File.Exists(oldPath))
                    return false;

                string ext = Path.GetExtension(oldPath);
                string safe = SanitizeFileName(newFileName);
                if (!Path.HasExtension(safe))
                    safe += ext;
                string newPath = Path.Combine(dir, safe);
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
        public bool DeleteScreenshot(string fileName)
        {
            try
            {
                string path = Path.Combine(GetScreenshotsDirectory(_appContext.Context), Path.GetFileName(fileName));
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
                    Context context = _appContext.Context;
                    string sourcePath = Path.Combine(GetScreenshotsDirectory(context), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return null;

                    string displayName = Path.GetFileName(fileName);
                    string mime = GuessMime(displayName);

                    string dir = AnotePaths.Pic;
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
        public Task<bool> ShareScreenshotAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    Context context = _appContext.Context;
                    string sourcePath = Path.Combine(GetScreenshotsDirectory(context), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return false;

                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, Path.GetFileName(fileName));
                    File.Copy(sourcePath, shareFile.AbsolutePath, overwrite: true);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                        context, context.PackageName + ".fileprovider", shareFile);

                    var shareIntent = new Intent(Intent.ActionSend);
                    shareIntent.SetType(GuessMime(fileName));
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

        internal static string GetScreenshotsDirectory(Context context)
        {
            string root = context?.GetExternalFilesDir(global::Android.OS.Environment.DirectoryPictures)?.AbsolutePath
                ?? context?.FilesDir?.AbsolutePath
                ?? Path.GetTempPath();
            return Path.Combine(root, FolderName);
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
        }

        private static string GuessMime(string fileName)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
        }

        private static string SanitizeFileName(string name)
        {
            name = Path.GetFileName(name?.Trim() ?? string.Empty);
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "screenshot" : name;
        }
    }
}
