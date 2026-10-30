// Copyright © 2025 aNote.
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
    /// View model for the File tab (file exchange over WebDAV).
    /// </summary>
    public class FileViewModel : ViewModelBase
    {
        private readonly IWebdavFileService _webdavService;
        private readonly ISettingsService _settingsService;
        private readonly IFilePickerService _filePickerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private List<WebdavFileItemViewModel> _items = new List<WebdavFileItemViewModel>();
        private string _filter;
        private string _currentSubPath = string.Empty;
        private bool _isBusy;
        private bool _isLoadedOnce;
        private bool _isSettingsOpen;
        private bool _isNewFolderOpen;
        private bool _isNewTextFileOpen;
        private string _newFolderName;
        private string _newTextFileName;
        private string _lastError;
        private int _activePathTabIndex;
        private List<PathTabSessionModel> _pathTabs = new List<PathTabSessionModel>();

        private string _editUrl;
        private string _editFolder;
        private string _editUsername;
        private string _editPassword;

        public FileViewModel(
            IWebdavFileService webdavService,
            ISettingsService settingsService,
            IFilePickerService filePickerService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _webdavService = webdavService;
            _settingsService = settingsService;
            _filePickerService = filePickerService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            LoadCredentialsFromSettings();
            LoadPathTabsFromSettings();

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            UploadFileCommand = new AsyncRelayCommand(UploadFileAsync);
            NavigateUpCommand = new AsyncRelayCommand(NavigateUpAsync);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            CloseOverlayCommand = new RelayCommand(CloseOverlay);
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
            OpenNewFolderCommand = new RelayCommand(() =>
            {
                IsNewTextFileOpen = false;
                _newFolderName = string.Empty;
                IsNewFolderOpen = true;
            });
            CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync);
            OpenNewTextFileCommand = new RelayCommand(() =>
            {
                IsNewFolderOpen = false;
                _newTextFileName = "note.txt";
                IsNewTextFileOpen = true;
            });
            CreateTextFileCommand = new AsyncRelayCommand(CreateTextFileAsync);
            AddPathTabCommand = new AsyncRelayCommand(AddPathTabAsync);
            ClosePathTabCommand = new AsyncRelayCommand<int>(ClosePathTabAsync);
            SelectPathTabCommand = new AsyncRelayCommand<int>(SelectPathTabAsync);
        }

        public ICommand RefreshCommand { get; }
        public ICommand UploadFileCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand CloseOverlayCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand OpenNewFolderCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand OpenNewTextFileCommand { get; }
        public ICommand CreateTextFileCommand { get; }
        public ICommand AddPathTabCommand { get; }
        public ICommand ClosePathTabCommand { get; }
        public ICommand SelectPathTabCommand { get; }

        /// <summary>Gets a value indicating whether WebDAV server URL is configured.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(EffectiveUrl);

        /// <summary>Gets or sets a value indicating whether a network operation is in progress.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Gets the last error message, if any.</summary>
        public string LastError
        {
            get => _lastError;
            private set => SetProperty(ref _lastError, value);
        }

        /// <summary>Gets or sets the search filter for files in the current directory.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    OnPropertyChanged(nameof(FilteredItems), nameof(HasNoMatches));
            }
        }

        /// <summary>Gets the filtered files and folders.</summary>
        public IReadOnlyList<WebdavFileItemViewModel> FilteredItems
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _items;
                return _items.Where(i => i.Name.ToLowerInvariant().Contains(query)).ToList();
            }
        }

        /// <summary>Gets a value indicating whether the current directory has no items.</summary>
        public bool IsEmpty => _items.Count == 0;

        /// <summary>Gets a value indicating whether the filter has no matches.</summary>
        public bool HasNoMatches => !IsEmpty && FilteredItems.Count == 0;

        /// <summary>Gets a value indicating whether we are inside a subfolder.</summary>
        public bool CanNavigateUp => !string.IsNullOrEmpty(_currentSubPath);

        /// <summary>Gets the current remote path display string.</summary>
        public string CurrentPathDisplay
        {
            get
            {
                string root = string.IsNullOrWhiteSpace(EffectiveFolder) ? "/" : "/" + EffectiveFolder.Trim('/');
                if (string.IsNullOrEmpty(_currentSubPath))
                    return root;
                return root.TrimEnd('/') + "/" + _currentSubPath.Trim('/');
            }
        }

        /// <summary>Gets or sets a value indicating whether the WebDAV settings overlay is open.</summary>
        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetProperty(ref _isSettingsOpen, value);
        }

        /// <summary>Gets or sets a value indicating whether the new folder input bar is open.</summary>
        public bool IsNewFolderOpen
        {
            get => _isNewFolderOpen;
            set => SetProperty(ref _isNewFolderOpen, value);
        }

        /// <summary>Gets or sets a value indicating whether the new text file input bar is open.</summary>
        public bool IsNewTextFileOpen
        {
            get => _isNewTextFileOpen;
            set => SetProperty(ref _isNewTextFileOpen, value);
        }

        /// <summary>Gets a value indicating whether any overlay is open (for back button handling).</summary>
        public bool IsAnyOverlayOpen => _isSettingsOpen || _isNewFolderOpen || _isNewTextFileOpen;

        /// <summary>Gets or sets the name of the folder to create.</summary>
        public string NewFolderName
        {
            get => _newFolderName;
            set => SetProperty(ref _newFolderName, value);
        }

        /// <summary>Gets or sets the name of the text file to create.</summary>
        public string NewTextFileName
        {
            get => _newTextFileName;
            set => SetProperty(ref _newTextFileName, value);
        }

        /// <summary>Gets path sessions (sub-tabs) for the File browser.</summary>
        public IReadOnlyList<PathTabSessionModel> PathTabs => _pathTabs;

        /// <summary>Gets the active path session index.</summary>
        public int ActivePathTabIndex => _activePathTabIndex;

        public string EditUrl
        {
            get => _editUrl;
            set => SetProperty(ref _editUrl, value);
        }

        public string EditFolder
        {
            get => _editFolder;
            set => SetProperty(ref _editFolder, value);
        }

        public string EditUsername
        {
            get => _editUsername;
            set => SetProperty(ref _editUsername, value);
        }

        public string EditPassword
        {
            get => _editPassword;
            set => SetProperty(ref _editPassword, value);
        }

        private string EffectiveUrl { get; set; }
        private string EffectiveFolder { get; set; }
        private string EffectiveUsername { get; set; }
        private string EffectivePassword { get; set; }

        /// <summary>Initializes the File tab on first open.</summary>
        public async Task InitializeAsync()
        {
            LoadCredentialsFromSettings();
            LoadPathTabsFromSettings();
            if (!_isLoadedOnce && IsConfigured)
            {
                _isLoadedOnce = true;
                await ApplyActivePathTabAsync();
            }
        }

        /// <summary>Closes any open overlay.</summary>
        public void CloseOverlay()
        {
            IsSettingsOpen = false;
            IsNewFolderOpen = false;
            IsNewTextFileOpen = false;
        }

        private void OpenSettings()
        {
            LoadCredentialsFromSettings();
            EditUrl = EffectiveUrl;
            EditFolder = EffectiveFolder;
            EditUsername = EffectiveUsername;
            EditPassword = EffectivePassword;
            IsSettingsOpen = true;
        }

        public async Task SaveSettingsAsync()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.FileWebdavUrl = (EditUrl ?? string.Empty).Trim();
            settings.FileWebdavFolder = (EditFolder ?? string.Empty).Trim().Trim('/');
            settings.FileWebdavUsername = (EditUsername ?? string.Empty).Trim();
            settings.FileWebdavPassword = EditPassword ?? string.Empty;
            _settingsService.TrySaveSettingsToLocalDevice(settings);

            LoadCredentialsFromSettings();
            _currentSubPath = string.Empty;
            IsSettingsOpen = false;
            OnPropertyChanged(nameof(IsConfigured), nameof(CurrentPathDisplay), nameof(CanNavigateUp));

            if (IsConfigured)
                await RefreshAsync();
        }

        /// <summary>Refreshes the file list of the current remote folder.</summary>
        public async Task RefreshAsync()
        {
            LoadCredentialsFromSettings();
            if (!IsConfigured)
            {
                LastError = null;
                _items.Clear();
                OnPropertyChanged(nameof(IsConfigured), nameof(FilteredItems), nameof(IsEmpty));
                return;
            }

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                IReadOnlyList<WebdavFileItemModel> list = await _webdavService.ListDirectoryAsync(
                    EffectiveUrl, remoteFolder, EffectiveUsername, EffectivePassword);
                _items = list.Select(m => new WebdavFileItemViewModel(m)).ToList();
                _isLoadedOnce = true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_error_connect"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(FilteredItems), nameof(IsEmpty), nameof(HasNoMatches), nameof(CurrentPathDisplay), nameof(CanNavigateUp));
            }
        }

        /// <summary>Opens a subfolder or downloads a file when tapped.</summary>
        public async Task OpenItemAsync(WebdavFileItemViewModel item)
        {
            if (item == null || IsBusy)
                return;

            if (item.IsDirectory)
            {
                _currentSubPath = CombinePaths(_currentSubPath, item.Name);
                SaveCurrentPathToActiveTab();
                PersistPathTabs();
                await RefreshAsync();
            }
            else
            {
                await DownloadFileToDeviceAsync(item);
            }
        }

        /// <summary>Navigates up to the parent directory.</summary>
        public async Task NavigateUpAsync()
        {
            if (string.IsNullOrEmpty(_currentSubPath) || IsBusy)
                return;

            int lastSlash = _currentSubPath.LastIndexOf('/');
            _currentSubPath = (lastSlash > 0) ? _currentSubPath.Substring(0, lastSlash) : string.Empty;
            SaveCurrentPathToActiveTab();
            PersistPathTabs();
            await RefreshAsync();
        }

        /// <summary>Picks a local file from the device and uploads it to the current WebDAV folder.</summary>
        public async Task UploadFileAsync()
        {
            if (!IsConfigured)
            {
                OpenSettings();
                return;
            }
            if (IsBusy)
                return;

            bool picked = await _filePickerService.PickFile(null, "*/*");
            if (!picked)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                byte[] content = await _filePickerService.ReadPickedFile();
                string fileName = _filePickerService.GetPickedFileName();
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "upload_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".bin";

                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                await _webdavService.UploadFileAsync(
                    EffectiveUrl, remoteFolder, fileName, content, EffectiveUsername, EffectivePassword);

                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_uploaded", fileName));
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_upload_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Creates a new remote directory in the current WebDAV folder.</summary>
        public async Task CreateFolderAsync()
        {
            string name = _newFolderName?.Trim().Trim('/');
            if (string.IsNullOrEmpty(name) || IsBusy || !IsConfigured)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                await _webdavService.CreateDirectoryAsync(
                    EffectiveUrl, remoteFolder, name, EffectiveUsername, EffectivePassword);
                IsNewFolderOpen = false;
                _newFolderName = string.Empty;
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_mkdir_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Creates a new empty .txt file in the current WebDAV folder.</summary>
        public async Task CreateTextFileAsync()
        {
            string name = _newTextFileName?.Trim().Trim('/');
            if (string.IsNullOrEmpty(name) || IsBusy || !IsConfigured)
                return;

            if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                name += ".txt";

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                byte[] content = System.Text.Encoding.UTF8.GetBytes(string.Empty);
                await _webdavService.UploadFileAsync(
                    EffectiveUrl, remoteFolder, name, content, EffectiveUsername, EffectivePassword);
                IsNewTextFileOpen = false;
                _newTextFileName = string.Empty;
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_created_txt", name));
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_create_txt_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Adds a new path session starting at the current folder.</summary>
        public async Task AddPathTabAsync()
        {
            _pathTabs.Add(new PathTabSessionModel
            {
                Path = _currentSubPath ?? string.Empty,
                Label = MakePathLabel(_currentSubPath),
            });
            _activePathTabIndex = _pathTabs.Count - 1;
            PersistPathTabs();
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex));
            await Task.CompletedTask;
        }

        /// <summary>Closes a path session by index.</summary>
        public async Task ClosePathTabAsync(int index)
        {
            if (_pathTabs.Count <= 1 || index < 0 || index >= _pathTabs.Count)
                return;

            _pathTabs.RemoveAt(index);
            if (_activePathTabIndex >= _pathTabs.Count)
                _activePathTabIndex = _pathTabs.Count - 1;
            else if (_activePathTabIndex > index)
                _activePathTabIndex--;

            PersistPathTabs();
            await ApplyActivePathTabAsync();
        }

        /// <summary>Selects a path session by index.</summary>
        public async Task SelectPathTabAsync(int index)
        {
            if (index < 0 || index >= _pathTabs.Count || index == _activePathTabIndex)
                return;

            SaveCurrentPathToActiveTab();
            _activePathTabIndex = index;
            PersistPathTabs();
            await ApplyActivePathTabAsync();
        }

        /// <summary>Downloads a remote file and saves it to the device Downloads folder.</summary>
        public async Task DownloadFileToDeviceAsync(WebdavFileItemViewModel item)
        {
            if (item == null || item.IsDirectory || IsBusy)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                byte[] bytes = await _webdavService.DownloadFileAsync(
                    EffectiveUrl, remoteFolder, item.Name, EffectiveUsername, EffectivePassword);

                string savedPath = await _webdavService.SaveDownloadedFileAsync(item.Name, bytes);
                if (!string.IsNullOrEmpty(savedPath))
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_downloaded", savedPath));
                else
                    _feedbackService?.ShowToast(_languageService["file_download_failed"], FeedbackSeverity.Warning);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_download_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Downloads a remote file and opens the platform share chooser.</summary>
        public async Task ShareFileAsync(WebdavFileItemViewModel item)
        {
            if (item == null || item.IsDirectory || IsBusy)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                byte[] bytes = await _webdavService.DownloadFileAsync(
                    EffectiveUrl, remoteFolder, item.Name, EffectiveUsername, EffectivePassword);
                await _webdavService.ShareDownloadedFileAsync(item.Name, bytes);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_download_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Deletes a remote file or folder after user confirmation.</summary>
        public async Task DeleteItemAsync(WebdavFileItemViewModel item)
        {
            if (item == null || IsBusy)
                return;

            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService.LoadTextFmt("file_delete_confirm", item.Name),
                _languageService["file_delete"],
                MessageBoxButtons.YesNoCancel,
                false);
            if (answer != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                await _webdavService.DeleteItemAsync(
                    EffectiveUrl, remoteFolder, item.Name, item.IsDirectory, EffectiveUsername, EffectivePassword);
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_deleted", item.Name));
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_delete_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadCredentialsFromSettings()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (!string.IsNullOrWhiteSpace(settings.FileWebdavUrl))
            {
                EffectiveUrl = settings.FileWebdavUrl;
                EffectiveFolder = settings.FileWebdavFolder ?? string.Empty;
                EffectiveUsername = settings.FileWebdavUsername ?? string.Empty;
                EffectivePassword = settings.FileWebdavPassword ?? string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(settings.BookySyncUrl))
            {
                EffectiveUrl = settings.BookySyncUrl;
                EffectiveFolder = string.Empty;
                EffectiveUsername = settings.BookySyncUsername ?? string.Empty;
                EffectivePassword = settings.BookySyncPassword ?? string.Empty;
            }
            else if (settings.Credentials != null &&
                string.Equals(settings.Credentials.CloudStorageId, CloudStorageClientFactory.CloudStorageIdWebdav, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(settings.Credentials.Url))
            {
                EffectiveUrl = settings.Credentials.Url;
                EffectiveFolder = string.Empty;
                EffectiveUsername = settings.Credentials.Username ?? string.Empty;
                EffectivePassword = settings.Credentials.UnprotectedPassword ?? string.Empty;
            }
            else
            {
                EffectiveUrl = string.Empty;
                EffectiveFolder = string.Empty;
                EffectiveUsername = string.Empty;
                EffectivePassword = string.Empty;
            }
        }

        private static string CombinePaths(string first, string second)
        {
            string a = (first ?? string.Empty).Trim().Trim('/');
            string b = (second ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrEmpty(a))
                return b;
            if (string.IsNullOrEmpty(b))
                return a;
            return a + "/" + b;
        }

        private void LoadPathTabsFromSettings()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _pathTabs = settings.FilePathTabs != null && settings.FilePathTabs.Count > 0
                ? new List<PathTabSessionModel>(settings.FilePathTabs)
                : new List<PathTabSessionModel> { new PathTabSessionModel { Path = string.Empty, Label = "/" } };
            _activePathTabIndex = Math.Clamp(settings.FilePathTabIndex, 0, _pathTabs.Count - 1);
            _currentSubPath = _pathTabs[_activePathTabIndex].Path ?? string.Empty;
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex), nameof(CurrentPathDisplay), nameof(CanNavigateUp));
        }

        private void PersistPathTabs()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.FilePathTabs = _pathTabs.Select(t => new PathTabSessionModel
            {
                Path = t.Path ?? string.Empty,
                Label = t.Label ?? string.Empty,
            }).ToList();
            settings.FilePathTabIndex = _activePathTabIndex;
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        private void SaveCurrentPathToActiveTab()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                return;
            _pathTabs[_activePathTabIndex].Path = _currentSubPath ?? string.Empty;
            _pathTabs[_activePathTabIndex].Label = MakePathLabel(_currentSubPath);
            OnPropertyChanged(nameof(PathTabs));
        }

        private async Task ApplyActivePathTabAsync()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                _activePathTabIndex = 0;
            _currentSubPath = _pathTabs[_activePathTabIndex].Path ?? string.Empty;
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex), nameof(CurrentPathDisplay), nameof(CanNavigateUp));
            await RefreshAsync();
        }

        private static string MakePathLabel(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";
            string trimmed = path.Trim('/').Trim();
            int slash = trimmed.LastIndexOf('/');
            return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
        }
    }

    /// <summary>
    /// Item view model for a remote WebDAV file or folder.
    /// </summary>
    public class WebdavFileItemViewModel
    {
        public WebdavFileItemViewModel(WebdavFileItemModel model)
        {
            Model = model;
        }

        public WebdavFileItemModel Model { get; }

        public string Name => Model.Name;

        public bool IsDirectory => Model.IsDirectory;

        public string MetaText
        {
            get
            {
                string datePart = Model.LastModifiedUtc.HasValue
                    ? Model.LastModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : string.Empty;

                if (Model.IsDirectory)
                    return string.IsNullOrEmpty(datePart) ? "Folder" : "Folder · " + datePart;

                string sizePart = FormatSize(Model.SizeBytes);
                return string.IsNullOrEmpty(datePart) ? sizePart : sizePart + " · " + datePart;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / 1024.0);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB", bytes / (1024.0 * 1024.0));
        }
    }
}
