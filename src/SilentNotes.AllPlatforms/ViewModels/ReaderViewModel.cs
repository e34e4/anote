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
    /// View model for the Reader tab: browse folders and open text / Office / PDF / ebook files.
    /// </summary>
    public class ReaderViewModel : ViewModelBase
    {
        private readonly IDocumentReaderService _readerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private string _filter;
        private string _kindFilter = "all"; // all | text | office | pdf | ebook
        private List<DocumentItemViewModel> _items = new List<DocumentItemViewModel>();
        private bool _isBusy;

        public ReaderViewModel(
            IDocumentReaderService readerService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _readerService = readerService;
            _languageService = languageService;
            _feedbackService = feedbackService;

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

                return query.ToList();
            }
        }

        public bool IsEmpty => _items.Count == 0;

        public bool HasNoMatches => !IsEmpty && FilteredItems.Count == 0;

        public string CountText => _languageService.LoadTextFmt("reader_count", FilteredItems.Count);

        public bool HasOpenDocument => _readerService.ActiveDocument != null;

        public DocumentContentModel ActiveDocument => _readerService.ActiveDocument;

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

        public void Reload()
        {
            try
            {
                IsBusy = true;
                _items = _readerService.ListCurrentDirectory()
                    .Select(m => new DocumentItemViewModel(m))
                    .ToList();
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
            Reload();
        }

        private void GoToRoot(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (HasOpenDocument)
                CloseDocument();
            _readerService.GoToRoot(key);
            Reload();
        }

        private void SetKindFilter(string kind)
        {
            KindFilter = string.IsNullOrEmpty(kind) ? "all" : kind;
            RaiseStateChanged();
        }

        private async Task EnterOrOpenAsync(DocumentItemViewModel item)
        {
            if (item == null)
                return;

            try
            {
                if (item.Kind == DocumentKind.Folder)
                {
                    _readerService.EnterDirectory(item.Path);
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
