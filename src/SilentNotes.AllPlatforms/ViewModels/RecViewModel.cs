// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Rec (sound recording) tab.
    /// </summary>
    public class RecViewModel : ViewModelBase
    {
        private readonly IAudioRecorderService _recorderService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private List<AudioRecordingItemViewModel> _recordings = new List<AudioRecordingItemViewModel>();
        private string _filter;
        private string _renamingFileName;
        private string _draftNewName;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecViewModel"/> class.
        /// </summary>
        public RecViewModel(
            IAudioRecorderService recorderService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _recorderService = recorderService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _recorderService.StateChanged += OnRecorderStateChanged;

            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync);
            PauseResumeRecordingCommand = new RelayCommand(PauseResumeRecording);
            StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync);
            CancelRecordingCommand = new RelayCommand(CancelRecording);

            TogglePlayCommand = new RelayCommand<AudioRecordingItemViewModel>(TogglePlay);
            StopPlayCommand = new RelayCommand(() => _recorderService.StopPlayback());
            StartRenameCommand = new RelayCommand<AudioRecordingItemViewModel>(StartRename);
            SaveRenameCommand = new RelayCommand(SaveRename);
            CancelRenameCommand = new RelayCommand(CancelRename);
            SaveToDeviceCommand = new AsyncRelayCommand<AudioRecordingItemViewModel>(SaveToDeviceAsync);
            ShareCommand = new AsyncRelayCommand<AudioRecordingItemViewModel>(ShareAsync);
            DeleteCommand = new AsyncRelayCommand<AudioRecordingItemViewModel>(DeleteAsync);

            Reload();
        }

        /// <summary>Raised when the UI should re-render due to timer/playback updates.</summary>
        public event Action StateChanged;

        public ICommand GrantPermissionCommand { get; }
        public ICommand StartRecordingCommand { get; }
        public ICommand PauseResumeRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand CancelRecordingCommand { get; }
        public ICommand TogglePlayCommand { get; }
        public ICommand StopPlayCommand { get; }
        public ICommand StartRenameCommand { get; }
        public ICommand SaveRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand SaveToDeviceCommand { get; }
        public ICommand ShareCommand { get; }
        public ICommand DeleteCommand { get; }

        /// <summary>Gets a value indicating whether the platform supports audio recording.</summary>
        public bool IsSupported => _recorderService.IsSupported;

        /// <summary>Gets a value indicating whether microphone permission is missing.</summary>
        public bool NeedsPermission => _recorderService.IsSupported && !_recorderService.HasPermission;

        /// <summary>Gets a value indicating whether a recording is currently active.</summary>
        public bool IsRecording => _recorderService.IsRecording;

        /// <summary>Gets a value indicating whether the active recording is paused.</summary>
        public bool IsRecordingPaused => _recorderService.IsRecordingPaused;

        /// <summary>Gets the formatted elapsed recording time (MM:SS.d).</summary>
        public string ElapsedText => FormatTimeSpan(_recorderService.RecordingElapsed);

        /// <summary>Gets the file name currently being played back, or null.</summary>
        public string ActivePlayingFile => _recorderService.ActivePlayingFile;

        /// <summary>Gets a value indicating whether audio is currently playing.</summary>
        public bool IsPlaying => _recorderService.IsPlaying;

        /// <summary>Gets the formatted playback progress text.</summary>
        public string PlaybackProgressText
        {
            get
            {
                return FormatTimeSpan(_recorderService.PlaybackPosition) + " / " + FormatTimeSpan(_recorderService.PlaybackDuration);
            }
        }

        /// <summary>Gets or sets the search filter for recordings.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilteredRecordings), nameof(HasNoMatches));
                }
            }
        }

        /// <summary>Gets all recordings matching the filter.</summary>
        public IReadOnlyList<AudioRecordingItemViewModel> FilteredRecordings
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _recordings;
                return _recordings.Where(r => r.FileName.ToLowerInvariant().Contains(query)).ToList();
            }
        }

        /// <summary>Gets a value indicating whether there are no recordings saved.</summary>
        public bool IsEmpty => _recordings.Count == 0;

        /// <summary>Gets a value indicating whether the search filter has no matches.</summary>
        public bool HasNoMatches => !IsEmpty && FilteredRecordings.Count == 0;

        /// <summary>Gets the count summary text.</summary>
        public string CountText => _languageService.LoadTextFmt("rec_count", _recordings.Count);

        /// <summary>Gets or sets the draft name when renaming a recording.</summary>
        public string DraftNewName
        {
            get => _draftNewName;
            set => SetProperty(ref _draftNewName, value);
        }

        /// <summary>Determines whether a given recording is currently being renamed.</summary>
        public bool IsRenaming(AudioRecordingItemViewModel item)
        {
            return (item != null) && string.Equals(_renamingFileName, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Determines whether a given recording is currently playing.</summary>
        public bool IsItemPlaying(AudioRecordingItemViewModel item)
        {
            return (item != null)
                && _recorderService.IsPlaying
                && string.Equals(_recorderService.ActivePlayingFile, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Determines whether a given recording is loaded in the player (playing or paused).</summary>
        public bool IsItemActiveInPlayer(AudioRecordingItemViewModel item)
        {
            return (item != null)
                && string.Equals(_recorderService.ActivePlayingFile, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reloads the list of saved recordings.</summary>
        public void Reload()
        {
            _recordings = _recorderService.GetRecordings()
                .Select(m => new AudioRecordingItemViewModel(m))
                .ToList();
            OnPropertyChanged(nameof(FilteredRecordings), nameof(IsEmpty), nameof(HasNoMatches), nameof(CountText), nameof(NeedsPermission));
        }

        /// <summary>Requests microphone permission from the user.</summary>
        public async Task GrantPermissionAsync()
        {
            await _recorderService.RequestPermissionAsync();
            OnPropertyChanged(nameof(NeedsPermission));
        }

        /// <summary>Starts a new sound recording.</summary>
        public async Task StartRecordingAsync()
        {
            bool started = await _recorderService.StartRecordingAsync();
            if (!started)
                _feedbackService?.ShowToast(_languageService["rec_start_failed"], FeedbackSeverity.Warning);
            OnPropertyChanged(nameof(IsRecording), nameof(IsRecordingPaused), nameof(NeedsPermission));
        }

        private void PauseResumeRecording()
        {
            if (!_recorderService.IsRecording)
                return;
            if (_recorderService.IsRecordingPaused)
                _recorderService.ResumeRecording();
            else
                _recorderService.PauseRecording();
            OnPropertyChanged(nameof(IsRecordingPaused));
        }

        /// <summary>Stops and saves the current recording.</summary>
        public async Task StopRecordingAsync()
        {
            AudioRecordingModel saved = await _recorderService.StopRecordingAsync();
            Reload();
            if (saved != null)
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("rec_saved", saved.FileName));
        }

        private void CancelRecording()
        {
            _recorderService.CancelRecording();
            Reload();
        }

        private void TogglePlay(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;

            if (IsItemPlaying(item))
                _recorderService.PausePlayback();
            else
                _recorderService.Play(item.FileName);
        }

        private void StartRename(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;
            _renamingFileName = item.FileName;
            _draftNewName = Path.GetFileNameWithoutExtension(item.FileName);
        }

        private void CancelRename()
        {
            _renamingFileName = null;
            _draftNewName = null;
        }

        private void SaveRename()
        {
            if (string.IsNullOrEmpty(_renamingFileName) || string.IsNullOrWhiteSpace(_draftNewName))
            {
                CancelRename();
                return;
            }

            bool ok = _recorderService.RenameRecording(_renamingFileName, _draftNewName);
            if (!ok)
                _feedbackService?.ShowToast(_languageService["rec_rename_failed"], FeedbackSeverity.Warning);
            CancelRename();
            Reload();
        }

        public async Task SaveToDeviceAsync(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;
            string savedPath = await _recorderService.SaveToPublicStorageAsync(item.FileName);
            if (!string.IsNullOrEmpty(savedPath))
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("rec_exported", savedPath));
            else
                _feedbackService?.ShowToast(_languageService["rec_export_failed"], FeedbackSeverity.Warning);
        }

        public async Task ShareAsync(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;
            await _recorderService.ShareRecordingAsync(item.FileName);
        }

        public async Task DeleteAsync(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;

            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService.LoadTextFmt("rec_delete_confirm", item.FileName),
                _languageService["rec_delete"],
                MessageBoxButtons.YesNoCancel,
                false);
            if (answer != MessageBoxResult.Yes)
                return;

            _recorderService.DeleteRecording(item.FileName);
            Reload();
        }

        private void OnRecorderStateChanged()
        {
            StateChanged?.Invoke();
        }

        internal static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", (int)ts.TotalMinutes, ts.Seconds);
        }
    }

    /// <summary>
    /// Item view model for a single saved audio recording.
    /// </summary>
    public class AudioRecordingItemViewModel
    {
        public AudioRecordingItemViewModel(AudioRecordingModel model)
        {
            Model = model;
        }

        public AudioRecordingModel Model { get; }

        public string FileName => Model.FileName;

        public string DisplayTitle => Path.GetFileNameWithoutExtension(Model.FileName);

        public string DurationText => RecViewModel.FormatTimeSpan(TimeSpan.FromSeconds(Math.Max(0, Model.DurationSeconds)));

        public string DateText => Model.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        public string SizeText
        {
            get
            {
                long bytes = Model.SizeBytes;
                if (bytes < 1024)
                    return bytes + " B";
                if (bytes < 1024 * 1024)
                    return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / 1024.0);
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB", bytes / (1024.0 * 1024.0));
            }
        }
    }
}
