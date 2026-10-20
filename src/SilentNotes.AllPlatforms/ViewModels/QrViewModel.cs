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
    /// View model of the QR tab. It can scan QR codes with the camera, decode codes from image
    /// files, manage the history of the scans and generate own QR codes.
    /// </summary>
    public class QrViewModel : ViewModelBase
    {
        /// <summary>Maximum number of items shown on the start view of the tab.</summary>
        private const int PreviewItemCount = 6;
        private const int QrImageSize = 640;

        private readonly IQrService _qrService;
        private readonly IQrHistoryService _historyService;
        private readonly ISettingsService _settingsService;
        private readonly IClipboardService _clipboardService;
        private readonly INativeBrowserService _browserService;
        private readonly ISharingService _sharingService;
        private readonly IFilePickerService _filePickerService;
        private readonly IImageFileService _imageFileService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private SettingsModel _settings;
        private List<QrItemViewModel> _items = new List<QrItemViewModel>();
        private QrItemViewModel _result;
        private bool _isHistoryView;
        private bool _isCreateVisible;
        private bool _isSettingsVisible;
        private bool _isBusy;
        private string _createText = string.Empty;
        private byte[] _createdPng;

        /// <summary>
        /// Initializes a new instance of the <see cref="QrViewModel"/> class.
        /// </summary>
        public QrViewModel(
            IQrService qrService,
            IQrHistoryService historyService,
            ISettingsService settingsService,
            IClipboardService clipboardService,
            INativeBrowserService browserService,
            ISharingService sharingService,
            IFilePickerService filePickerService,
            IImageFileService imageFileService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _qrService = qrService;
            _historyService = historyService;
            _settingsService = settingsService;
            _clipboardService = clipboardService;
            _browserService = browserService;
            _sharingService = sharingService;
            _filePickerService = filePickerService;
            _imageFileService = imageFileService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _settings = settingsService.LoadSettingsOrDefault();

            ShowScanViewCommand = new RelayCommand(() => IsHistoryView = false);
            ShowHistoryViewCommand = new RelayCommand(() => IsHistoryView = true);
            ShowCreateCommand = new RelayCommand(() => { CloseOverlay(); IsCreateVisible = true; });
            ShowSettingsCommand = new RelayCommand(() => { CloseOverlay(); IsSettingsVisible = true; });
            CloseOverlayCommand = new RelayCommand(CloseOverlay);
            DecodeImageCommand = new AsyncRelayCommand(DecodeImageAsync);
            GenerateQrCommand = new AsyncRelayCommand(GenerateQrAsync);
            SaveQrImageCommand = new AsyncRelayCommand(SaveQrImageAsync);
            OpenItemCommand = new RelayCommand<QrItemViewModel>(OpenItem);
            CopyItemCommand = new AsyncRelayCommand<QrItemViewModel>(CopyItemAsync);
            ShareItemCommand = new AsyncRelayCommand<QrItemViewModel>(ShareItemAsync);
            ShowResultCommand = new RelayCommand<QrItemViewModel>(item => Result = item);
            DeleteItemCommand = new RelayCommand<QrItemViewModel>(DeleteItem);
            ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
            ShareCreateTextCommand = new AsyncRelayCommand(() => _sharingService.ShareText(CreateText));
            CopyCreateTextCommand = new AsyncRelayCommand(CopyCreateTextAsync);

            Reload();
        }

        /// <summary>Gets the command to show the scan / start view.</summary>
        public ICommand ShowScanViewCommand { get; }

        /// <summary>Gets the command to show the full history view.</summary>
        public ICommand ShowHistoryViewCommand { get; }

        /// <summary>Gets the command to open the create QR overlay.</summary>
        public ICommand ShowCreateCommand { get; }

        /// <summary>Gets the command to open the QR settings overlay.</summary>
        public ICommand ShowSettingsCommand { get; }

        /// <summary>Gets the command to close any open overlay.</summary>
        public ICommand CloseOverlayCommand { get; }

        /// <summary>Gets the command to decode a QR code from an image file.</summary>
        public ICommand DecodeImageCommand { get; }

        /// <summary>Gets the command to generate a QR code image from text.</summary>
        public ICommand GenerateQrCommand { get; }

        /// <summary>Gets the command to save the generated QR code image.</summary>
        public ICommand SaveQrImageCommand { get; }

        /// <summary>Gets the command to open a scanned URL in the browser.</summary>
        public ICommand OpenItemCommand { get; }

        /// <summary>Gets the command to copy a scanned text to the clipboard.</summary>
        public ICommand CopyItemCommand { get; }

        /// <summary>Gets the command to share a scanned text.</summary>
        public ICommand ShareItemCommand { get; }

        /// <summary>Gets the command to show a history item in the result card.</summary>
        public ICommand ShowResultCommand { get; }

        /// <summary>Gets the command to delete a history item.</summary>
        public ICommand DeleteItemCommand { get; }

        /// <summary>Gets the command to clear the whole QR history.</summary>
        public ICommand ClearHistoryCommand { get; }

        /// <summary>Gets the command to share the create-text.</summary>
        public ICommand ShareCreateTextCommand { get; }

        /// <summary>Gets the command to copy the create-text to the clipboard.</summary>
        public ICommand CopyCreateTextCommand { get; }

        /// <summary>Gets a value indicating whether the platform can scan codes with the camera.</summary>
        public bool IsScanSupported
        {
            get { return _qrService.IsScanSupported; }
        }

        /// <summary>Gets a value indicating whether the app is allowed to use the camera.</summary>
        public bool NeedsCameraPermission
        {
            get { return _qrService.IsScanSupported && !_qrService.HasCameraPermission; }
        }

        /// <summary>Gets or sets all history items, the newest item is at the top.</summary>
        public List<QrItemViewModel> Items
        {
            get { return _items; }
            set { SetProperty(ref _items, value); }
        }

        /// <summary>Gets the first items of the history for the start view.</summary>
        public List<QrItemViewModel> PreviewItems
        {
            get { return _items.Take(PreviewItemCount).ToList(); }
        }

        /// <summary>Gets or sets the result which is shown in the result card, can be null.</summary>
        public QrItemViewModel Result
        {
            get { return _result; }
            set { SetProperty(ref _result, value); }
        }

        /// <summary>Gets or sets a value indicating whether the history view is shown.</summary>
        public bool IsHistoryView
        {
            get { return _isHistoryView; }
            set { SetProperty(ref _isHistoryView, value); }
        }

        /// <summary>Gets or sets a value indicating whether the create overlay is shown.</summary>
        public bool IsCreateVisible
        {
            get { return _isCreateVisible; }
            set { SetProperty(ref _isCreateVisible, value); }
        }

        /// <summary>Gets or sets a value indicating whether the settings overlay is shown.</summary>
        public bool IsSettingsVisible
        {
            get { return _isSettingsVisible; }
            set { SetProperty(ref _isSettingsVisible, value); }
        }

        /// <summary>Gets a value indicating whether any overlay is open.</summary>
        public bool IsAnyOverlayOpen
        {
            get { return IsCreateVisible || IsSettingsVisible; }
        }

        /// <summary>Gets or sets a value indicating whether a scan or decode operation is running.</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }

        /// <summary>Gets or sets the text which should be encoded as a QR code.</summary>
        public string CreateText
        {
            get { return _createText; }
            set { SetProperty(ref _createText, value); }
        }

        /// <summary>Gets the generated QR code as base64 encoded PNG image for an img tag.</summary>
        public string QrImageBase64
        {
            get { return (_createdPng != null) ? "data:image/png;base64," + Convert.ToBase64String(_createdPng) : null; }
        }

        /// <summary>Gets a value indicating whether a QR code image was generated.</summary>
        public bool HasQrImage
        {
            get { return _createdPng != null; }
        }

        /// <summary>
        /// Gets the generated QR PNG bytes, or null when none is available.
        /// </summary>
        public byte[] CreatedPngBytes
        {
            get { return _createdPng; }
        }

        /// <summary>Gets or sets a value indicating whether the device should vibrate after a scan.</summary>
        public bool VibrateOnScan
        {
            get { return _settings.QrVibrateOnScan; }
            set { _settings.QrVibrateOnScan = value; SaveSettings(); }
        }

        /// <summary>Gets or sets a value indicating whether scan results are copied to the clipboard.</summary>
        public bool AutoCopyToClipboard
        {
            get { return _settings.QrAutoCopyToClipboard; }
            set { _settings.QrAutoCopyToClipboard = value; SaveSettings(); }
        }

        /// <summary>Gets or sets a value indicating whether scans are stored in the history.</summary>
        public bool KeepHistory
        {
            get { return _settings.QrKeepHistory; }
            set { _settings.QrKeepHistory = value; SaveSettings(); }
        }

        /// <summary>
        /// Closes all overlays.
        /// </summary>
        public void CloseOverlay()
        {
            IsCreateVisible = false;
            IsSettingsVisible = false;
        }

        /// <summary>
        /// Reloads the history items from the local storage.
        /// </summary>
        public void Reload()
        {
            QrHistoryModel model = _historyService.LoadHistoryOrDefault();
            Items = model.Items
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new QrItemViewModel(item))
                .ToList();
        }

        /// <summary>
        /// Asks for the camera permission if necessary.
        /// </summary>
        /// <returns>True if the camera can be used, otherwise false.</returns>
        public async Task<bool> RequestCameraPermissionAsync()
        {
            if (!_qrService.IsScanSupported)
                return false;
            if (_qrService.HasCameraPermission)
                return true;
            return await _qrService.RequestCameraPermissionAsync();
        }

        /// <summary>
        /// Opens the camera and scans for a QR code, the result is added to the history and is
        /// shown in the result card.
        /// </summary>
        /// <returns>Task for async calls.</returns>
        public async Task ScanAsync()
        {
            if (IsBusy || !_qrService.IsScanSupported)
                return;

            if (!await RequestCameraPermissionAsync())
                return;

            IsBusy = true;
            try
            {
                string text = await _qrService.ScanQrAsync();
                if (!string.IsNullOrEmpty(text))
                    HandleScannedText(text);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Lets the user pick an image file and tries to decode a QR code from it.
        /// </summary>
        /// <returns>Task for async calls.</returns>
        public async Task DecodeImageAsync()
        {
            if (IsBusy)
                return;

            bool picked = await _filePickerService.PickImage();
            if (!picked)
                return;

            IsBusy = true;
            try
            {
                byte[] image = await _filePickerService.ReadPickedFile();
                string text = await _qrService.DecodeImageAsync(image);
                if (!string.IsNullOrEmpty(text))
                    HandleScannedText(text);
                else
                    _feedbackService.ShowToast(_languageService["qr_no_code_found"], FeedbackSeverity.Warning);
            }
            catch (Exception)
            {
                _feedbackService.ShowToast(_languageService["qr_no_code_found"], FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Generates a QR code image for the text of <see cref="CreateText"/>.
        /// </summary>
        /// <returns>Task for async calls.</returns>
        public async Task GenerateQrAsync()
        {
            if (IsBusy)
                return;

            string text = CreateText?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            IsBusy = true;
            try
            {
                _createdPng = await _qrService.GenerateQrPngAsync(text, QrImageSize);
                OnPropertyChanged(nameof(QrImageBase64), nameof(HasQrImage));
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Saves the generated QR code image into the pictures folder of the device.
        /// </summary>
        /// <returns>Task for async calls.</returns>
        public async Task SaveQrImageAsync()
        {
            if (_createdPng == null)
                return;

            try
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(), "anote_qr_" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(tempFilePath, _createdPng);
                string displayPath = await _imageFileService.SaveImageToPicturesAsync(tempFilePath);
                if (!string.IsNullOrEmpty(displayPath))
                    _feedbackService.ShowToast(string.Format(_languageService["qr_saved_to_pictures"], displayPath), FeedbackSeverity.Info);
                else
                    _feedbackService.ShowToast(_languageService["qr_save_failed"], FeedbackSeverity.Error);
            }
            catch (Exception)
            {
                _feedbackService.ShowToast(_languageService["qr_save_failed"], FeedbackSeverity.Error);
            }
        }

        private void HandleScannedText(string text)
        {
            if (_settings.QrVibrateOnScan)
                _qrService.Vibrate();

            if (_settings.QrAutoCopyToClipboard)
                _ = _clipboardService.SetTextAsync(text);

            if (_settings.QrKeepHistory)
            {
                _historyService.AddText(text, QrHistoryModel.MaxHistoryItems);
                Reload();
            }

            Result = new QrItemViewModel(Guid.Empty, text, DateTime.Now);
            IsHistoryView = false;
        }

        private void OpenItem(QrItemViewModel item)
        {
            if ((item != null) && item.IsUrl)
                _browserService.OpenWebsite(item.Text);
        }

        private async Task CopyItemAsync(QrItemViewModel item)
        {
            if (item == null)
                return;
            await _clipboardService.SetTextAsync(item.Text);
            _feedbackService.ShowToast(_languageService["qr_copied"], FeedbackSeverity.Info);
        }

        private async Task CopyCreateTextAsync()
        {
            if (string.IsNullOrEmpty(CreateText))
                return;
            await _clipboardService.SetTextAsync(CreateText);
            _feedbackService.ShowToast(_languageService["qr_copied"], FeedbackSeverity.Info);
        }

        private async Task ShareItemAsync(QrItemViewModel item)
        {
            if (item == null)
                return;
            await _sharingService.ShareText(item.Text);
        }

        private void DeleteItem(QrItemViewModel item)
        {
            if (item == null)
                return;
            _historyService.Delete(item.Id);
            Reload();
        }

        /// <summary>
        /// Asks for confirmation and clears the whole QR history.
        /// </summary>
        /// <returns>Task for async calls.</returns>
        public async Task ClearHistoryAsync()
        {
            MessageBoxResult dialogResult = await _feedbackService.ShowMessageAsync(
                _languageService["qr_clear_history_confirm"], null, MessageBoxButtons.ContinueCancel, true);
            if (dialogResult != MessageBoxResult.Continue)
                return;

            _historyService.ClearAll();
            Reload();
        }

        private void SaveSettings()
        {
            _settingsService.TrySaveSettingsToLocalDevice(_settings);
            OnPropertyChanged(nameof(VibrateOnScan), nameof(AutoCopyToClipboard), nameof(KeepHistory));
        }
    }

    /// <summary>
    /// Wrapper of a <see cref="QrItemModel"/> for the QR tab.
    /// </summary>
    public class QrItemViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QrItemViewModel"/> class.
        /// </summary>
        /// <param name="model">The model to wrap.</param>
        public QrItemViewModel(QrItemModel model)
            : this(model.Id, model.Text, model.CreatedAt)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QrItemViewModel"/> class.
        /// </summary>
        /// <param name="id">The id of the item.</param>
        /// <param name="text">The decoded text of the QR code.</param>
        /// <param name="createdAt">The local time when the code was scanned.</param>
        public QrItemViewModel(Guid id, string text, DateTime createdAt)
        {
            Id = id;
            Text = text;
            CreatedAt = createdAt;
        }

        /// <summary>Gets the id of the history item.</summary>
        public Guid Id { get; }

        /// <summary>Gets the decoded text of the QR code.</summary>
        public string Text { get; }

        /// <summary>Gets the local time when the code was scanned.</summary>
        public DateTime CreatedAt { get; }

        /// <summary>Gets a value indicating whether the text is a web link.</summary>
        public bool IsUrl
        {
            get
            {
                return Uri.TryCreate(Text, UriKind.Absolute, out Uri uri)
                    && ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps));
            }
        }

        /// <summary>Gets a display text for the scan date.</summary>
        public string DateText
        {
            get { return CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
        }
    }
}
