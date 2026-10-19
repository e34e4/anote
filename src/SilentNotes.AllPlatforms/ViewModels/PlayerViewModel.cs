// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

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

        private string _filter;
        private string _kindFilter = "all"; // all | audio | video | image
        private List<MediaItemViewModel> _items = new List<MediaItemViewModel>();
        private bool _isBusy;

        public PlayerViewModel(
            IMediaPlayerService mediaService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _mediaService = mediaService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _mediaService.StateChanged += OnMediaStateChanged;

            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            RefreshCommand = new RelayCommand(Reload);
            NavigateUpCommand = new RelayCommand(NavigateUp);
            EnterItemCommand = new AsyncRelayCommand<MediaItemViewModel>(EnterOrOpenAsync);
            PickFileCommand = new AsyncRelayCommand(PickFileAsync);
            TogglePlayCommand = new RelayCommand(TogglePlay);
            StopCommand = new RelayCommand(() => _mediaService.Stop());
            SeekCommand = new RelayCommand<double>(Seek);
            SetKindFilterCommand = new RelayCommand<string>(SetKindFilter);
            GoToRootCommand = new RelayCommand<string>(GoToRoot);

            Reload();
        }

        /// <summary>Raised when the UI should re-render.</summary>
        public event Action StateChanged;

        public ICommand GrantPermissionCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand EnterItemCommand { get; }
        public ICommand PickFileCommand { get; }
        public ICommand TogglePlayCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SeekCommand { get; }
        public ICommand SetKindFilterCommand { get; }
        public ICommand GoToRootCommand { get; }

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
            Reload();
        }

        private void GoToRoot(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            _mediaService.GoToRoot(key);
            Reload();
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
                    _mediaService.EnterDirectory(item.Path);
                    Reload();
                    return;
                }

                if (item.Kind == MediaKind.Image)
                {
                    await _mediaService.OpenImageAsync(item.Model);
                    return;
                }

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
