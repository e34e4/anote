// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;
using Path = System.IO.Path;
using Stream = System.IO.Stream;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IAudioRecorderService"/> using MediaRecorder and MediaPlayer.
    /// </summary>
    internal class AudioRecorderService : IAudioRecorderService
    {
        private const string FolderName = "recordings";
        private const string ShareFolder = "clipboard";
        private const string PublicSubFolder = "Recordings";

        private readonly IAppContextService _appContext;
        private readonly Stopwatch _recordingStopwatch = new Stopwatch();
        private readonly System.Timers.Timer _timer;

        private MediaRecorder _recorder;
        private string _activeRecordingPath;
        private bool _isRecording;
        private bool _isRecordingPaused;

        private MediaPlayer _player;
        private string _activePlayingFile;
        private bool _isPlaying;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioRecorderService"/> class.
        /// </summary>
        public AudioRecorderService(IAppContextService appContextService)
        {
            _appContext = appContextService;
            _timer = new System.Timers.Timer(250);
            _timer.Elapsed += OnTimerElapsed;
        }

        /// <inheritdoc/>
        public event Action StateChanged;

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasPermission
        {
            get
            {
                Context context = _appContext.Context;
                return (context != null)
                    && (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.RecordAudio) == Permission.Granted);
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
                    () => Permissions.RequestAsync<Permissions.Microphone>());
                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsRecording => _isRecording;

        /// <inheritdoc/>
        public bool IsRecordingPaused => _isRecordingPaused;

        /// <inheritdoc/>
        public TimeSpan RecordingElapsed => _recordingStopwatch.Elapsed;

        /// <inheritdoc/>
        public string ActivePlayingFile => _activePlayingFile;

        /// <inheritdoc/>
        public bool IsPlaying => _isPlaying;

        /// <inheritdoc/>
        public TimeSpan PlaybackPosition
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
        public TimeSpan PlaybackDuration
        {
            get
            {
                try
                {
                    if (_player != null && _player.Duration > 0)
                        return TimeSpan.FromMilliseconds(_player.Duration);
                }
                catch (Exception)
                {
                }
                return TimeSpan.Zero;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> StartRecordingAsync()
        {
            if (_isRecording)
                return false;

            if (!HasPermission && !await RequestPermissionAsync())
                return false;

            StopPlayback();

            try
            {
                string dir = GetRecordingsDirectory();
                Directory.CreateDirectory(dir);
                string fileName = "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".m4a";
                _activeRecordingPath = Path.Combine(dir, fileName);

#pragma warning disable CA1422
                _recorder = (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    ? new MediaRecorder(_appContext.Context)
                    : new MediaRecorder();
#pragma warning restore CA1422

                _recorder.SetAudioSource(AudioSource.Mic);
                _recorder.SetOutputFormat(OutputFormat.Mpeg4);
                _recorder.SetAudioEncoder(AudioEncoder.Aac);
                _recorder.SetAudioEncodingBitRate(128000);
                _recorder.SetAudioSamplingRate(44100);
                _recorder.SetOutputFile(_activeRecordingPath);
                _recorder.Prepare();
                _recorder.Start();

                _recordingStopwatch.Restart();
                _isRecording = true;
                _isRecordingPaused = false;
                _timer.Start();
                RaiseStateChanged();
                return true;
            }
            catch (Exception)
            {
                CleanupRecorder(deleteFile: true);
                return false;
            }
        }

        /// <inheritdoc/>
        public void PauseRecording()
        {
            if (!_isRecording || _isRecordingPaused || _recorder == null)
                return;

            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                {
                    _recorder.Pause();
                    _recordingStopwatch.Stop();
                    _isRecordingPaused = true;
                    RaiseStateChanged();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public void ResumeRecording()
        {
            if (!_isRecording || !_isRecordingPaused || _recorder == null)
                return;

            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                {
                    _recorder.Resume();
                    _recordingStopwatch.Start();
                    _isRecordingPaused = false;
                    RaiseStateChanged();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public Task<AudioRecordingModel> StopRecordingAsync()
        {
            return Task.Run(() =>
            {
                if (!_isRecording || _recorder == null)
                    return null;

                string savedPath = _activeRecordingPath;
                double elapsedSeconds = _recordingStopwatch.Elapsed.TotalSeconds;

                try
                {
                    _recorder.Stop();
                }
                catch (Exception)
                {
                }

                CleanupRecorder(deleteFile: false);

                if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    var info = new FileInfo(savedPath);
                    double duration = ReadDurationSeconds(savedPath);
                    if (duration <= 0)
                        duration = elapsedSeconds;

                    RaiseStateChanged();
                    return new AudioRecordingModel
                    {
                        FileName = info.Name,
                        FilePath = info.FullName,
                        SizeBytes = info.Length,
                        DurationSeconds = duration,
                        CreatedAtUtc = info.CreationTimeUtc,
                    };
                }

                RaiseStateChanged();
                return null;
            });
        }

        /// <inheritdoc/>
        public void CancelRecording()
        {
            if (!_isRecording)
                return;

            try
            {
                _recorder?.Stop();
            }
            catch (Exception)
            {
            }

            CleanupRecorder(deleteFile: true);
            RaiseStateChanged();
        }

        /// <inheritdoc/>
        public IReadOnlyList<AudioRecordingModel> GetRecordings()
        {
            var result = new List<AudioRecordingModel>();
            try
            {
                string dir = GetRecordingsDirectory();
                if (!Directory.Exists(dir))
                    return result;

                var dirInfo = new DirectoryInfo(dir);
                foreach (FileInfo file in dirInfo.GetFiles("*.m4a").OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if (_isRecording && string.Equals(file.FullName, _activeRecordingPath, StringComparison.OrdinalIgnoreCase))
                        continue;

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
                string dir = GetRecordingsDirectory();
                string oldPath = Path.Combine(dir, Path.GetFileName(oldFileName));
                if (!File.Exists(oldPath))
                    return false;

                string sanitized = SanitizeFileName(newFileName.Trim());
                if (string.IsNullOrEmpty(sanitized))
                    return false;
                if (!sanitized.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                    sanitized += ".m4a";

                string newPath = Path.Combine(dir, sanitized);
                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (File.Exists(newPath))
                    return false;

                if (string.Equals(_activePlayingFile, oldFileName, StringComparison.OrdinalIgnoreCase))
                    StopPlayback();

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
                if (string.Equals(_activePlayingFile, fileName, StringComparison.OrdinalIgnoreCase))
                    StopPlayback();

                string path = Path.Combine(GetRecordingsDirectory(), Path.GetFileName(fileName));
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <inheritdoc/>
        public void Play(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || _isRecording)
                return;

            try
            {
                if (_player != null && string.Equals(_activePlayingFile, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_player.IsPlaying)
                    {
                        _player.Start();
                        _isPlaying = true;
                        _timer.Start();
                        RaiseStateChanged();
                    }
                    return;
                }

                StopPlayback();

                string path = Path.Combine(GetRecordingsDirectory(), Path.GetFileName(fileName));
                if (!File.Exists(path))
                    return;

                _player = new MediaPlayer();
                _player.SetDataSource(path);
                _player.Completion += OnPlaybackCompleted;
                _player.Prepare();
                _player.Start();

                _activePlayingFile = fileName;
                _isPlaying = true;
                _timer.Start();
                RaiseStateChanged();
            }
            catch (Exception)
            {
                StopPlayback();
            }
        }

        /// <inheritdoc/>
        public void PausePlayback()
        {
            try
            {
                if (_player != null && _player.IsPlaying)
                {
                    _player.Pause();
                    _isPlaying = false;
                    RaiseStateChanged();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public void StopPlayback()
        {
            try
            {
                if (_player != null)
                {
                    _player.Completion -= OnPlaybackCompleted;
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
                _activePlayingFile = null;
                _isPlaying = false;
                if (!_isRecording)
                    _timer.Stop();
                RaiseStateChanged();
            }
        }

        /// <inheritdoc/>
        public Task<string> SaveToPublicStorageAsync(string fileName)
        {
            return Task.Run(() =>
            {
                try
                {
                    string sourcePath = Path.Combine(GetRecordingsDirectory(), Path.GetFileName(fileName));
                    if (!File.Exists(sourcePath))
                        return null;

                    Context context = _appContext.Context;
                    string displayName = Path.GetFileName(fileName);

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var values = new ContentValues();
                        values.Put("_display_name", displayName);
                        values.Put("mime_type", "audio/mp4");
                        values.Put("relative_path", "Music/" + PublicSubFolder);

                        global::Android.Net.Uri uri = context.ContentResolver.Insert(
                            global::Android.Provider.MediaStore.Audio.Media.ExternalContentUri, values);
                        if (uri == null)
                            return null;

                        using (Stream input = File.OpenRead(sourcePath))
                        using (Stream output = context.ContentResolver.OpenOutputStream(uri))
                        {
                            input.CopyTo(output);
                        }
                        return "Music/" + PublicSubFolder + "/" + displayName;
                    }
                    else
                    {
                        string dir = Path.Combine(
                            global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                                global::Android.OS.Environment.DirectoryMusic).AbsolutePath,
                            PublicSubFolder);
                        Directory.CreateDirectory(dir);
                        string target = Path.Combine(dir, displayName);
                        File.Copy(sourcePath, target, overwrite: true);
                        return target;
                    }
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
                    string sourcePath = Path.Combine(GetRecordingsDirectory(), Path.GetFileName(fileName));
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
                    shareIntent.SetType("audio/mp4");
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

        private void OnPlaybackCompleted(object sender, EventArgs e)
        {
            StopPlayback();
        }

        private void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_isRecording && !_isPlaying)
            {
                _timer.Stop();
                return;
            }
            RaiseStateChanged();
        }

        private void CleanupRecorder(bool deleteFile)
        {
            _recordingStopwatch.Stop();
            _isRecording = false;
            _isRecordingPaused = false;
            if (!_isPlaying)
                _timer.Stop();

            try
            {
                _recorder?.Release();
                _recorder?.Dispose();
            }
            catch (Exception)
            {
            }
            _recorder = null;

            if (deleteFile && !string.IsNullOrEmpty(_activeRecordingPath))
            {
                try
                {
                    if (File.Exists(_activeRecordingPath))
                        File.Delete(_activeRecordingPath);
                }
                catch (Exception)
                {
                }
            }
            _activeRecordingPath = null;
        }

        private string GetRecordingsDirectory()
        {
            string baseDir = ContextCompat.GetNoBackupFilesDir(_appContext.Context).AbsolutePath;
            return Path.Combine(baseDir, FolderName);
        }

        private static double ReadDurationSeconds(string filePath)
        {
            MediaMetadataRetriever retriever = null;
            try
            {
                retriever = new MediaMetadataRetriever();
                retriever.SetDataSource(filePath);
                string durationStr = retriever.ExtractMetadata(MetadataKey.Duration);
                if (long.TryParse(durationStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) && ms > 0)
                    return ms / 1000.0;
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    retriever?.Release();
                    retriever?.Dispose();
                }
                catch (Exception)
                {
                }
            }
            return 0;
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
