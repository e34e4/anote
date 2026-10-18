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

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Camera tab supporting photo capture and conversion with selectable
    /// format (JPG, PNG, WEBP) and compression quality percentage (1-100%).
    /// </summary>
    public class CameraViewModel : ViewModelBase
    {
        private readonly ICameraService _cameraService;
        private readonly ISettingsService _settingsService;
        private readonly IFilePickerService _filePickerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private SettingsModel _settings;
        private List<CameraPhotoItemViewModel> _photos = new List<CameraPhotoItemViewModel>();
        private string _filter;
        private string _renamingFileName;
        private string _draftNewName;
        private bool _isBusy;

        public CameraViewModel(
            ICameraService cameraService,
            ISettingsService settingsService,
            IFilePickerService filePickerService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _cameraService = cameraService;
            _settingsService = settingsService;
            _filePickerService = filePickerService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _settings = _settingsService.LoadSettingsOrDefault();

            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            TakePhotoCommand = new AsyncRelayCommand(TakePhotoAsync);
            TakeSystemPhotoCommand = new AsyncRelayCommand(TakeSystemPhotoAsync);
            ConvertImageCommand = new AsyncRelayCommand(ConvertImageAsync);
            SelectFormatCommand = new RelayCommand<string>(SelectFormat);
            SelectQualityPresetCommand = new RelayCommand<int>(SelectQualityPreset);
            ToggleLensCommand = new RelayCommand(() => UseFrontLens = !UseFrontLens);
            CycleFlashModeCommand = new RelayCommand(CycleFlashMode);

            OpenPhotoCommand = new AsyncRelayCommand<CameraPhotoItemViewModel>(OpenPhotoAsync);
            ReencodePhotoCommand = new AsyncRelayCommand<CameraPhotoItemViewModel>(ReencodePhotoAsync);
            SaveToGalleryCommand = new AsyncRelayCommand<CameraPhotoItemViewModel>(SaveToGalleryAsync);
            SharePhotoCommand = new AsyncRelayCommand<CameraPhotoItemViewModel>(SharePhotoAsync);
            StartRenameCommand = new RelayCommand<CameraPhotoItemViewModel>(StartRename);
            SaveRenameCommand = new RelayCommand(SaveRename);
            CancelRenameCommand = new RelayCommand(CancelRename);
            DeletePhotoCommand = new AsyncRelayCommand<CameraPhotoItemViewModel>(DeletePhotoAsync);

            Reload();
        }

        /// <summary>Raised when asynchronous thumbnails finish loading.</summary>
        public event Action StateChanged;

        public ICommand GrantPermissionCommand { get; }
        public ICommand TakePhotoCommand { get; }
        public ICommand TakeSystemPhotoCommand { get; }
        public ICommand ConvertImageCommand { get; }
        public ICommand SelectFormatCommand { get; }
        public ICommand SelectQualityPresetCommand { get; }
        public ICommand ToggleLensCommand { get; }
        public ICommand CycleFlashModeCommand { get; }

        public ICommand OpenPhotoCommand { get; }
        public ICommand ReencodePhotoCommand { get; }
        public ICommand SaveToGalleryCommand { get; }
        public ICommand SharePhotoCommand { get; }
        public ICommand StartRenameCommand { get; }
        public ICommand SaveRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand DeletePhotoCommand { get; }

        /// <summary>Gets a value indicating whether camera capture is supported on this platform.</summary>
        public bool IsSupported => _cameraService.IsSupported;

        /// <summary>Gets a value indicating whether camera permission is needed.</summary>
        public bool NeedsPermission => _cameraService.IsSupported && !_cameraService.HasPermission;

        /// <summary>Gets or sets a value indicating whether a capture or conversion is running.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Gets or sets the selected photo format ("jpg", "png", or "webp").</summary>
        public string PhotoFormat
        {
            get
            {
                string fmt = (_settings.CameraPhotoFormat ?? "jpg").Trim().ToLowerInvariant();
                if (fmt != "jpg" && fmt != "png" && fmt != "webp")
                    fmt = "jpg";
                return fmt;
            }
            set
            {
                string normalized = (value ?? "jpg").Trim().ToLowerInvariant();
                if (normalized != "jpg" && normalized != "png" && normalized != "webp")
                    normalized = "jpg";
                if (!string.Equals(_settings.CameraPhotoFormat, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.CameraPhotoFormat = normalized;
                    SaveSettings();
                    OnPropertyChanged(nameof(PhotoFormat));
                }
            }
        }

        /// <summary>Gets or sets the selected photo quality percentage (1..100).</summary>
        public int PhotoQuality
        {
            get => Math.Clamp(_settings.CameraPhotoQuality <= 0 ? 90 : _settings.CameraPhotoQuality, 1, 100);
            set
            {
                int clamped = Math.Clamp(value, 1, 100);
                if (_settings.CameraPhotoQuality != clamped)
                {
                    _settings.CameraPhotoQuality = clamped;
                    SaveSettings();
                    OnPropertyChanged(nameof(PhotoQuality));
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether the front camera lens is used by default.</summary>
        public bool UseFrontLens
        {
            get => _settings.CameraUseFrontLens;
            set
            {
                if (_settings.CameraUseFrontLens != value)
                {
                    _settings.CameraUseFrontLens = value;
                    SaveSettings();
                    OnPropertyChanged(nameof(UseFrontLens));
                }
            }
        }

        /// <summary>Gets or sets the camera flash mode ("auto", "on", "off", "torch").</summary>
        public string FlashMode
        {
            get => string.IsNullOrEmpty(_settings.CameraFlashMode) ? "auto" : _settings.CameraFlashMode;
            set
            {
                string normalized = string.IsNullOrEmpty(value) ? "auto" : value.ToLowerInvariant();
                if (_settings.CameraFlashMode != normalized)
                {
                    _settings.CameraFlashMode = normalized;
                    SaveSettings();
                    OnPropertyChanged(nameof(FlashMode), nameof(FlashModeLabel));
                }
            }
        }

        /// <summary>Gets a human-readable label for the current flash mode.</summary>
        public string FlashModeLabel
        {
            get
            {
                return FlashMode switch
                {
                    "on" => _languageService["camera_flash_on"],
                    "off" => _languageService["camera_flash_off"],
                    "torch" => _languageService["camera_flash_torch"],
                    _ => _languageService["camera_flash_auto"],
                };
            }
        }

        /// <summary>Gets or sets a value indicating whether captured photos are also saved to Pictures/aNote.</summary>
        public bool AutoSaveToGallery
        {
            get => _settings.CameraAutoSaveToGallery;
            set
            {
                if (_settings.CameraAutoSaveToGallery != value)
                {
                    _settings.CameraAutoSaveToGallery = value;
                    SaveSettings();
                    OnPropertyChanged(nameof(AutoSaveToGallery));
                }
            }
        }

        /// <summary>Gets or sets the search filter for saved photos.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilteredPhotos), nameof(HasNoMatches));
                }
            }
        }

        /// <summary>Gets the filtered list of photos.</summary>
        public IReadOnlyList<CameraPhotoItemViewModel> FilteredPhotos
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _photos;
                return _photos.Where(p =>
                    p.FileName.ToLowerInvariant().Contains(query) ||
                    p.FormatBadge.ToLowerInvariant().Contains(query)).ToList();
            }
        }

        /// <summary>Gets a value indicating whether there are no saved photos yet.</summary>
        public bool IsEmpty => _photos.Count == 0;

        /// <summary>Gets a value indicating whether the filter matched no photos.</summary>
        public bool HasNoMatches => !IsEmpty && FilteredPhotos.Count == 0;

        /// <summary>Gets the total photo count summary text.</summary>
        public string CountText => _languageService.LoadTextFmt("camera_count", _photos.Count);

        /// <summary>Gets or sets the draft name when renaming a photo.</summary>
        public string DraftNewName
        {
            get => _draftNewName;
            set => SetProperty(ref _draftNewName, value);
        }

        /// <summary>Determines whether a given photo item is currently being renamed.</summary>
        public bool IsRenaming(CameraPhotoItemViewModel item)
        {
            return (item != null) && string.Equals(_renamingFileName, item.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reloads the list of captured photos and loads their thumbnails in the background.</summary>
        public void Reload()
        {
            var existingThumbs = _photos.ToDictionary(p => p.FileName, p => p.ThumbnailDataUrl, StringComparer.OrdinalIgnoreCase);
            _photos = _cameraService.GetPhotos()
                .Select(m =>
                {
                    var vm = new CameraPhotoItemViewModel(m);
                    if (existingThumbs.TryGetValue(m.FileName, out string thumb))
                        vm.ThumbnailDataUrl = thumb;
                    return vm;
                })
                .ToList();

            OnPropertyChanged(nameof(FilteredPhotos), nameof(IsEmpty), nameof(HasNoMatches), nameof(CountText), nameof(NeedsPermission));
            _ = LoadMissingThumbnailsAsync();
        }

        private async Task LoadMissingThumbnailsAsync()
        {
            var pending = _photos.Where(p => string.IsNullOrEmpty(p.ThumbnailDataUrl)).Take(24).ToList();
            foreach (CameraPhotoItemViewModel item in pending)
            {
                string dataUrl = await _cameraService.GetPhotoPreviewDataUrlAsync(item.FileName, 240);
                if (!string.IsNullOrEmpty(dataUrl))
                {
                    item.ThumbnailDataUrl = dataUrl;
                    StateChanged?.Invoke();
                }
            }
        }

        public async Task GrantPermissionAsync()
        {
            await _cameraService.RequestPermissionAsync();
            OnPropertyChanged(nameof(NeedsPermission));
        }

        public void SelectFormat(string format)
        {
            if (!string.IsNullOrWhiteSpace(format))
                PhotoFormat = format;
        }

        public void SelectQualityPreset(int quality)
        {
            PhotoQuality = quality;
        }

        public void CycleFlashMode()
        {
            FlashMode = FlashMode switch
            {
                "auto" => "on",
                "on" => "torch",
                "torch" => "off",
                _ => "auto",
            };
        }

        public async Task TakePhotoAsync()
        {
            if (IsBusy || !_cameraService.IsSupported)
                return;

            IsBusy = true;
            try
            {
                CameraPhotoModel captured = await _cameraService.CapturePhotoAsync(
                    PhotoFormat,
                    PhotoQuality,
                    UseFrontLens,
                    FlashMode,
                    AutoSaveToGallery);
                if (captured != null)
                {
                    // Update format/quality if changed inside the camera HUD
                    if (!string.IsNullOrEmpty(captured.Format))
                        PhotoFormat = captured.Format;
                    if (captured.Quality > 0)
                        PhotoQuality = captured.Quality;

                    Reload();
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt(
                        "camera_photo_saved_fmt",
                        captured.FileName,
                        captured.Format.ToUpperInvariant(),
                        captured.Quality));
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task TakeSystemPhotoAsync()
        {
            if (IsBusy || !_cameraService.IsSupported)
                return;

            IsBusy = true;
            try
            {
                CameraPhotoModel captured = await _cameraService.CaptureWithSystemCameraAsync(
                    PhotoFormat,
                    PhotoQuality,
                    AutoSaveToGallery);
                if (captured != null)
                {
                    Reload();
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt(
                        "camera_photo_saved_fmt",
                        captured.FileName,
                        captured.Format.ToUpperInvariant(),
                        captured.Quality));
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ConvertImageAsync()
        {
            if (IsBusy)
                return;

            bool picked = await _filePickerService.PickImage();
            if (!picked)
                return;

            IsBusy = true;
            try
            {
                byte[] bytes = await _filePickerService.ReadPickedFile();
                CameraPhotoModel converted = await _cameraService.ConvertExistingImageAsync(
                    bytes,
                    PhotoFormat,
                    PhotoQuality,
                    AutoSaveToGallery);
                if (converted != null)
                {
                    Reload();
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt(
                        "camera_photo_saved_fmt",
                        converted.FileName,
                        converted.Format.ToUpperInvariant(),
                        converted.Quality));
                }
                else
                {
                    _feedbackService?.ShowToast(_languageService["camera_convert_failed"], FeedbackSeverity.Warning);
                }
            }
            catch (Exception)
            {
                _feedbackService?.ShowToast(_languageService["camera_convert_failed"], FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ReencodePhotoAsync(CameraPhotoItemViewModel item)
        {
            if (item == null || IsBusy || !File.Exists(item.Model.FilePath))
                return;

            IsBusy = true;
            try
            {
                byte[] rawBytes = await File.ReadAllBytesAsync(item.Model.FilePath);
                CameraPhotoModel converted = await _cameraService.ConvertExistingImageAsync(
                    rawBytes,
                    PhotoFormat,
                    PhotoQuality,
                    AutoSaveToGallery);
                if (converted != null)
                {
                    Reload();
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt(
                        "camera_photo_saved_fmt",
                        converted.FileName,
                        converted.Format.ToUpperInvariant(),
                        converted.Quality));
                }
            }
            catch (Exception)
            {
                _feedbackService?.ShowToast(_languageService["camera_convert_failed"], FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task OpenPhotoAsync(CameraPhotoItemViewModel item)
        {
            if (item == null)
                return;
            bool opened = await _cameraService.OpenPhotoAsync(item.FileName);
            if (!opened)
                _feedbackService?.ShowToast(_languageService["clipboard_image_open_failed"], FeedbackSeverity.Warning);
        }

        public async Task SaveToGalleryAsync(CameraPhotoItemViewModel item)
        {
            if (item == null)
                return;
            string savedPath = await _cameraService.SaveToPublicGalleryAsync(item.FileName);
            if (!string.IsNullOrEmpty(savedPath))
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("camera_exported", savedPath));
            else
                _feedbackService?.ShowToast(_languageService["camera_export_failed"], FeedbackSeverity.Warning);
        }

        public async Task SharePhotoAsync(CameraPhotoItemViewModel item)
        {
            if (item == null)
                return;
            await _cameraService.SharePhotoAsync(item.FileName);
        }

        private void StartRename(CameraPhotoItemViewModel item)
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

            bool ok = _cameraService.RenamePhoto(_renamingFileName, _draftNewName);
            if (!ok)
                _feedbackService?.ShowToast(_languageService["camera_rename_failed"], FeedbackSeverity.Warning);
            CancelRename();
            Reload();
        }

        public async Task DeletePhotoAsync(CameraPhotoItemViewModel item)
        {
            if (item == null)
                return;

            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService.LoadTextFmt("camera_delete_confirm", item.FileName),
                _languageService["camera_delete"],
                MessageBoxButtons.YesNoCancel,
                false);
            if (answer != MessageBoxResult.Yes)
                return;

            _cameraService.DeletePhoto(item.FileName);
            Reload();
        }

        private void SaveSettings()
        {
            _settingsService.TrySaveSettingsToLocalDevice(_settings);
        }
    }

    /// <summary>
    /// Item view model for a single captured/converted photo in the Camera tab.
    /// </summary>
    public class CameraPhotoItemViewModel
    {
        public CameraPhotoItemViewModel(CameraPhotoModel model)
        {
            Model = model;
        }

        public CameraPhotoModel Model { get; }

        public string FileName => Model.FileName;

        public string DisplayTitle => Path.GetFileNameWithoutExtension(Model.FileName);

        public string FormatBadge => (Model.Format ?? "jpg").ToUpperInvariant();

        public string QualityText => Model.Quality + "%";

        public string DimensionsText => (Model.Width > 0 && Model.Height > 0)
            ? string.Format(CultureInfo.InvariantCulture, "{0}×{1}", Model.Width, Model.Height)
            : string.Empty;

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

        public string ThumbnailDataUrl { get; set; }
    }
}
