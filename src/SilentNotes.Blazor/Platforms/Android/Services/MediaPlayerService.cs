// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Provider;
using SilentNotes.Models;
using SilentNotes.Services;
using AndroidEnv = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;
using Settings = Android.Provider.Settings;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IMediaPlayerService"/> using MediaPlayer for audio,
    /// VideoPlayerActivity for video, and ImageViewerActivity for pictures (gallery-ready).
    /// </summary>
    internal class MediaPlayerService : IMediaPlayerService, IDisposable
    {
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".ogg", ".oga", ".m4a", ".aac", ".wma", ".opus", ".aiff", ".aif", ".amr", ".mid", ".midi",
        };

        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".webm", ".mov", ".3gp", ".3gpp", ".m4v", ".wmv", ".flv", ".ts", ".mpeg", ".mpg",
        };

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".heif", ".svg",
        };

        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;
        private readonly System.Timers.Timer _timer;

        private MediaPlayer _player;
        private string _currentDirectory;
        private string _activePath;
        private string _activeTitle;
        private MediaKind? _activeKind;
        private bool _isPlaying;
        private bool _isPaused;
        private TimeSpan _duration;

        public MediaPlayerService(IAppContextService appContext, IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContext;
            _activityResultAwaiter = activityResultAwaiter;
            _currentDirectory = GetDefaultRootPath();
            _timer = new System.Timers.Timer(400);
            _timer.Elapsed += (_, __) => RaiseStateChanged();
            _timer.AutoReset = true;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasAllFilesPermission
        {
            get
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                    return AndroidEnv.IsExternalStorageManager;
                return true;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestAllFilesPermissionAsync()
        {
            if (HasAllFilesPermission)
                return true;

            if (Build.VERSION.SdkInt < BuildVersionCodes.R)
                return true;

            try
            {
                Activity activity = _appContext.RootActivity;
                Intent intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
                intent.SetData(AndroidUri.Parse("package:" + activity.PackageName));
                await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
            }
            catch (Exception)
            {
                try
                {
                    Activity activity = _appContext.RootActivity;
                    Intent intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                    await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
                }
                catch (Exception)
                {
                }
            }

            return HasAllFilesPermission;
        }

        /// <inheritdoc/>
        public string CurrentDirectory => _currentDirectory;

        /// <inheritdoc/>
        public string CurrentDirectoryDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(_currentDirectory))
                    return "/";
                string root = GetDefaultRootPath();
                if (string.Equals(_currentDirectory, root, StringComparison.OrdinalIgnoreCase))
                    return "Internal storage";
                return _currentDirectory;
            }
        }

        /// <inheritdoc/>
        public bool CanNavigateUp
        {
            get
            {
                string root = GetDefaultRootPath();
                if (string.IsNullOrEmpty(_currentDirectory) || string.IsNullOrEmpty(root))
                    return false;
                return !string.Equals(
                    Path.GetFullPath(_currentDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <inheritdoc/>
        public event Action StateChanged;

        /// <inheritdoc/>
        public string ActivePath => _activePath;

        /// <inheritdoc/>
        public string ActiveTitle => _activeTitle;

        /// <inheritdoc/>
        public MediaKind? ActiveKind => _activeKind;

        /// <inheritdoc/>
        public bool IsPlaying => _isPlaying;

        /// <inheritdoc/>
        public bool IsPaused => _isPaused;

        /// <inheritdoc/>
        public TimeSpan Position
        {
            get
            {
                try
                {
                    if (_player != null)
                        return TimeSpan.FromMilliseconds(_player.CurrentPosition);
                }
                catch (Exception)
                {
                }
                return TimeSpan.Zero;
            }
        }

        /// <inheritdoc/>
        public TimeSpan Duration => _duration;

        /// <inheritdoc/>
        public IReadOnlyList<(string Key, string Label, string Path)> GetStorageRoots()
        {
            var roots = new List<(string, string, string)>();
            string internalRoot = GetDefaultRootPath();
            if (!string.IsNullOrEmpty(internalRoot))
                roots.Add(("internal", "Storage", internalRoot));

            AddRootIfExists(roots, "music", "Music", AndroidEnv.DirectoryMusic);
            AddRootIfExists(roots, "movies", "Movies", AndroidEnv.DirectoryMovies);
            AddRootIfExists(roots, "dcim", "DCIM", AndroidEnv.DirectoryDcim);
            AddRootIfExists(roots, "pictures", "Pictures", AndroidEnv.DirectoryPictures);
            AddRootIfExists(roots, "downloads", "Download", AndroidEnv.DirectoryDownloads);
            return roots;
        }

        /// <inheritdoc/>
        public IReadOnlyList<MediaItemModel> ListCurrentDirectory()
        {
            var result = new List<MediaItemModel>();
            if (string.IsNullOrEmpty(_currentDirectory) || !Directory.Exists(_currentDirectory))
                return result;

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(_currentDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    DirectoryInfo info = new DirectoryInfo(dir);
                    result.Add(new MediaItemModel
                    {
                        Path = dir,
                        Name = info.Name,
                        Kind = MediaKind.Folder,
                        ModifiedLocal = info.LastWriteTime,
                    });
                }

                foreach (string file in Directory.EnumerateFiles(_currentDirectory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    MediaKind kind = DetectKindFromPath(file);
                    if (kind == MediaKind.Other)
                        continue;

                    FileInfo info = new FileInfo(file);
                    result.Add(new MediaItemModel
                    {
                        Path = file,
                        Name = info.Name,
                        Kind = kind,
                        SizeBytes = info.Length,
                        ModifiedLocal = info.LastWriteTime,
                    });
                }
            }
            catch (Exception)
            {
            }

            return result;
        }

        /// <inheritdoc/>
        public void EnterDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            _currentDirectory = path;
        }

        /// <inheritdoc/>
        public void NavigateUp()
        {
            if (!CanNavigateUp)
                return;
            DirectoryInfo parent = Directory.GetParent(_currentDirectory);
            if (parent != null && parent.Exists)
                _currentDirectory = parent.FullName;
        }

        /// <inheritdoc/>
        public void GoToRoot(string rootKey)
        {
            var roots = GetStorageRoots();
            var match = roots.FirstOrDefault(r => string.Equals(r.Key, rootKey, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Path) && Directory.Exists(match.Path))
                _currentDirectory = match.Path;
        }

        /// <inheritdoc/>
        public async Task PlayAsync(MediaItemModel item)
        {
            if (item == null)
                return;

            if (item.Kind == MediaKind.Image)
            {
                await OpenImageAsync(item);
                return;
            }

            await PlayUriAsync(item.Path, item.MimeType, item.Name);
        }

        /// <inheritdoc/>
        public Task PlayUriAsync(string uriOrPath, string mimeType = null, string displayName = null)
        {
            if (string.IsNullOrEmpty(uriOrPath))
                return Task.CompletedTask;

            MediaKind kind = DetectKind(uriOrPath, mimeType);
            string title = displayName ?? GuessDisplayName(uriOrPath);

            if (kind == MediaKind.Image)
            {
                OpenImagePath(uriOrPath, title);
                return Task.CompletedTask;
            }

            if (kind == MediaKind.Video)
            {
                StopAudioPlayer();
                _activePath = uriOrPath;
                _activeTitle = title;
                _activeKind = MediaKind.Video;
                _isPlaying = false;
                _isPaused = false;
                _duration = TimeSpan.Zero;
                RaiseStateChanged();
                SilentNotes.VideoPlayerActivity.Start(GetLaunchContext(), uriOrPath, title);
                return Task.CompletedTask;
            }

            // Default / audio
            PlayAudio(uriOrPath, title);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void Pause()
        {
            try
            {
                if (_player != null && _player.IsPlaying)
                {
                    _player.Pause();
                    _isPlaying = false;
                    _isPaused = true;
                    RaiseStateChanged();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public void Resume()
        {
            try
            {
                if (_player != null && _isPaused)
                {
                    _player.Start();
                    _isPlaying = true;
                    _isPaused = false;
                    RaiseStateChanged();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            StopAudioPlayer();
            RaiseStateChanged();
        }

        /// <inheritdoc/>
        public void SeekRelative(double relative)
        {
            try
            {
                if (_player == null || _duration.TotalMilliseconds <= 0)
                    return;
                relative = Math.Clamp(relative, 0, 1);
                int pos = (int)(_duration.TotalMilliseconds * relative);
                _player.SeekTo(pos);
                RaiseStateChanged();
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public Task OpenImageAsync(MediaItemModel item)
        {
            if (item == null)
                return Task.CompletedTask;
            OpenImagePath(item.Path, item.Name);
            return Task.CompletedTask;
        }

        private void OpenImagePath(string pathOrUri, string title)
        {
            List<string> gallery = GetSiblingImages(pathOrUri);
            int index = gallery.FindIndex(p => string.Equals(p, pathOrUri, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                if (!string.IsNullOrEmpty(pathOrUri) && gallery.Count == 0)
                    gallery.Add(pathOrUri);
                index = Math.Max(0, gallery.Count - 1);
            }

            SilentNotes.ImageViewerActivity.Start(
                GetLaunchContext(),
                pathOrUri,
                title ?? Path.GetFileName(pathOrUri),
                gallery,
                index);
        }

        private Context GetLaunchContext()
        {
            return (Context)_appContext.RootActivity ?? _appContext.Context;
        }

        /// <summary>Lists image files in the same folder as <paramref name="pathOrUri"/> for swipe browsing.</summary>
        private List<string> GetSiblingImages(string pathOrUri)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(pathOrUri) ||
                    pathOrUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                string path = pathOrUri;
                if (pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    path = AndroidUri.Parse(pathOrUri)?.Path ?? pathOrUri;

                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return result;

                foreach (string file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (DetectKindFromPath(file) == MediaKind.Image)
                        result.Add(file);
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task PickAndOpenAsync()
        {
            Intent picker = new Intent(Intent.ActionOpenDocument);
            picker.AddCategory(Intent.CategoryOpenable);
            picker.SetType("*/*");
            picker.PutExtra(Intent.ExtraMimeTypes, new[]
            {
                "audio/*", "video/*", "image/*",
            });
            picker.AddFlags(ActivityFlags.GrantReadUriPermission);

            var result = await _activityResultAwaiter.StartActivityAndWaitForResult(
                _appContext.RootActivity, picker);
            if (result?.ResultCode != Result.Ok || result.Data?.Data == null)
                return;

            AndroidUri uri = result.Data.Data;
            try
            {
                _appContext.RootActivity.ContentResolver.TakePersistableUriPermission(
                    uri,
                    result.Data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission));
            }
            catch (Exception)
            {
            }

            string mime = _appContext.RootActivity.ContentResolver.GetType(uri);
            string name = QueryDisplayName(uri) ?? uri.LastPathSegment;
            await PlayUriAsync(uri.ToString(), mime, name);
        }

        public void Dispose()
        {
            StopAudioPlayer();
            _timer.Dispose();
        }

        private void PlayAudio(string uriOrPath, string title)
        {
            try
            {
                StopAudioPlayer();

                _player = new MediaPlayer();
                _player.Completion += OnPlaybackCompleted;
                _player.Error += OnPlaybackError;

                if (uriOrPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                    uriOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    _player.SetDataSource(_appContext.Context, AndroidUri.Parse(uriOrPath));
                }
                else
                {
                    _player.SetDataSource(uriOrPath);
                }

                _player.Prepare();
                _duration = TimeSpan.FromMilliseconds(_player.Duration);
                _player.Start();

                _activePath = uriOrPath;
                _activeTitle = title;
                _activeKind = MediaKind.Audio;
                _isPlaying = true;
                _isPaused = false;
                _timer.Start();
                RaiseStateChanged();
            }
            catch (Exception)
            {
                StopAudioPlayer();
                throw;
            }
        }

        private void StopAudioPlayer()
        {
            try
            {
                if (_player != null)
                {
                    _player.Completion -= OnPlaybackCompleted;
                    _player.Error -= OnPlaybackError;
                    try
                    {
                        if (_player.IsPlaying)
                            _player.Stop();
                    }
                    catch (Exception)
                    {
                    }
                    _player.Release();
                    _player.Dispose();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _player = null;
                _activePath = null;
                _activeTitle = null;
                _activeKind = null;
                _isPlaying = false;
                _isPaused = false;
                _duration = TimeSpan.Zero;
                _timer.Stop();
            }
        }

        private void OnPlaybackCompleted(object sender, EventArgs e)
        {
            _isPlaying = false;
            _isPaused = false;
            _timer.Stop();
            RaiseStateChanged();
        }

        private void OnPlaybackError(object sender, MediaPlayer.ErrorEventArgs e)
        {
            StopAudioPlayer();
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private string QueryDisplayName(AndroidUri uri)
        {
            try
            {
                using var cursor = _appContext.RootActivity.ContentResolver.Query(
                    uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    int index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                    if (index >= 0)
                        return cursor.GetString(index);
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static string GuessDisplayName(string uriOrPath)
        {
            if (string.IsNullOrEmpty(uriOrPath))
                return "Media";
            try
            {
                if (uriOrPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
                    return AndroidUri.Parse(uriOrPath)?.LastPathSegment ?? "Media";
                return Path.GetFileName(uriOrPath);
            }
            catch (Exception)
            {
                return "Media";
            }
        }

        internal static MediaKind DetectKind(string uriOrPath, string mimeType)
        {
            if (!string.IsNullOrEmpty(mimeType))
            {
                if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    return MediaKind.Audio;
                if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                    return MediaKind.Video;
                if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return MediaKind.Image;
            }
            return DetectKindFromPath(uriOrPath);
        }

        private static MediaKind DetectKindFromPath(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty);
            if (AudioExtensions.Contains(ext))
                return MediaKind.Audio;
            if (VideoExtensions.Contains(ext))
                return MediaKind.Video;
            if (ImageExtensions.Contains(ext))
                return MediaKind.Image;
            return MediaKind.Other;
        }

        private static string GetDefaultRootPath()
        {
            try
            {
                Java.IO.File dir = AndroidEnv.ExternalStorageDirectory;
                if (dir != null)
                    return dir.AbsolutePath;
            }
            catch (Exception)
            {
            }
            return "/storage/emulated/0";
        }

        private static void AddRootIfExists(
            List<(string Key, string Label, string Path)> roots,
            string key,
            string label,
            string publicDirType)
        {
            try
            {
                Java.IO.File dir = AndroidEnv.GetExternalStoragePublicDirectory(publicDirType);
                if (dir != null && dir.Exists())
                    roots.Add((key, label, dir.AbsolutePath));
            }
            catch (Exception)
            {
            }
        }
    }
}
