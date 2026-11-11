// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Crypto;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the File tab (file exchange over WebDAV / OAuth clouds).
    /// </summary>
    public class FileViewModel : ViewModelBase
    {
        private const string OAuthPendingFileName = "oauth_pending.json";
        private const string OAuthResponseFileName = "oauth_response.json";

        private readonly IWebdavFileService _webdavService;
        private readonly ISettingsService _settingsService;
        private readonly IFilePickerService _filePickerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly IImageEditorService _imageEditorService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;
        private readonly INativeBrowserService _nativeBrowserService;
        private readonly IMessengerService _messengerService;
        private readonly ICryptoRandomService _randomService;
        private readonly HashSet<string> _selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _handledOAuthRedirect;

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
        private List<CloudFileLocationModel> _editCloudLocations = new List<CloudFileLocationModel>();
        private string _activeLocationId = CloudFileLocationModel.PrimaryLocationId;
        private bool _useCloudClient;
        private string _effectiveCloudStorageId;
        private CloudStorageCredentials _cloudCredentials;

        public FileViewModel(
            IWebdavFileService webdavService,
            ISettingsService settingsService,
            IFilePickerService filePickerService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            IImageEditorService imageEditorService = null,
            ICloudStorageClientFactory cloudStorageClientFactory = null,
            INativeBrowserService nativeBrowserService = null,
            IMessengerService messengerService = null,
            ICryptoRandomService randomService = null)
        {
            _webdavService = webdavService;
            _settingsService = settingsService;
            _filePickerService = filePickerService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _imageEditorService = imageEditorService;
            _cloudStorageClientFactory = cloudStorageClientFactory;
            _nativeBrowserService = nativeBrowserService;
            _messengerService = messengerService;
            _randomService = randomService;

            _messengerService?.Register<SilentNotes.FileCloudOAuthRedirectMessage>(this, (_, message) =>
            {
                _ = FinishCloudOAuthAsync(message.RedirectUrl);
            });
            if (SilentNotes.FileOAuthSession.Peek() != null)
                _ = FinishCloudOAuthAsync(SilentNotes.FileOAuthSession.Peek());

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

        /// <summary>Raised when the UI should re-render (e.g. after OAuth redirect).</summary>
        public event Action StateChanged;

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
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

        /// <summary>Gets the number of selected items.</summary>
        public int SelectedCount => _selectedNames.Count;

        /// <summary>Gets whether any items are selected.</summary>
        public bool HasSelection => _selectedNames.Count > 0;

        /// <summary>Gets a value indicating whether the active storage location is configured.</summary>
        public bool IsConfigured
        {
            get
            {
                if (_useCloudClient)
                    return _cloudCredentials != null;
                if (!string.IsNullOrWhiteSpace(EffectiveUrl))
                    return true;
                CloudFileLocationModel location = FindLocationById(_activeLocationId);
                return location != null && location.IsMegaStub;
            }
        }

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

        /// <summary>Gets extra cloud locations configured in settings (excludes the primary WebDAV).</summary>
        public IReadOnlyList<CloudFileLocationModel> ExtraCloudLocations =>
            _settingsService.LoadSettingsOrDefault().CloudFileLocations
            ?? new List<CloudFileLocationModel>();

        /// <summary>Gets editable extra cloud locations while the settings overlay is open.</summary>
        public IList<CloudFileLocationModel> EditCloudLocations => _editCloudLocations;

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
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _editCloudLocations = settings.CloudFileLocations != null
                ? settings.CloudFileLocations.Select(CloneCloudLocation).ToList()
                : new List<CloudFileLocationModel>();
            IsSettingsOpen = true;
            OnPropertyChanged(nameof(EditCloudLocations), nameof(ExtraCloudLocations));
        }

        public async Task SaveSettingsAsync()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.FileWebdavUrl = (EditUrl ?? string.Empty).Trim();
            settings.FileWebdavFolder = (EditFolder ?? string.Empty).Trim().Trim('/');
            settings.FileWebdavUsername = (EditUsername ?? string.Empty).Trim();
            settings.FileWebdavPassword = EditPassword ?? string.Empty;
            settings.CloudFileLocations = _editCloudLocations
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.DisplayName))
                .Select(CloneCloudLocation)
                .ToList();
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
                if (_activeLocationId != CloudFileLocationModel.PrimaryLocationId)
                {
                    CloudFileLocationModel location = FindLocationById(_activeLocationId);
                    if (location != null && location.IsMegaStub)
                    {
                        LastError = _languageService["file_cloud_mega_coming_soon"];
                        _items.Clear();
                        return;
                    }
                }

                if (_useCloudClient)
                {
                    ICloudStorageClient client = _cloudStorageClientFactory?.GetByKey(_effectiveCloudStorageId);
                    CloudStorageCredentials credentials = BuildCloudCredentialsForList();
                    if (client == null || credentials == null)
                    {
                        LastError = _languageService["file_cloud_oauth_required"];
                        _items.Clear();
                        return;
                    }

                    List<string> names = await client.ListFileNamesAsync(credentials);
                    _items = names
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Select(n => new WebdavFileItemViewModel(new WebdavFileItemModel
                        {
                            Name = n,
                            IsDirectory = false,
                            SizeBytes = 0,
                        }))
                        .ToList();
                }
                else
                {
                    string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
                    IReadOnlyList<WebdavFileItemModel> list = await _webdavService.ListDirectoryAsync(
                        EffectiveUrl, remoteFolder, EffectiveUsername, EffectivePassword);
                    _items = list.Select(m => new WebdavFileItemViewModel(m)).ToList();
                }
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

                await UploadBytesAsync(fileName, content);

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

            if (_useCloudClient)
            {
                _feedbackService?.ShowToast(_languageService["file_cloud_mkdir_unsupported"], FeedbackSeverity.Warning);
                return;
            }

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
                byte[] content = Encoding.UTF8.GetBytes(string.Empty);
                await UploadBytesAsync(name, content);
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
                LocationId = _activeLocationId ?? CloudFileLocationModel.PrimaryLocationId,
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
                byte[] bytes = await DownloadFileBytesAsync(item.Name);

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

        /// <summary>
        /// Downloads a remote file and returns text suitable for an Abro AI prompt.
        /// Binary files get a short placeholder instead of raw bytes.
        /// </summary>
        public async Task<string> ReadFileTextForAiAsync(WebdavFileItemViewModel item)
        {
            if (item == null || item.IsDirectory)
                return null;

            byte[] bytes = await DownloadFileBytesAsync(item.Name);

            if (AbroAiAskText.TryDecodeText(bytes, out string text))
                return text;

            return _languageService.LoadTextFmt("ask_ai_binary_file", bytes?.Length ?? 0);
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
                byte[] bytes = await DownloadFileBytesAsync(item.Name);
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

        public bool IsSelected(WebdavFileItemViewModel item)
        {
            return item != null && _selectedNames.Contains(item.Name);
        }

        public void ToggleSelection(WebdavFileItemViewModel item)
        {
            if (item == null || item.IsDirectory)
                return;
            if (!_selectedNames.Add(item.Name))
                _selectedNames.Remove(item.Name);
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
        }

        public void ClearSelection()
        {
            if (_selectedNames.Count == 0)
                return;
            _selectedNames.Clear();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
        }

        public async Task ZipSelectedAsync()
        {
            List<WebdavFileItemViewModel> selected = GetSelectedFiles();
            if (selected.Count == 0 || IsBusy)
                return;

            IsBusy = true;
            LastError = null;
            try
            {
                var entries = new List<CompressUtils.CompressEntry>();
                foreach (WebdavFileItemViewModel item in selected)
                {
                    byte[] bytes = await DownloadFileBytesAsync(item.Name);
                    entries.Add(new CompressUtils.CompressEntry { Name = item.Name, Data = bytes ?? Array.Empty<byte>() });
                }

                byte[] zip = CompressUtils.CreateZipArchive(entries);
                string zipName = string.Format(CultureInfo.InvariantCulture, "archive_{0:yyyyMMdd_HHmmss}.zip", DateTime.Now);
                await UploadBytesAsync(zipName, zip);
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_zipped", zipName));
                ClearSelection();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_zip_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UnzipSelectedAsync()
        {
            List<WebdavFileItemViewModel> selected = GetSelectedFiles()
                .Where(i => i.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selected.Count == 0 || IsBusy)
            {
                _feedbackService?.ShowToast(_languageService["file_unzip_need_zip"], FeedbackSeverity.Warning);
                return;
            }

            IsBusy = true;
            LastError = null;
            try
            {
                int count = 0;
                foreach (WebdavFileItemViewModel item in selected)
                {
                    byte[] zipBytes = await DownloadFileBytesAsync(item.Name);
                    foreach (CompressUtils.CompressEntry entry in CompressUtils.OpenZipArchive(zipBytes))
                    {
                        string name = Path.GetFileName(entry.Name.Replace('\\', '/'));
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        await UploadBytesAsync(name, entry.Data ?? Array.Empty<byte>());
                        count++;
                    }
                }

                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_unzipped", count));
                ClearSelection();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_unzip_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task SplitSelectedAsync(int chunkSizeMb)
        {
            List<WebdavFileItemViewModel> selected = GetSelectedFiles();
            if (selected.Count != 1 || IsBusy)
            {
                _feedbackService?.ShowToast(_languageService["file_split_need_one"], FeedbackSeverity.Warning);
                return;
            }

            int chunkBytes = Math.Max(1, chunkSizeMb) * 1024 * 1024;
            IsBusy = true;
            LastError = null;
            try
            {
                WebdavFileItemViewModel item = selected[0];
                byte[] bytes = await DownloadFileBytesAsync(item.Name);
                List<CompressUtils.CompressEntry> parts = CompressUtils.SplitFile(bytes, item.Name, chunkBytes);
                foreach (CompressUtils.CompressEntry part in parts)
                    await UploadBytesAsync(part.Name, part.Data ?? Array.Empty<byte>());

                _feedbackService?.ShowToast(_languageService.LoadTextFmt("file_split_ok", parts.Count));
                ClearSelection();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_split_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task CropImageAsync(WebdavFileItemViewModel item)
        {
            if (item == null || item.IsDirectory || _imageEditorService == null || !_imageEditorService.IsSupported || IsBusy)
            {
                _feedbackService?.ShowToast(_languageService["file_crop_unavailable"], FeedbackSeverity.Warning);
                return;
            }

            if (!IsImageName(item.Name))
            {
                _feedbackService?.ShowToast(_languageService["file_crop_need_image"], FeedbackSeverity.Warning);
                return;
            }

            IsBusy = true;
            LastError = null;
            try
            {
                byte[] bytes = await DownloadFileBytesAsync(item.Name);
                string savedPath = await _webdavService.SaveDownloadedFileAsync(item.Name, bytes);
                if (string.IsNullOrEmpty(savedPath) || !await _imageEditorService.OpenEditorAsync(savedPath))
                    _feedbackService?.ShowToast(_languageService["file_crop_failed"], FeedbackSeverity.Warning);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _feedbackService?.ShowToast(_languageService["file_crop_failed"] + " — " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private List<WebdavFileItemViewModel> GetSelectedFiles()
        {
            return _items.Where(i => !i.IsDirectory && _selectedNames.Contains(i.Name)).ToList();
        }

        private static bool IsImageName(string name)
        {
            string ext = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".heic" or ".avif";
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
                await DeleteRemoteItemAsync(item);
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

        /// <summary>Opens a configured storage location as a new path tab at its root folder.</summary>
        public async Task OpenCloudLocationAsNewTabAsync(string locationId)
        {
            SaveCurrentPathToActiveTab();
            string label = ResolveLocationLabel(locationId);
            _pathTabs.Add(new PathTabSessionModel
            {
                LocationId = locationId ?? CloudFileLocationModel.PrimaryLocationId,
                Path = string.Empty,
                Label = label,
            });
            _activePathTabIndex = _pathTabs.Count - 1;
            PersistPathTabs();
            await ApplyActivePathTabAsync();
        }

        /// <summary>Adds a blank extra cloud location row in the settings overlay.</summary>
        public void AddExtraCloudLocation()
        {
            _editCloudLocations.Add(new CloudFileLocationModel
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = _languageService["file_cloud_new_location"],
                ProviderId = CloudFileLocationModel.ProviderWebdav,
            });
            OnPropertyChanged(nameof(EditCloudLocations));
        }

        /// <summary>Removes an extra cloud location row from the settings overlay.</summary>
        public void RemoveExtraCloudLocation(CloudFileLocationModel location)
        {
            if (location == null)
                return;
            _editCloudLocations.Remove(location);
            OnPropertyChanged(nameof(EditCloudLocations));
        }

        /// <summary>True when this location already has usable OAuth tokens.</summary>
        public bool IsCloudLocationConnected(CloudFileLocationModel location)
        {
            if (location == null || !IsOAuthProvider(location.ProviderId))
                return false;
            if (HasUsableToken(location.CloudCredentials?.Token))
                return true;
            if (!location.UseRepositoryCredentials)
                return false;
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            return settings.Credentials != null
                && string.Equals(settings.Credentials.CloudStorageId, MapProviderToCloudStorageId(location.ProviderId), StringComparison.OrdinalIgnoreCase)
                && HasUsableToken(settings.Credentials.Token);
        }

        /// <summary>Starts browser OAuth for Dropbox / Google Drive / OneDrive.</summary>
        public async Task ConnectCloudLocationAsync(CloudFileLocationModel location)
        {
            if (location == null || !IsOAuthProvider(location.ProviderId))
                return;
            if (_nativeBrowserService == null || _randomService == null || _cloudStorageClientFactory == null)
            {
                _feedbackService?.ShowToast(_languageService["file_cloud_oauth_unavailable"], FeedbackSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(location.Id))
                location.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(location.DisplayName))
                location.DisplayName = _languageService["file_cloud_new_location"];

            string cloudStorageId = MapProviderToCloudStorageId(location.ProviderId);
            ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(cloudStorageId);
            if (client is not IOAuth2CloudStorageClient oauthClient)
            {
                _feedbackService?.ShowToast(_languageService["file_cloud_oauth_unavailable"], FeedbackSeverity.Warning);
                return;
            }

            string state = SilentNotes.FileOAuthSession.StatePrefix + CryptoUtils.GenerateRandomBase62String(16, _randomService);
            string verifier = CryptoUtils.GenerateRandomBase62String(64, _randomService);
            WriteOAuthPending(new FileOAuthPending
            {
                LocationId = location.Id,
                CloudStorageId = cloudStorageId,
                ProviderId = location.ProviderId,
                DisplayName = location.DisplayName,
                RemoteFolder = location.RemoteFolder,
                State = state,
                CodeVerifier = verifier,
            });

            // Keep the row in settings so a successful redirect can find it even if the overlay closed.
            UpsertEditLocationIntoSettings(location);

            string url = oauthClient.BuildAuthorizationRequestUrl(state, verifier);
            _nativeBrowserService.OpenWebsiteInApp(url);
            _feedbackService?.ShowToast(_languageService["file_cloud_oauth_waiting"]);
            await Task.CompletedTask;
        }

        /// <summary>Clears OAuth tokens for a File-tab cloud location.</summary>
        public void DisconnectCloudLocation(CloudFileLocationModel location)
        {
            if (location == null)
                return;

            location.CloudCredentials = null;
            location.UseRepositoryCredentials = false;
            UpsertEditLocationIntoSettings(location);
            TryDeleteLocationOAuthFiles(location.Id);
            OnPropertyChanged(nameof(EditCloudLocations));
            _feedbackService?.ShowToast(_languageService["file_cloud_disconnected"]);
        }

        /// <summary>Completes OAuth after the custom-scheme redirect returns to the app.</summary>
        public async Task FinishCloudOAuthAsync(string redirectUrl)
        {
            if (string.IsNullOrEmpty(redirectUrl) || redirectUrl == _handledOAuthRedirect)
                return;

            _handledOAuthRedirect = redirectUrl;
            SilentNotes.FileOAuthSession.Clear(redirectUrl);

            FileOAuthPending pending = ReadOAuthPending();
            if (pending == null || string.IsNullOrEmpty(pending.State) || string.IsNullOrEmpty(pending.CodeVerifier))
            {
                _feedbackService?.ShowToast(_languageService["file_cloud_oauth_failed"], FeedbackSeverity.Warning);
                return;
            }

            IsBusy = true;
            try
            {
                ICloudStorageClient client = _cloudStorageClientFactory?.GetByKey(pending.CloudStorageId);
                if (client is not IOAuth2CloudStorageClient oauthClient)
                    throw new InvalidOperationException(_languageService["file_cloud_oauth_unavailable"]);

                CloudStorageToken token = await oauthClient.FetchTokenAsync(redirectUrl, pending.State, pending.CodeVerifier);
                ClearOAuthPending();
                if (token == null)
                {
                    _feedbackService?.ShowToast(_languageService["file_cloud_oauth_failed"], FeedbackSeverity.Warning);
                    return;
                }

                var credentials = new SerializeableCloudStorageCredentials
                {
                    CloudStorageId = pending.CloudStorageId,
                    Token = token,
                };

                CloudFileLocationModel editLocation = _editCloudLocations.FirstOrDefault(l =>
                    l != null && string.Equals(l.Id, pending.LocationId, StringComparison.Ordinal));
                if (editLocation == null)
                {
                    editLocation = new CloudFileLocationModel
                    {
                        Id = pending.LocationId,
                        DisplayName = string.IsNullOrWhiteSpace(pending.DisplayName)
                            ? _languageService["file_cloud_new_location"]
                            : pending.DisplayName,
                        ProviderId = pending.ProviderId,
                        RemoteFolder = pending.RemoteFolder,
                    };
                    _editCloudLocations.Add(editLocation);
                }

                editLocation.CloudCredentials = credentials;
                editLocation.UseRepositoryCredentials = false;
                UpsertEditLocationIntoSettings(editLocation);
                WriteOAuthResponseToDisk(editLocation.Id, pending.CloudStorageId, token);

                OnPropertyChanged(nameof(EditCloudLocations), nameof(ExtraCloudLocations));
                _feedbackService?.ShowToast(_languageService["file_cloud_oauth_ok"]);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService["file_cloud_oauth_failed"] + ": " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsConfigured));
                NotifyChanged();
            }
        }

        private async Task UploadBytesAsync(string fileName, byte[] content)
        {
            if (_useCloudClient)
            {
                ICloudStorageClient client = _cloudStorageClientFactory?.GetByKey(_effectiveCloudStorageId);
                CloudStorageCredentials credentials = BuildCloudCredentialsForList();
                if (client == null || credentials == null)
                    throw new InvalidOperationException(_languageService["file_cloud_oauth_required"]);
                await client.UploadFileAsync(fileName, content ?? Array.Empty<byte>(), credentials);
                return;
            }

            string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
            await _webdavService.UploadFileAsync(
                EffectiveUrl, remoteFolder, fileName, content, EffectiveUsername, EffectivePassword);
        }

        private async Task DeleteRemoteItemAsync(WebdavFileItemViewModel item)
        {
            if (_useCloudClient)
            {
                if (item.IsDirectory)
                    throw new InvalidOperationException(_languageService["file_cloud_mkdir_unsupported"]);

                ICloudStorageClient client = _cloudStorageClientFactory?.GetByKey(_effectiveCloudStorageId);
                CloudStorageCredentials credentials = BuildCloudCredentialsForList();
                if (client == null || credentials == null)
                    throw new InvalidOperationException(_languageService["file_cloud_oauth_required"]);
                await client.DeleteFileAsync(item.Name, credentials);
                return;
            }

            string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
            await _webdavService.DeleteItemAsync(
                EffectiveUrl, remoteFolder, item.Name, item.IsDirectory, EffectiveUsername, EffectivePassword);
        }

        private void UpsertEditLocationIntoSettings(CloudFileLocationModel location)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.Id))
                return;

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (settings.CloudFileLocations == null)
                settings.CloudFileLocations = new List<CloudFileLocationModel>();

            CloudFileLocationModel existing = settings.CloudFileLocations.FirstOrDefault(l =>
                l != null && string.Equals(l.Id, location.Id, StringComparison.Ordinal));
            CloudFileLocationModel clone = CloneCloudLocation(location);
            if (existing == null)
                settings.CloudFileLocations.Add(clone);
            else
            {
                int index = settings.CloudFileLocations.IndexOf(existing);
                settings.CloudFileLocations[index] = clone;
            }

            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        private static bool HasUsableToken(CloudStorageToken token)
        {
            return token != null
                && (!string.IsNullOrEmpty(token.AccessToken) || !string.IsNullOrEmpty(token.RefreshToken));
        }

        private static string EnsureFileWorkspace()
        {
            AnotePaths.EnsureDirectories();
            string root = AnotePaths.FileDir;
            Directory.CreateDirectory(root);
            return root;
        }

        private static string LocationDirectory(string locationId)
        {
            string safeId = string.IsNullOrWhiteSpace(locationId) ? "unknown" : locationId.Trim();
            string dir = Path.Combine(EnsureFileWorkspace(), safeId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteOAuthPending(FileOAuthPending pending)
        {
            string path = Path.Combine(EnsureFileWorkspace(), OAuthPendingFileName);
            string json = JsonSerializer.Serialize(pending);
            System.IO.File.WriteAllText(path, json, Encoding.UTF8);
        }

        private static FileOAuthPending ReadOAuthPending()
        {
            string path = Path.Combine(EnsureFileWorkspace(), OAuthPendingFileName);
            if (!System.IO.File.Exists(path))
                return null;
            try
            {
                return JsonSerializer.Deserialize<FileOAuthPending>(System.IO.File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static void ClearOAuthPending()
        {
            try
            {
                string path = Path.Combine(EnsureFileWorkspace(), OAuthPendingFileName);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
            }
        }

        private static void WriteOAuthResponseToDisk(string locationId, string cloudStorageId, CloudStorageToken token)
        {
            if (token == null)
                return;

            var payload = new
            {
                cloudStorageId,
                connectedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                accessToken = token.AccessToken,
                refreshToken = token.RefreshToken,
                expiryDate = token.ExpiryDate?.ToString("o", CultureInfo.InvariantCulture),
            };
            string path = Path.Combine(LocationDirectory(locationId), OAuthResponseFileName);
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }

        private static void TryDeleteLocationOAuthFiles(string locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId))
                return;
            try
            {
                string dir = Path.Combine(AnotePaths.FileDir, locationId.Trim());
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
            }
        }

        private sealed class FileOAuthPending
        {
            public string LocationId { get; set; }
            public string CloudStorageId { get; set; }
            public string ProviderId { get; set; }
            public string DisplayName { get; set; }
            public string RemoteFolder { get; set; }
            public string State { get; set; }
            public string CodeVerifier { get; set; }
        }

        private void LoadCredentialsFromSettings()
        {
            _useCloudClient = false;
            _effectiveCloudStorageId = null;
            _cloudCredentials = null;

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            string locationId = _activeLocationId ?? CloudFileLocationModel.PrimaryLocationId;

            if (!string.IsNullOrEmpty(locationId) && !string.Equals(locationId, CloudFileLocationModel.PrimaryLocationId, StringComparison.Ordinal))
            {
                CloudFileLocationModel location = FindLocationById(locationId);
                if (location == null)
                {
                    ClearEffectiveCredentials();
                    return;
                }

                ApplyCloudFileLocation(settings, location);
                return;
            }

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
                ClearEffectiveCredentials();
            }
        }

        private void ApplyCloudFileLocation(SettingsModel settings, CloudFileLocationModel location)
        {
            if (location.IsMegaStub)
            {
                ClearEffectiveCredentials();
                return;
            }

            if (IsOAuthProvider(location.ProviderId))
            {
                CloudStorageCredentials credentials = ResolveOAuthCredentials(settings, location);
                if (credentials == null || _cloudStorageClientFactory == null)
                {
                    ClearEffectiveCredentials();
                    return;
                }

                _useCloudClient = true;
                _effectiveCloudStorageId = MapProviderToCloudStorageId(location.ProviderId);
                _cloudCredentials = credentials;
                EffectiveUrl = string.Empty;
                EffectiveFolder = location.RemoteFolder ?? string.Empty;
                EffectiveUsername = string.Empty;
                EffectivePassword = string.Empty;
                return;
            }

            EffectiveUrl = location.Url ?? string.Empty;
            EffectiveFolder = location.RemoteFolder ?? string.Empty;
            EffectiveUsername = location.Username ?? string.Empty;
            EffectivePassword = location.Password ?? string.Empty;
        }

        private static bool IsOAuthProvider(string providerId)
        {
            return string.Equals(providerId, CloudFileLocationModel.ProviderDropbox, StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerId, CloudFileLocationModel.ProviderGoogleDrive, StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerId, CloudFileLocationModel.ProviderOneDrive, StringComparison.OrdinalIgnoreCase);
        }

        private static string MapProviderToCloudStorageId(string providerId)
        {
            if (string.Equals(providerId, CloudFileLocationModel.ProviderDropbox, StringComparison.OrdinalIgnoreCase))
                return CloudStorageClientFactory.CloudStorageIdDropbox;
            if (string.Equals(providerId, CloudFileLocationModel.ProviderGoogleDrive, StringComparison.OrdinalIgnoreCase))
                return CloudStorageClientFactory.CloudStorageIdGoogleDrive;
            if (string.Equals(providerId, CloudFileLocationModel.ProviderOneDrive, StringComparison.OrdinalIgnoreCase))
                return CloudStorageClientFactory.CloudStorageIdOneDrive;
            return providerId;
        }

        private CloudStorageCredentials ResolveOAuthCredentials(SettingsModel settings, CloudFileLocationModel location)
        {
            SerializeableCloudStorageCredentials serialized = null;
            if (location.CloudCredentials != null)
                serialized = location.CloudCredentials;
            else if (location.UseRepositoryCredentials && settings.Credentials != null
                && string.Equals(settings.Credentials.CloudStorageId, MapProviderToCloudStorageId(location.ProviderId), StringComparison.OrdinalIgnoreCase))
                serialized = settings.Credentials;

            if (serialized == null)
                return null;

            return new CloudStorageCredentials
            {
                CloudStorageId = serialized.CloudStorageId,
                Token = serialized.Token,
                Username = serialized.Username,
                UnprotectedPassword = serialized.UnprotectedPassword,
                Url = CombinePaths(location.RemoteFolder, _currentSubPath),
                Secure = serialized.Secure,
                AcceptInvalidCertificate = serialized.AcceptInvalidCertificate,
            };
        }

        private CloudStorageCredentials BuildCloudCredentialsForList()
        {
            if (_cloudCredentials == null)
                return null;

            return new CloudStorageCredentials
            {
                CloudStorageId = _cloudCredentials.CloudStorageId,
                Token = _cloudCredentials.Token,
                Username = _cloudCredentials.Username,
                UnprotectedPassword = _cloudCredentials.UnprotectedPassword,
                Url = CombinePaths(EffectiveFolder, _currentSubPath),
                Secure = _cloudCredentials.Secure,
                AcceptInvalidCertificate = _cloudCredentials.AcceptInvalidCertificate,
            };
        }

        private async Task<byte[]> DownloadFileBytesAsync(string fileName)
        {
            if (_useCloudClient)
            {
                ICloudStorageClient client = _cloudStorageClientFactory?.GetByKey(_effectiveCloudStorageId);
                CloudStorageCredentials credentials = BuildCloudCredentialsForList();
                if (client == null || credentials == null)
                    throw new InvalidOperationException(_languageService["file_cloud_oauth_required"]);
                return await client.DownloadFileAsync(fileName, credentials);
            }

            string remoteFolder = CombinePaths(EffectiveFolder, _currentSubPath);
            return await _webdavService.DownloadFileAsync(
                EffectiveUrl, remoteFolder, fileName, EffectiveUsername, EffectivePassword);
        }

        private void ClearEffectiveCredentials()
        {
            EffectiveUrl = string.Empty;
            EffectiveFolder = string.Empty;
            EffectiveUsername = string.Empty;
            EffectivePassword = string.Empty;
        }

        private CloudFileLocationModel FindLocationById(string locationId)
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (settings.CloudFileLocations == null)
                return null;
            return settings.CloudFileLocations.FirstOrDefault(l =>
                l != null && string.Equals(l.Id, locationId, StringComparison.Ordinal));
        }

        private string ResolveLocationLabel(string locationId)
        {
            if (string.IsNullOrEmpty(locationId) || string.Equals(locationId, CloudFileLocationModel.PrimaryLocationId, StringComparison.Ordinal))
                return _languageService["file_cloud_location_primary"];

            CloudFileLocationModel location = FindLocationById(locationId);
            if (location != null && !string.IsNullOrWhiteSpace(location.DisplayName))
                return location.DisplayName;
            return locationId;
        }

        private static CloudFileLocationModel CloneCloudLocation(CloudFileLocationModel source)
        {
            if (source == null)
                return null;

            var clone = new CloudFileLocationModel
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
                DisplayName = source.DisplayName,
                ProviderId = source.ProviderId,
                Url = source.Url,
                RemoteFolder = source.RemoteFolder,
                Username = source.Username,
                Password = source.Password,
                UseRepositoryCredentials = source.UseRepositoryCredentials,
            };
            if (source.CloudCredentials != null)
            {
                clone.CloudCredentials = new SerializeableCloudStorageCredentials
                {
                    CloudStorageId = source.CloudCredentials.CloudStorageId,
                    Token = source.CloudCredentials.Token,
                    Username = source.CloudCredentials.Username,
                    UnprotectedPassword = source.CloudCredentials.UnprotectedPassword,
                    Url = source.CloudCredentials.Url,
                    Secure = source.CloudCredentials.Secure,
                    AcceptInvalidCertificate = source.CloudCredentials.AcceptInvalidCertificate,
                };
            }

            return clone;
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
            _activeLocationId = _pathTabs[_activePathTabIndex].LocationId ?? CloudFileLocationModel.PrimaryLocationId;
            LoadCredentialsFromSettings();
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex), nameof(CurrentPathDisplay), nameof(CanNavigateUp), nameof(IsConfigured));
        }

        private void PersistPathTabs()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.FilePathTabs = _pathTabs.Select(t => new PathTabSessionModel
            {
                Path = t.Path ?? string.Empty,
                Label = t.Label ?? string.Empty,
                LocationId = t.LocationId ?? string.Empty,
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
            _activeLocationId = _pathTabs[_activePathTabIndex].LocationId ?? CloudFileLocationModel.PrimaryLocationId;
            LoadCredentialsFromSettings();
            OnPropertyChanged(nameof(PathTabs), nameof(ActivePathTabIndex), nameof(CurrentPathDisplay), nameof(CanNavigateUp), nameof(IsConfigured));
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
