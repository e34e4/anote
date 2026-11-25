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
    /// View model for the Reader tab: browse folders and open text / Office / PDF / ebook files.
    /// </summary>
    public class ReaderViewModel : ViewModelBase
    {
        private readonly IDocumentReaderService _readerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly ISettingsService _settingsService;

        private string _filter;
        private string _kindFilter = "all"; // all | text | office | pdf | ebook
        private List<DocumentItemViewModel> _items = new List<DocumentItemViewModel>();
        private bool _isBusy;
        private int _activePathTabIndex;
        private List<PathTabSessionModel> _pathTabs = new List<PathTabSessionModel>();
        private readonly HashSet<string> _selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FileBrowserSortMode _sortMode = FileBrowserSortMode.DateDesc;
        private bool _isRenameOpen;
        private string _renameTargetPath;
        private string _renameDraft;

        public ReaderViewModel(
            IDocumentReaderService readerService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            ISettingsService settingsService = null)
        {
            _readerService = readerService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _settingsService = settingsService;

            _readerService.StateChanged += OnReaderStateChanged;

            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            RefreshCommand = new RelayCommand(Reload);
            NavigateUpCommand = new RelayCommand(NavigateUp);
            EnterItemCommand = new AsyncRelayCommand<DocumentItemViewModel>(EnterOrOpenAsync);
            PickFileCommand = new AsyncRelayCommand(PickFileAsync);
            CloseDocumentCommand = new RelayCommand(CloseDocument);
            SetKindFilterCommand = new RelayCommand<string>(SetKindFilter);
            GoToRootCommand = new RelayCommand<string>(GoToRoot);
            PrevPdfPageCommand = new AsyncRelayCommand(PrevPdfPageAsync, () => CanGoPrevPdfPage);
            NextPdfPageCommand = new AsyncRelayCommand(NextPdfPageAsync, () => CanGoNextPdfPage);
            OpenExternallyCommand = new AsyncRelayCommand(OpenExternallyAsync);
            AddPathTabCommand = new RelayCommand(AddPathTab);
            ClosePathTabCommand = new RelayCommand<int>(ClosePathTab);
            SelectPathTabCommand = new RelayCommand<int>(SelectPathTab);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            SetSortModeCommand = new RelayCommand<string>(SetSortMode);
            StartRenameSelectedCommand = new RelayCommand(StartRenameSelected);
            CancelRenameCommand = new RelayCommand(CancelRename);
            ConfirmRenameCommand = new AsyncRelayCommand(ConfirmRenameAsync);

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
        public ICommand CloseDocumentCommand { get; }
        public ICommand SetKindFilterCommand { get; }
        public ICommand GoToRootCommand { get; }
        public ICommand PrevPdfPageCommand { get; }
        public ICommand NextPdfPageCommand { get; }
        public ICommand OpenExternallyCommand { get; }
        public ICommand AddPathTabCommand { get; }
        public ICommand ClosePathTabCommand { get; }
        public ICommand SelectPathTabCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand SetSortModeCommand { get; }
        public ICommand StartRenameSelectedCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand ConfirmRenameCommand { get; }

        public bool HasSelection => _selectedPaths.Count > 0;

        public int SelectedCount => _selectedPaths.Count;

        /// <summary>Gets path sessions for the Reader browser.</summary>
        public IReadOnlyList<PathTabSessionModel> PathTabs => _pathTabs;

        /// <summary>Gets the active path session index.</summary>
        public int ActivePathTabIndex => _activePathTabIndex;

        public bool IsSupported => _readerService.IsSupported;

        public bool NeedsPermission => _readerService.IsSupported && !_readerService.HasAllFilesPermission;

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string CurrentPathDisplay => _readerService.CurrentDirectoryDisplay;

        public bool CanNavigateUp => _readerService.CanNavigateUp;

        public IReadOnlyList<(string Key, string Label, string Path)> StorageRoots => _readerService.GetStorageRoots();

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    OnPropertyChanged(nameof(FilteredItems), nameof(HasNoMatches));
            }
        }

        public string KindFilter
        {
            get => _kindFilter;
            set
            {
                if (SetProperty(ref _kindFilter, value))
                {
                    OnPropertyChanged(
                        nameof(FilteredItems),
                        nameof(HasNoMatches),
                        nameof(IsKindAll),
                        nameof(IsKindText),
                        nameof(IsKindOffice),
                        nameof(IsKindPdf),
                        nameof(IsKindEbook));
                }
            }
        }

        public bool IsKindAll => string.Equals(_kindFilter, "all", StringComparison.OrdinalIgnoreCase);
        public bool IsKindText => string.Equals(_kindFilter, "text", StringComparison.OrdinalIgnoreCase);
        public bool IsKindOffice => string.Equals(_kindFilter, "office", StringComparison.OrdinalIgnoreCase);
        public bool IsKindPdf => string.Equals(_kindFilter, "pdf", StringComparison.OrdinalIgnoreCase);
        public bool IsKindEbook => string.Equals(_kindFilter, "ebook", StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets or sets how items in the current directory are sorted.</summary>
        public FileBrowserSortMode SortMode
        {
            get => _sortMode;
            set
            {
                if (SetProperty(ref _sortMode, value))
                {
                    PersistSortMode();
                    OnPropertyChanged(
                        nameof(FilteredItems),
                        nameof(HasNoMatches),
                        nameof(CountText),
                        nameof(IsSortName),
                        nameof(IsSortDate),
                        nameof(IsSortSize));
                }
            }
        }

        public bool IsSortName => _sortMode == FileBrowserSortMode.NameAsc;
        public bool IsSortDate => _sortMode == FileBrowserSortMode.DateDesc;
        public bool IsSortSize => _sortMode == FileBrowserSortMode.SizeDesc;

        public bool IsRenameOpen
        {
            get => _isRenameOpen;
            private set => SetProperty(ref _isRenameOpen, value);
        }

        public string RenameDraft
        {
            get => _renameDraft;
            set => SetProperty(ref _renameDraft, value);
        }

        /// <summary>True when exactly one non-folder item is selected.</summary>
        public bool CanRenameSelection => TryGetSingleSelectedFile(out _);

        public IReadOnlyList<DocumentItemViewModel> FilteredItems
        {
            get
            {
                IEnumerable<DocumentItemViewModel> query = _items;

                if (!IsKindAll)
                {
                    query = query.Where(i =>
                        i.Kind == DocumentKind.Folder ||
                        (IsKindText && i.Kind == DocumentKind.Text) ||
                        (IsKindOffice && i.Kind == DocumentKind.Office) ||
                        (IsKindPdf && (i.Kind == DocumentKind.Pdf || i.Kind == DocumentKind.Djvu)) ||
                        (IsKindEbook && i.Kind == DocumentKind.Ebook));
                }

                string filter = _filter?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(filter))
                    query = query.Where(i => i.Name.ToLowerInvariant().Contains(filter));

                return SortItems(query);
            }
        }

        public bool IsEmpty => _items.Count == 0;

        public bool HasNoMatches => !IsEmpty && FilteredItems.Count == 0;

        public string CountText => _languageService.LoadTextFmt("reader_count", FilteredItems.Count);

        public bool HasOpenDocument => _readerService.ActiveDocument != null;

        public DocumentContentModel ActiveDocument => _readerService.ActiveDocument;

        /// <summary>
        /// Extracts plain text from the open document (PDF pages via OCR), for AI actions.
        /// </summary>
        public Task<string> ExtractActiveTextAsync()
        {
            return _readerService.ExtractActiveTextAsync();
        }

        public string ActiveTitle => ActiveDocument?.Title ?? string.Empty;

        public bool IsPdfDocument =>
            ActiveDocument != null && ActiveDocument.RenderMode == DocumentRenderMode.PdfPages;

        public string PdfPageLabel
        {
            get
            {
                if (!IsPdfDocument || ActiveDocument == null || ActiveDocument.PdfPageCount <= 0)
                    return string.Empty;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} / {1}",
                    ActiveDocument.PdfPageIndex + 1,
                    ActiveDocument.PdfPageCount);
            }
        }

        public bool CanGoPrevPdfPage =>
            IsPdfDocument && ActiveDocument != null && ActiveDocument.PdfPageIndex > 0;

        public bool CanGoNextPdfPage =>
            IsPdfDocument &&
            ActiveDocument != null &&
            ActiveDocument.PdfPageIndex < ActiveDocument.PdfPageCount - 1;

        public async Task GrantPermissionAsync()
        {
            await _readerService.RequestAllFilesPermissionAsync();
            Reload();
            RaiseStateChanged();
        }

        public bool IsSelected(DocumentItemViewModel item)
            => item != null && !string.IsNullOrEmpty(item.Path) && _selectedPaths.Contains(item.Path);

        public void ToggleSelection(DocumentItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
                return;
            if (!_selectedPaths.Add(item.Path))
                _selectedPaths.Remove(item.Path);
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount), nameof(CanRenameSelection));
            RaiseStateChanged();
        }

        public void SelectAllVisible()
        {
            foreach (DocumentItemViewModel item in FilteredItems)
            {
                if (!string.IsNullOrEmpty(item.Path))
                    _selectedPaths.Add(item.Path);
            }
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount), nameof(CanRenameSelection));
            RaiseStateChanged();
        }

        public void ClearSelection()
        {
            if (_selectedPaths.Count == 0)
                return;
            _selectedPaths.Clear();
            CancelRename();
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount), nameof(CanRenameSelection));
            RaiseStateChanged();
        }

        public async Task DeleteSelectedAsync()
        {
            if (_selectedPaths.Count == 0)
                return;

            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService.LoadTextFmt("reader_delete_confirm", _selectedPaths.Count),
                _languageService["reader_delete"],
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
            foreach (DocumentItemViewModel item in targets)
            {
                bool isDir = item.Kind == DocumentKind.Folder;
                if (_readerService.TryDeletePath(item.Path, isDir))
                    deleted++;
            }

            ClearSelection();
            Reload();
            if (deleted > 0)
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("reader_deleted", deleted));
            else
                _feedbackService?.ShowToast(_languageService["reader_delete_failed"], FeedbackSeverity.Warning);
        }

        public void Reload()
        {
            try
            {
                IsBusy = true;
                _items = SortItems(_readerService.ListCurrentDirectory()
                    .Select(m => new DocumentItemViewModel(m)));
                OnPropertyChanged(
                    nameof(FilteredItems),
                    nameof(IsEmpty),
                    nameof(HasNoMatches),
                    nameof(CountText),
                    nameof(CurrentPathDisplay),
                    nameof(CanNavigateUp),
                    nameof(NeedsPermission),
                    nameof(StorageRoots),
                    nameof(HasOpenDocument),
                    nameof(ActiveDocument),
                    nameof(ActiveTitle));
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        public void HandleIntent(DocumentIntentRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UriOrPath))
                return;

            _ = OpenFromIntentAsync(request);
        }

        private async Task OpenFromIntentAsync(DocumentIntentRequest request)
        {
            try
            {
                IsBusy = true;
                RaiseStateChanged();
                await _readerService.OpenUriAsync(request.UriOrPath, request.MimeType, request.DisplayName);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(
                    _languageService.LoadTextFmt("reader_open_failed", ex.Message),
                    FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        public void NavigateUp()
        {
            if (HasOpenDocument)
            {
                CloseDocument();
                return;
            }

            _readerService.NavigateUp();
            SaveCurrentPathToActiveTab();
            PersistPathTabs();
            Reload();
        }

        private void GoToRoot(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (HasOpenDocument)
                CloseDocument();
            _readerService.GoToRoot(key);
            SaveCurrentPathToActiveTab();
            PersistPathTabs();
            Reload();
        }

        private void AddPathTab()
        {
            _pathTabs.Add(new PathTabSessionModel
            {
                Path = _readerService.CurrentDirectory ?? string.Empty,
                Label = MakePathLabel(_readerService.CurrentDirectory),
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
                        Path = _readerService.CurrentDirectory ?? string.Empty,
                        Label = MakePathLabel(_readerService.CurrentDirectory),
                    },
                };
                _activePathTabIndex = 0;
                return;
            }

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _pathTabs = settings.ReaderPathTabs != null && settings.ReaderPathTabs.Count > 0
                ? new List<PathTabSessionModel>(settings.ReaderPathTabs)
                : new List<PathTabSessionModel>
                {
                    new PathTabSessionModel
                    {
                        Path = _readerService.CurrentDirectory ?? string.Empty,
                        Label = MakePathLabel(_readerService.CurrentDirectory),
                    },
                };
            _activePathTabIndex = Math.Clamp(settings.ReaderPathTabIndex, 0, _pathTabs.Count - 1);
            _sortMode = FileBrowserSort.ParseOrDefault(settings.ReaderSortMode);
            string path = _pathTabs[_activePathTabIndex].Path;
            if (!string.IsNullOrEmpty(path))
                _readerService.EnterDirectory(path);
        }

        private void PersistPathTabs()
        {
            if (_settingsService == null)
                return;
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.ReaderPathTabs = _pathTabs.Select(t => new PathTabSessionModel
            {
                Path = t.Path ?? string.Empty,
                Label = t.Label ?? string.Empty,
            }).ToList();
            settings.ReaderPathTabIndex = _activePathTabIndex;
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        private void SaveCurrentPathToActiveTab()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                return;
            _pathTabs[_activePathTabIndex].Path = _readerService.CurrentDirectory ?? string.Empty;
            _pathTabs[_activePathTabIndex].Label = MakePathLabel(_readerService.CurrentDirectory);
            OnPropertyChanged(nameof(PathTabs));
        }

        private void ApplyActivePathTab()
        {
            if (_activePathTabIndex < 0 || _activePathTabIndex >= _pathTabs.Count)
                _activePathTabIndex = 0;
            if (HasOpenDocument)
                CloseDocument();
            string path = _pathTabs[_activePathTabIndex].Path;
            if (!string.IsNullOrEmpty(path))
                _readerService.EnterDirectory(path);
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

        private void SetSortMode(string modeKey)
        {
            if (string.IsNullOrEmpty(modeKey))
                return;
            if (string.Equals(modeKey, "name", StringComparison.OrdinalIgnoreCase))
                SortMode = FileBrowserSortMode.NameAsc;
            else if (string.Equals(modeKey, "size", StringComparison.OrdinalIgnoreCase))
                SortMode = FileBrowserSortMode.SizeDesc;
            else
                SortMode = FileBrowserSortMode.DateDesc;
            RaiseStateChanged();
        }

        private void StartRenameSelected()
        {
            if (!TryGetSingleSelectedFile(out DocumentItemViewModel item))
                return;
            _renameTargetPath = item.Path;
            RenameDraft = item.Name;
            IsRenameOpen = true;
            OnPropertyChanged(nameof(CanRenameSelection));
            RaiseStateChanged();
        }

        private void CancelRename()
        {
            _renameTargetPath = null;
            RenameDraft = null;
            IsRenameOpen = false;
            RaiseStateChanged();
        }

        private async Task ConfirmRenameAsync()
        {
            string newName = RenameDraft?.Trim();
            if (string.IsNullOrEmpty(_renameTargetPath) || string.IsNullOrWhiteSpace(newName))
            {
                CancelRename();
                return;
            }

            if (string.Equals(newName, Path.GetFileName(_renameTargetPath), StringComparison.Ordinal))
            {
                CancelRename();
                return;
            }

            bool ok = _readerService.TryRenamePath(_renameTargetPath, newName);
            if (!ok)
                _feedbackService?.ShowToast(_languageService["browse_rename_failed"], FeedbackSeverity.Warning);
            else
            {
                string dir = Path.GetDirectoryName(_renameTargetPath);
                string newPath = string.IsNullOrEmpty(dir) ? newName : Path.Combine(dir, newName);
                if (_selectedPaths.Remove(_renameTargetPath))
                    _selectedPaths.Add(newPath);
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("browse_renamed", newName));
            }

            CancelRename();
            Reload();
            OnPropertyChanged(nameof(HasSelection), nameof(SelectedCount), nameof(CanRenameSelection));
            await Task.CompletedTask;
        }

        private bool TryGetSingleSelectedFile(out DocumentItemViewModel item)
        {
            item = null;
            if (_selectedPaths.Count != 1)
                return false;
            string path = _selectedPaths.First();
            item = _items.FirstOrDefault(i =>
                string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            return item != null && item.Kind != DocumentKind.Folder;
        }

        private List<DocumentItemViewModel> SortItems(IEnumerable<DocumentItemViewModel> items)
        {
            return FileBrowserSort.Apply(
                items,
                _sortMode,
                i => i.Kind == DocumentKind.Folder,
                i => i.Name,
                i => i.Model.ModifiedLocal,
                i => i.Model.SizeBytes);
        }

        private void PersistSortMode()
        {
            if (_settingsService == null)
                return;
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.ReaderSortMode = _sortMode.ToString();
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        private async Task EnterOrOpenAsync(DocumentItemViewModel item)
        {
            if (item == null)
                return;

            try
            {
                if (item.Kind == DocumentKind.Folder)
                {
                    ClearSelection();
                    _readerService.EnterDirectory(item.Path);
                    SaveCurrentPathToActiveTab();
                    PersistPathTabs();
                    Reload();
                    return;
                }

                IsBusy = true;
                RaiseStateChanged();
                await _readerService.OpenAsync(item.Model);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(
                    _languageService.LoadTextFmt("reader_open_failed", ex.Message),
                    FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        private async Task PickFileAsync()
        {
            try
            {
                IsBusy = true;
                RaiseStateChanged();
                await _readerService.PickAndOpenAsync();
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(
                    _languageService.LoadTextFmt("reader_open_failed", ex.Message),
                    FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        public void CloseDocument()
        {
            _readerService.CloseDocument();
            RaiseStateChanged();
        }

        private async Task PrevPdfPageAsync()
        {
            if (!CanGoPrevPdfPage)
                return;
            await _readerService.GoToPdfPageAsync(ActiveDocument.PdfPageIndex - 1);
            RaiseStateChanged();
        }

        private async Task NextPdfPageAsync()
        {
            if (!CanGoNextPdfPage)
                return;
            await _readerService.GoToPdfPageAsync(ActiveDocument.PdfPageIndex + 1);
            RaiseStateChanged();
        }

        private async Task OpenExternallyAsync()
        {
            try
            {
                string path = ActiveDocument?.Path;
                await _readerService.OpenExternallyAsync(path, mimeType: null);
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(
                    _languageService.LoadTextFmt("reader_open_failed", ex.Message),
                    FeedbackSeverity.Warning);
            }
        }

        private void OnReaderStateChanged()
        {
            OnPropertyChanged(
                nameof(HasOpenDocument),
                nameof(ActiveDocument),
                nameof(ActiveTitle),
                nameof(IsPdfDocument),
                nameof(PdfPageLabel),
                nameof(CanGoPrevPdfPage),
                nameof(CanGoNextPdfPage));
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>List item wrapper for a document browser entry.</summary>
    public class DocumentItemViewModel
    {
        public DocumentItemViewModel(DocumentItemModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public DocumentItemModel Model { get; }

        public string Path => Model.Path;

        public string Name => Model.Name;

        public DocumentKind Kind => Model.Kind;

        public string MetaText
        {
            get
            {
                if (Kind == DocumentKind.Folder)
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
