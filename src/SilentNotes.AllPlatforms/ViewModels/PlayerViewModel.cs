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
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Player tab: browse device folders and play audio/video. Images are
    /// listed too so the same tab can later become a pictures gallery viewer.
    /// </summary>
    public class PlayerViewModel : ViewModelBase
    {
        private readonly IMediaPlayerService _mediaService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly ISettingsService _settingsService;
        private readonly IYtDlpDownloadPromptService _ytDlpPrompt;

        private string _filter;
        private string _kindFilter = "all"; // all | audio | video | image
        private string _urlDraft;
        private List<MediaItemViewModel> _items = new List<MediaItemViewModel>();
        private bool _isBusy;
        private int _activePathTabIndex;
        private List<PathTabSessionModel> _pathTabs = new List<PathTabSessionModel>();
        private readonly HashSet<string> _selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _queue = new List<string>();
        private int _queueIndex = -1;
        private double _playbackSpeed = 1.0;

        public PlayerViewModel(
            IMediaPlayerService mediaService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            ISettingsService settingsService = null,
            IYtDlpDownloadPromptService ytDlpPrompt = null)
        {
            _mediaService = mediaService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _settingsService = settingsService;
            _ytDlpPrompt = ytDlpPrompt;

            _mediaService.StateChanged += OnMediaStateChanged;
            _mediaService.PlaybackCompleted += OnPlaybackCompleted;

            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            RefreshCommand = new RelayCommand(Reload);
            NavigateUpCommand = new RelayCommand(NavigateUp);
            EnterItemCommand = new AsyncRelayCommand<MediaItemViewModel>(EnterOrOpenAsync);
            PickFileCommand = new AsyncRelayCommand(PickFileAsync);
            OpenUrlCommand = new AsyncRelayCommand(OpenUrlAsync);
            TogglePlayCommand = new RelayCommand(TogglePlay);
            StopCommand = new RelayCommand(() => _mediaService.Stop());
            SeekCommand = new RelayCommand<double>(Seek);
            SetKindFilterCommand = new RelayCommand<string>(SetKindFilter);
            GoToRootCommand = new RelayCommand<string>(GoToRoot);
            AddPathTabCommand = new RelayCommand(AddPathTab);
            ClosePathTabCommand = new RelayCommand<int>(ClosePathTab);
            SelectPathTabCommand = new RelayCommand<int>(SelectPathTab);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            PlayNextCommand = new RelayCommand(PlayNextTrack);
            PlayPreviousCommand = new RelayCommand(PlayPreviousTrack);
            QueueFolderCommand = new RelayCommand(QueueVisibleAudio);
            ClearQueueCommand = new RelayCommand(ClearQueue);
            SetSpeedCommand = new RelayCommand<double>(SetSpeed);

            MediaPlaybackCommands.Next = PlayNextTrack;
            MediaPlaybackCommands.Previous = PlayPreviousTrack;
            LoadQueueFromStore();

            LoadPathTabsFromSettings();
            Reload();
        }

        /// <summary>Raised when the UI should re-render.</summary>
        public event Action StateChanged;

        public ICommand GrantPermissionCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand EnterItemCommand { get; }
        public ICommand PickFileCommand { get; }
        public ICommand OpenUrlCommand { get; }
        public ICommand TogglePlayCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SeekCommand { get; }
        public ICommand SetKindFilterCommand { get; }
        public ICommand GoToRootCommand { get; }
        public ICommand AddPathTabCommand { get; }
        public ICommand ClosePathTabCommand { get; }
        public ICommand SelectPathTabCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand PlayNextCommand { get; }
        public ICommand PlayPreviousCommand { get; }
        public ICommand QueueFolderCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand SetSpeedCommand { get; }

        /// <summary>Gets a value indicating whether a queue is active.</summary>
        public bool HasQueue => _queue.Count > 0;

        /// <summary>Gets the number of items in the queue.</summary>
        public int QueueCount => _queue.Count;

        /// <summary>Gets the current playback speed.</summary>
        public double PlaybackSpeed => _playbackSpeed;

        /// <summary>Gets a value indicating whether the given speed is active.</summary>
        public bool IsSpeed(double speed)
        {
            return Math.Abs(_playbackSpeed - speed) < 0.01;
        }

        public bool HasSelection => _selectedPaths.Count > 0;

        public int SelectedCount => _selectedPaths.Count;

        /// <summary>Gets or sets a URL (YouTube / http media) to open in the player.</summary>
        public string UrlDraft
        {
            get => _urlDraft;
            set => SetProperty(ref _urlDraft, value);
        }

        /// <summary>Gets path sessions for the Player browser.</summary>
        public IReadOnlyList<PathTabSessionModel> PathTabs => _pathTabs;

        /// <summary>Gets the active path session index.</summary>
        public int ActivePathTabIndex => _activePathTabIndex;

        public bool IsSupported => _mediaService.IsSupported;

        public bool NeedsPermission => _mediaService.IsSupported && !_mediaService.HasAllFilesPermission;

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string CurrentPathDisplay => _mediaService.CurrentDirectoryDisplay;

        public bool CanNavigateUp => _mediaService.CanNavigateUp;

        public IReadOnlyList<(string Key, string Label, string Path)> StorageRoots => _mediaService.GetStorageRoots();

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    OnPropertyChanged(nameof(FilteredItems), nameof(HasNoMatches));
            }
        }

        /// <summary>Gets or sets the media kind chip filter: all, audio, video, image.</summary>
        public string KindFilter
        {
            get => _kindFilter;
            set
            {
                if (SetProperty(ref _kindFilter, value))
                    OnPropertyChanged(nameof(FilteredItems), nameof(HasNoMatches), nameof(IsKindAll), nameof(IsKindAudio), nameof(IsKindVideo), nameof(IsKindImage));
            }
        }

        public bool IsKindAll => string.Equals(_kindFilter, "all", StringComparison.OrdinalIgnoreCase);
        public bool IsKindAudio => string.Equals(_kindFilter, "audio", StringComparison.OrdinalIgnoreCase);
        public bool IsKindVideo => string.Equals(_kindFilter, "video", StringComparison.OrdinalIgnoreCase);
        public bool IsKindImage => string.Equals(_kindFilter, "image", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<MediaItemViewModel> FilteredItems
        {
            get
            {
                IEnumerable<MediaItemViewModel> query = _items;

                if (!IsKindAll)
                {
                    query = query.Where(i =>
                        i.Kind == MediaKind.Folder ||
                        (IsKindAudio && i.Kind == MediaKind.Audio) ||
                        (IsKindVideo && i.Kind == MediaKind.Video) ||
                        (IsKindImage && i.Kind == MediaKind.Image));
                }

                string filter = _filter?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(filter))
                    query = query.Where(i => i.Name.ToLowerInvariant().Contains(filter));

                return query.ToList();
            }
        }

        public bool IsEmpty => _items.Count == 0;

        public bool HasNoMatches => !IsEmpty && FilteredItems.Count == 0;

        public string CountText => _languageService.LoadTextFmt("player_count", FilteredItems.Count);

        public bool HasActiveMedia => !string.IsNullOrEmpty(_mediaService.ActivePath);

        public string ActiveTitle => _mediaService.ActiveTitle ?? string.Empty;

        public MediaKind? ActiveKind => _mediaService.ActiveKind;

        public bool IsPlaying => _mediaService.IsPlaying;

        public bool IsPaused => _mediaService.IsPaused;

        public string PlaybackProgressText
        {
            get
            {
                return FormatTimeSpan(_mediaService.Position) + " / " + FormatTimeSpan(_mediaService.Duration);
            }
        }

        public double PlaybackRelative
        {
            get
            {
                double total = _mediaService.Duration.TotalMilliseconds;
                if (total <= 0)
                    return 0;
                return Math.Clamp(_mediaService.Position.TotalMilliseconds / total, 0, 1);
            }
        }

        public async Task GrantPermissionAsync()
        {
            await _mediaService.RequestAllFilesPermissionAsync();
            Reload();
            RaiseStateChanged();
        }

        public bool IsSelected(MediaItemViewModel item)
            => item != null && !string.IsNullOrEmpty(item.Path) && _selectedPaths.Contains(item.Path);

        public void ToggleSelection(MediaItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
                return;
            if (!_selectedPaths.Add(item.Path))
                _selectedPaths.Remove(item.Path);
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount));
            RaiseStateChanged();
        }

        public void SelectAllVisible()
        {
            foreach (MediaItemViewModel item in FilteredItems)
            {
                if (!string.IsNullOrEmpty(item.Path))
                    _selectedPaths.Add(item.Path);
            }
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount));
            RaiseStateChanged();
        }

        public void ClearSelection()
        {
            if (_selectedPaths.Count == 0)
                return;
            _selectedPaths.Clear();
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount));
            RaiseStateChanged();
        }

        public async Task DeleteSelectedAsync()
        {
            if (_selectedPaths.Count == 0)
                return;

            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService.LoadTextFmt("player_delete_confirm", _selectedPaths.Count),
                _languageService["player_delete"],
                MessageBoxButtons.YesNoCancel,
                false);
            if (answer != MessageBoxResult.Yes)
                return;

            var targets = FilteredItems
                .Where(i => _selectedPaths.Contains(i.Path))
                .ToList();
            if (targets.Count == 0)
            {
                ClearSelection();
                return;
            }

            int deleted = 0;
            foreach (MediaItemViewModel item in targets)
            {
                bool isDir = item.Kind == MediaKind.Folder;
                if (_mediaService.TryDeletePath(item.Path, isDir))
                    deleted++;
            }

            ClearSelection();
            Reload();
            if (deleted > 0)
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_deleted", deleted));
            else
                _feedbackService?.ShowToast(_languageService["player_delete_failed"], FeedbackSeverity.Warning);
        }

        public void Reload()
        {
            try
            {
                IsBusy = true;
                _items = _mediaService.ListCurrentDirectory()
                    .Select(m => new MediaItemViewModel(m))
                    .ToList();
                OnPropertyChanged(
                    nameof(FilteredItems),
                    nameof(IsEmpty),
                    nameof(HasNoMatches),
                    nameof(CountText),
                    nameof(CurrentPathDisplay),
                    nameof(CanNavigateUp),
                    nameof(NeedsPermission),
                    nameof(StorageRoots));
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        public void HandleIntent(MediaIntentRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UriOrPath))
                return;

            _ = PlayFromIntentAsync(request);
        }

        private async Task PlayFromIntentAsync(MediaIntentRequest request)
        {
            try
            {
                await _mediaService.PlayUriAsync(request.UriOrPath, request.MimeType, request.DisplayName);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_play_failed", ex.Message), FeedbackSeverity.Warning);
            }
            RaiseStateChanged();
        }

        /// <summary>Navigates one directory level up.</summary>
        public void NavigateUp()
        {
            _mediaService.NavigateUp();
            SaveCurrentPathToActiveTab();
            PersistPathTabs();
            Reload();
        }

        private void GoToRoot(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            _mediaService.GoToRoot(key);
            SaveCurrentPathToActiveTab();
            PersistPathTabs();
            Reload();
        }

        private async Task OpenUrlAsync()
        {
            string url = (_urlDraft ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url))
                return;

            PlayerUrlHistoryStore.Add(url);

            try
            {
                if (_ytDlpPrompt != null)
                {
                    await _ytDlpPrompt.HandlePlayOrDownloadAsync(url, async () =>
                    {
                        await _mediaService.PlayUriAsync(url, null, url).ConfigureAwait(true);
                    }).ConfigureAwait(true);
                }
                else
                {
                    await _mediaService.PlayUriAsync(url, null, url).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_play_failed", ex.Message), FeedbackSeverity.Warning);
            }
            RaiseStateChanged();
        }

        /// <summary>Recent Player URL history (newest first).</summary>
        public IReadOnlyList<string> UrlHistory => PlayerUrlHistoryStore.Load();

        public void ApplyHistoryUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            UrlDraft = url.Trim();
            RaiseStateChanged();
        }

        private void AddPathTab()
        {
            _pathTabs.Add(new PathTabSessionModel
            {
                Path = _mediaService.CurrentDirectory ?? string.Empty,
                Label = MakePathLabel(_mediaService.CurrentDirectory),
            });
            _activePathTabIndex = _pathTabs.Count - 1;
            PersistPathTabs();
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex));
            RaiseStateChanged();
        }

        private void ClosePathTab(int index)
        {
            if (_pathTabs.Count <= 1 || index < 0 || index >= _pathTabs.Count)
                return;

            _pathTabs.RemoveAt(index);
            if (_activePathTabIndex >= _pathTabs.Count)
                _activePathTabIndex = _pathTabs.Count - 1;
            else if (_activePathTabIndex > index)
                _activePathTabIndex--;

            PersistPathTabs();
            ApplyActivePathTab();
        }

        private void SelectPathTab(int index)
        {
            if (index < 0 || index >= _pathTabs.Count || index == _activePathTabIndex)
                return;

            SaveCurrentPathToActiveTab();
            _activePathTabIndex = index;
            PersistPathTabs();
            ApplyActivePathTab();
        }

        private void LoadPathTabsFromSettings()
        {
            if (_settingsService == null)
            {
                _pathTabs = new List<PathTabSessionModel>
                {
                    new PathTabSessionModel
                    {
                        Path = _mediaService.CurrentDirectory ?? string.Empty,
                        Label = MakePathLabel(_mediaService.CurrentDirectory),
                    },
                };
                _activePathTabIndex = 0;
                return;
            }

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _pathTabs = settings.PlayerPathTabs != null && settings.PlayerPathTabs.Count > 0
                ? new List<PathTabSessionModel>(settings.PlayerPathTabs)
                : new List<PathTabSessionModel>
                {
                    new PathTabSessionModel
                    {
                        Path = _mediaService.CurrentDirectory ?? string.Empty,
                        Label = MakePathLabel(_mediaService.CurrentDirectory),
                    },
                };
            _activePathTabIndex = Math.Clamp(settings.PlayerPathTabIndex, 0, _pathTabs.Count - 1);
            string path = _pathTabs[_activePathTabIndex].Path;
            if (!string.IsNullOrEmpty(path))
                _mediaService.EnterDirectory(path);
        }

        private void PersistPathTabs()
        {
            if (_settingsService == null)
                return;
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.PlayerPathTabs = _pathTabs.Select(t => new PathTabSessionModel
            {
                Path = t.Path ?? string.Empty,
                Label = t.Label ?? string.Empty,
            }).ToList();
            settings.PlayerPathTabIndex = _activePathTabIndex;
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        private void SaveCurrentPathToActiveTab()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                return;
            _pathTabs[_activePathTabIndex].Path = _mediaService.CurrentDirectory ?? string.Empty;
            _pathTabs[_activePathTabIndex].Label = MakePathLabel(_mediaService.CurrentDirectory);
            OnPropertyChanged(nameof(PathTabs));
        }

        private void ApplyActivePathTab()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                _activePathTabIndex = 0;
            string path = _pathTabs[_activePathTabIndex].Path;
            if (!string.IsNullOrEmpty(path))
                _mediaService.EnterDirectory(path);
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex));
            Reload();
        }

        private static string MakePathLabel(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";
            try
            {
                string name = Path.GetFileName(path.TrimEnd('/', '\\'));
                return string.IsNullOrEmpty(name) ? "/" : name;
            }
            catch (Exception)
            {
                return "/";
            }
        }

        private void SetKindFilter(string kind)
        {
            KindFilter = string.IsNullOrEmpty(kind) ? "all" : kind;
            RaiseStateChanged();
        }

        private async Task EnterOrOpenAsync(MediaItemViewModel item)
        {
            if (item == null)
                return;

            try
            {
                if (item.Kind == MediaKind.Folder)
                {
                    ClearSelection();
                    _mediaService.EnterDirectory(item.Path);
                    SaveCurrentPathToActiveTab();
                    PersistPathTabs();
                    Reload();
                    return;
                }

                if (item.Kind == MediaKind.Image)
                {
                    await _mediaService.OpenImageAsync(item.Model);
                    return;
                }

                _queueIndex = _queue.FindIndex(path => string.Equals(path, item.Path, StringComparison.OrdinalIgnoreCase));
                await _mediaService.PlayAsync(item.Model);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_play_failed", ex.Message), FeedbackSeverity.Warning);
            }
            RaiseStateChanged();
        }

        private async Task PickFileAsync()
        {
            try
            {
                await _mediaService.PickAndOpenAsync();
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_play_failed", ex.Message), FeedbackSeverity.Warning);
            }
            RaiseStateChanged();
        }

        private void TogglePlay()
        {
            if (_mediaService.IsPlaying)
                _mediaService.Pause();
            else if (_mediaService.IsPaused)
                _mediaService.Resume();
            RaiseStateChanged();
        }

        private void Seek(double relative)
        {
            _mediaService.SeekRelative(relative);
            RaiseStateChanged();
        }

        /// <summary>Plays the next item of the queue, wrapping around at the end.</summary>
        public void PlayNextTrack()
        {
            if (_queue.Count == 0)
                return;
            _queueIndex = (_queueIndex + 1) % _queue.Count;
            PlayQueueItem();
        }

        /// <summary>Plays the previous item of the queue, wrapping around at the start.</summary>
        public void PlayPreviousTrack()
        {
            if (_queue.Count == 0)
                return;
            _queueIndex = _queueIndex <= 0 ? _queue.Count - 1 : _queueIndex - 1;
            PlayQueueItem();
        }

        private async void PlayQueueItem()
        {
            if (_queueIndex < 0 || _queueIndex >= _queue.Count)
                return;

            string path = _queue[_queueIndex];
            PersistQueue();
            try
            {
                await _mediaService.PlayUriAsync(path, null, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_play_failed", ex.Message), FeedbackSeverity.Warning);
            }
            OnPropertyChanged(nameof(HasQueue), nameof(QueueCount));
            RaiseStateChanged();
        }

        private void QueueVisibleAudio()
        {
            List<string> paths = FilteredItems
                .Where(item => item.Kind == MediaKind.Audio && !string.IsNullOrEmpty(item.Path))
                .Select(item => item.Path)
                .ToList();

            if (paths.Count == 0)
            {
                _feedbackService?.ShowToast(_languageService["player_queue_empty"], FeedbackSeverity.Info);
                return;
            }

            _queue.Clear();
            _queue.AddRange(paths);
            _queueIndex = _queue.FindIndex(path => string.Equals(path, _mediaService.ActivePath, StringComparison.OrdinalIgnoreCase));

            bool startPlayback = !HasActiveMedia;
            PersistQueue();
            OnPropertyChanged(nameof(HasQueue), nameof(QueueCount));
            _feedbackService?.ShowToast(_languageService.LoadTextFmt("player_queued", _queue.Count));
            if (startPlayback)
                PlayNextTrack();
            else
                RaiseStateChanged();
        }

        private void ClearQueue()
        {
            _queue.Clear();
            _queueIndex = -1;
            PersistQueue();
            OnPropertyChanged(nameof(HasQueue), nameof(QueueCount));
            RaiseStateChanged();
        }

        private void SetSpeed(double speed)
        {
            _playbackSpeed = Math.Clamp(speed, 0.5, 2.0);
            _mediaService.SetPlaybackSpeed(_playbackSpeed);
            OnPropertyChanged(nameof(PlaybackSpeed));
            RaiseStateChanged();
        }

        private void OnPlaybackCompleted()
        {
            if (_queue.Count > 0)
                PlayNextTrack();
        }

        private void LoadQueueFromStore()
        {
            PlayerQueueState state = PlayerQueueStore.Load();
            _queue.Clear();
            _queue.AddRange(state.Items.Where(path => !string.IsNullOrWhiteSpace(path)));
            _queueIndex = _queue.Count > 0 ? Math.Clamp(state.Index, 0, _queue.Count - 1) : -1;
        }

        private void PersistQueue()
        {
            PlayerQueueStore.Save(new PlayerQueueState
            {
                Items = new List<string>(_queue),
                Index = _queueIndex,
            });
        }

        private void OnMediaStateChanged()
        {
            OnPropertyChanged(
                nameof(HasActiveMedia),
                nameof(ActiveTitle),
                nameof(ActiveKind),
                nameof(IsPlaying),
                nameof(IsPaused),
                nameof(PlaybackProgressText),
                nameof(PlaybackRelative));
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private static string FormatTimeSpan(TimeSpan value)
        {
            if (value.TotalHours >= 1)
                return value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
            return value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>List item wrapper for a media browser entry.</summary>
    public class MediaItemViewModel
    {
        public MediaItemViewModel(MediaItemModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public MediaItemModel Model { get; }

        public string Path => Model.Path;

        public string Name => Model.Name;

        public MediaKind Kind => Model.Kind;

        public string MetaText
        {
            get
            {
                if (Kind == MediaKind.Folder)
                    return "Folder";

                string size = FormatSize(Model.SizeBytes);
                string date = Model.ModifiedLocal.ToString("g", CultureInfo.CurrentCulture);
                return size + " · " + date;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
