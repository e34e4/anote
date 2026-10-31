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
    /// <summary>Rec tab sub-pages.</summary>
    public enum RecSection
    {
        Audio,
        Screen,
    }

    /// <summary>
    /// View model for the Rec tab (audio + screen recording).
    /// </summary>
    public class RecViewModel : ViewModelBase
    {
        private readonly IAudioRecorderService _recorderService;
        private readonly IScreenRecorderService _screenRecorderService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private List<AudioRecordingItemViewModel> _recordings = new List<AudioRecordingItemViewModel>();
        private string _filter;
        private string _renamingFileName;
        private string _draftNewName;
        private RecSection _section = RecSection.Audio;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecViewModel"/> class.
        /// </summary>
        public RecViewModel(
            IAudioRecorderService recorderService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            IScreenRecorderService screenRecorderService = null)
        {
            _recorderService = recorderService;
            _screenRecorderService = screenRecorderService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _recorderService.StateChanged += OnRecorderStateChanged;
            if (_screenRecorderService != null)
                _screenRecorderService.StateChanged += OnRecorderStateChanged;

            ShowAudioCommand = new RelayCommand(() => Show(RecSection.Audio));
            ShowScreenCommand = new RelayCommand(() => Show(RecSection.Screen));

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

        public ICommand ShowAudioCommand { get; }
        public ICommand ShowScreenCommand { get; }
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

        public RecSection Section
        {
            get => _section;
            private set
            {
                if (SetProperty(ref _section, value))
                {
                    OnPropertyChanged(
                        nameof(IsAudioSection),
                        nameof(IsScreenSection),
                        nameof(IsSupported),
                        nameof(NeedsPermission),
                        nameof(IsRecording),
                        nameof(IsRecordingPaused),
                        nameof(ElapsedText),
                        nameof(FilteredRecordings),
                        nameof(IsEmpty),
                        nameof(HasNoMatches),
                        nameof(CountText),
                        nameof(CanPauseResume));
                    Reload();
                }
            }
        }

        public bool IsAudioSection => _section == RecSection.Audio;
        public bool IsScreenSection => _section == RecSection.Screen;

        public bool IsSupported => IsAudioSection
            ? _recorderService.IsSupported
            : (_screenRecorderService?.IsSupported == true);

        public bool NeedsPermission => IsAudioSection
            && _recorderService.IsSupported
            && !_recorderService.HasPermission;

        public bool IsRecording => IsAudioSection
            ? _recorderService.IsRecording
            : (_screenRecorderService?.IsRecording == true);

        public bool IsRecordingPaused => IsAudioSection && _recorderService.IsRecordingPaused;

        public bool CanPauseResume => IsAudioSection && IsRecording;

        public string ElapsedText => FormatTimeSpan(
            IsAudioSection ? _recorderService.RecordingElapsed : (_screenRecorderService?.RecordingElapsed ?? TimeSpan.Zero));

        public string ActivePlayingFile => _recorderService.ActivePlayingFile;

        public bool IsPlaying => _recorderService.IsPlaying;

        public string PlaybackProgressText
        {
            get
            {
                return FormatTimeSpan(_recorderService.PlaybackPosition) + " / " + FormatTimeSpan(_recorderService.PlaybackDuration);
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    OnPropertyChanged(nameof(FilteredRecordings), nameof(HasNoMatches));
            }
        }

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

        public bool IsEmpty => _recordings.Count == 0;

        public bool HasNoMatches => !IsEmpty && FilteredRecordings.Count == 0;

        public string CountText => _languageService.LoadTextFmt(
            IsScreenSection ? "rec_screen_count" : "rec_count",
            _recordings.Count);

        public string DraftNewName
        {
            get => _draftNewName;
            set => SetProperty(ref _draftNewName, value);
        }

        public bool IsRenaming(AudioRecordingItemViewModel item)
        {
            return (item != null) && string.Equals(_renamingFileName, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsItemPlaying(AudioRecordingItemViewModel item)
        {
            return IsAudioSection
                && (item != null)
                && _recorderService.IsPlaying
                && string.Equals(_recorderService.ActivePlayingFile, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsItemActiveInPlayer(AudioRecordingItemViewModel item)
        {
            return IsAudioSection
                && (item != null)
                && string.Equals(_recorderService.ActivePlayingFile, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        public void Reload()
        {
            if (IsScreenSection)
            {
                _recordings = (_screenRecorderService?.GetRecordings() ?? Array.Empty<AudioRecordingModel>())
                    .Select(m => new AudioRecordingItemViewModel(m))
                    .ToList();
            }
            else
            {
                _recordings = _recorderService.GetRecordings()
                    .Select(m => new AudioRecordingItemViewModel(m))
                    .ToList();
            }

            OnPropertyChanged(
                nameof(FilteredRecordings),
                nameof(IsEmpty),
                nameof(HasNoMatches),
                nameof(CountText),
                nameof(NeedsPermission),
                nameof(IsSupported),
                nameof(IsRecording),
                nameof(IsRecordingPaused),
                nameof(ElapsedText),
                nameof(CanPauseResume));
        }

        public async Task GrantPermissionAsync()
        {
            await _recorderService.RequestPermissionAsync();
            OnPropertyChanged(nameof(NeedsPermission));
        }

        public async Task StartRecordingAsync()
        {
            bool started;
            if (IsScreenSection)
            {
                if (_screenRecorderService == null)
                    return;
                started = await _screenRecorderService.StartRecordingAsync();
            }
            else
            {
                started = await _recorderService.StartRecordingAsync();
            }

            if (!started)
                _feedbackService?.ShowToast(_languageService["rec_start_failed"], FeedbackSeverity.Warning);
            OnPropertyChanged(nameof(IsRecording), nameof(IsRecordingPaused), nameof(NeedsPermission), nameof(CanPauseResume));
        }

        private void PauseResumeRecording()
        {
            if (!IsAudioSection || !_recorderService.IsRecording)
                return;
            if (_recorderService.IsRecordingPaused)
                _recorderService.ResumeRecording();
            else
                _recorderService.PauseRecording();
            OnPropertyChanged(nameof(IsRecordingPaused));
        }

        public async Task StopRecordingAsync()
        {
            AudioRecordingModel saved;
            if (IsScreenSection)
                saved = await _screenRecorderService.StopRecordingAsync();
            else
                saved = await _recorderService.StopRecordingAsync();

            Reload();
            if (saved != null)
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("rec_saved", saved.FileName));
        }

        private void CancelRecording()
        {
            if (IsScreenSection)
                _screenRecorderService?.CancelRecording();
            else
                _recorderService.CancelRecording();
            Reload();
        }

        private void TogglePlay(AudioRecordingItemViewModel item)
        {
            if (item == null || !IsAudioSection)
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

            bool ok = IsScreenSection
                ? (_screenRecorderService?.RenameRecording(_renamingFileName, _draftNewName) == true)
                : _recorderService.RenameRecording(_renamingFileName, _draftNewName);
            if (!ok)
                _feedbackService?.ShowToast(_languageService["rec_rename_failed"], FeedbackSeverity.Warning);
            CancelRename();
            Reload();
        }

        public async Task SaveToDeviceAsync(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;
            string savedPath = IsScreenSection
                ? await _screenRecorderService.SaveToPublicStorageAsync(item.FileName)
                : await _recorderService.SaveToPublicStorageAsync(item.FileName);
            if (!string.IsNullOrEmpty(savedPath))
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("rec_exported", savedPath));
            else
                _feedbackService?.ShowToast(_languageService["rec_export_failed"], FeedbackSeverity.Warning);
        }

        public async Task ShareAsync(AudioRecordingItemViewModel item)
        {
            if (item == null)
                return;
            if (IsScreenSection)
                await _screenRecorderService.ShareRecordingAsync(item.FileName);
            else
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

            if (IsScreenSection)
                _screenRecorderService?.DeleteRecording(item.FileName);
            else
                _recorderService.DeleteRecording(item.FileName);
            Reload();
        }

        private void Show(RecSection section)
        {
            Section = section;
        }

        /// <summary>
        /// Switches to the Screen sub-tab and starts recording (used by the floating quick menu).
        /// </summary>
        public async Task StartScreenRecordingFromIntentAsync()
        {
            Section = RecSection.Screen;
            if (_screenRecorderService?.IsRecording == true)
                return;
            await StartRecordingAsync();
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
    /// Item view model for a single saved audio or screen recording.
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
