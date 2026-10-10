// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
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
    /// View model which presents the Booky bookmarks in the booky tab.
    /// </summary>
    /// <remarks>
    /// The bookmarks are part of the note repository, so this view model shares the same
    /// <see cref="NoteRepositoryModel"/> instance as the <see cref="NoteRepositoryViewModel"/>.
    /// </remarks>
    public class BookyViewModel : ViewModelBase
    {
        private readonly NoteRepositoryModel _model;
        private readonly ILanguageService _languageService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly INativeBrowserService _browserService;
        private readonly IEnvironmentService _environmentService;
        private readonly IBookySyncService _bookySyncService;
        private readonly IFeedbackService _feedbackService;
        private string _filter;
        private bool _isDraftVisible;
        private string _draftUrl;
        private string _draftTitle;
        private string _draftQuote;
        private string _draftError;
        private List<BookmarkItemViewModel> _bookmarks = new List<BookmarkItemViewModel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="BookyViewModel"/> class.
        /// </summary>
        /// <param name="model">The note repository which owns the bookmarks.</param>
        /// <param name="languageService">A language service.</param>
        /// <param name="repositoryService">The storage service used to persist changes.</param>
        /// <param name="browserService">The service used to open a bookmark in the browser.</param>
        /// <param name="environmentService">The environment service, used to read the browser url.</param>
        public BookyViewModel(
            NoteRepositoryModel model,
            ILanguageService languageService,
            IRepositoryStorageService repositoryService,
            INativeBrowserService browserService,
            IEnvironmentService environmentService,
            IBookySyncService bookySyncService,
            IFeedbackService feedbackService)
        {
            _model = model;
            _languageService = languageService;
            _repositoryService = repositoryService;
            _browserService = browserService;
            _environmentService = environmentService;
            _bookySyncService = bookySyncService;
            _feedbackService = feedbackService;

            OpenCommand = new RelayCommand<BookmarkItemViewModel>(Open);
            DeleteCommand = new RelayCommand<BookmarkItemViewModel>(Delete);
            StartAddCommand = new RelayCommand(StartAdd);
            SaveDraftCommand = new RelayCommand(SaveDraft);
            CancelDraftCommand = new RelayCommand(CancelDraft);
            SyncCommand = new AsyncRelayCommand(SyncBookmarks);

            Reload();
        }

        /// <summary>
        /// Gets the command to open a bookmark in the browser.
        /// </summary>
        public ICommand OpenCommand { get; }

        /// <summary>
        /// Gets the command to delete a bookmark.
        /// </summary>
        public ICommand DeleteCommand { get; }

        /// <summary>
        /// Gets the command to start adding a new bookmark.
        /// </summary>
        public ICommand StartAddCommand { get; }

        /// <summary>
        /// Gets the command to save the bookmark draft.
        /// </summary>
        public ICommand SaveDraftCommand { get; }

        /// <summary>
        /// Gets the command to cancel the bookmark draft.
        /// </summary>
        public ICommand CancelDraftCommand { get; }

        /// <summary>
        /// Gets the command to synchronize the bookmarks with the shared Booky bookmarks file.
        /// </summary>
        public ICommand SyncCommand { get; }

        /// <summary>
        /// Gets or sets the search filter.
        /// </summary>
        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilteredBookmarks));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(IsGrouped));
                    OnPropertyChanged(nameof(Groups));
                }
            }
        }

        /// <summary>
        /// Gets the filtered bookmarks, newest first.
        /// </summary>
        public IReadOnlyList<BookmarkItemViewModel> FilteredBookmarks
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _bookmarks;
                return _bookmarks.Where(b => b.Matches(query)).ToList();
            }
        }

        /// <summary>
        /// Gets a value indicating whether there are no bookmarks at all.
        /// </summary>
        public bool IsEmpty
        {
            get { return _bookmarks.Count == 0; }
        }

        /// <summary>
        /// Gets a value indicating whether the bookmarks should be grouped by host (only done for
        /// larger lists without an active search filter).
        /// </summary>
        public bool IsGrouped
        {
            get { return string.IsNullOrEmpty(_filter) && (_bookmarks.Count > 10); }
        }

        /// <summary>
        /// Gets the bookmarks grouped by host, used when <see cref="IsGrouped"/> is true.
        /// </summary>
        public IReadOnlyList<BookmarkGroupViewModel> Groups
        {
            get
            {
                if (!IsGrouped)
                    return new List<BookmarkGroupViewModel>();

                return _bookmarks
                    .GroupBy(b => BookyUrlUtils.HostOf(b.Url))
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new BookmarkGroupViewModel(group.Key, group.ToList()))
                    .ToList();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the add-bookmark dialog is open.
        /// </summary>
        public bool IsDraftVisible
        {
            get { return _isDraftVisible; }
            set { SetProperty(ref _isDraftVisible, value); }
        }

        /// <summary>
        /// Gets or sets the url of the bookmark draft.
        /// </summary>
        public string DraftUrl
        {
            get { return _draftUrl; }
            set { SetProperty(ref _draftUrl, value); }
        }

        /// <summary>
        /// Gets or sets the title of the bookmark draft.
        /// </summary>
        public string DraftTitle
        {
            get { return _draftTitle; }
            set { SetProperty(ref _draftTitle, value); }
        }

        /// <summary>
        /// Gets or sets the quote of the bookmark draft.
        /// </summary>
        public string DraftQuote
        {
            get { return _draftQuote; }
            set { SetProperty(ref _draftQuote, value); }
        }

        /// <summary>
        /// Gets a validation error message for the current draft, or null if it is valid.
        /// </summary>
        public string DraftError
        {
            get { return _draftError; }
            private set { SetProperty(ref _draftError, value); }
        }

        /// <summary>
        /// Reloads the bookmarks from the shared repository model.
        /// </summary>
        public void Reload()
        {
            _model.Bookmarks.SortByCreatedDescending();
            _bookmarks = _model.Bookmarks
                .Select(bookmark => new BookmarkItemViewModel(bookmark))
                .ToList();

            OnPropertyChanged(nameof(FilteredBookmarks));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsGrouped));
            OnPropertyChanged(nameof(Groups));
        }

        /// <summary>
        /// Opens the add-bookmark dialog, prefilled with a url and quote if available.
        /// </summary>
        /// <param name="url">The url to prefill, or null.</param>
        /// <param name="title">The title to prefill, or null.</param>
        /// <param name="quote">The quote to prefill, or null.</param>
        public void ShowDraft(string url, string title, string quote)
        {
            DraftUrl = !string.IsNullOrEmpty(url) ? url : (_environmentService.BrowserUrl?.LastBrowserUrl() ?? string.Empty);
            DraftTitle = title ?? string.Empty;
            DraftQuote = quote ?? string.Empty;
            DraftError = null;
            IsDraftVisible = true;
        }

        private void StartAdd()
        {
            ShowDraft(null, null, null);
        }

        private void CancelDraft()
        {
            IsDraftVisible = false;
            DraftError = null;
        }

        private void SaveDraft()
        {
            string url = DraftUrl?.Trim();
            if (!BookyUrlUtils.LooksLikeUrl(url))
            {
                DraftError = _languageService["booky_error_url"];
                return;
            }

            string title = DraftTitle?.Trim();
            var bookmark = new BookmarkModel
            {
                Url = url,
                Title = string.IsNullOrEmpty(title) ? BookyUrlUtils.HostOf(url) : title,
                Quote = DraftQuote?.Trim(),
                Note = string.Empty,
            };

            _model.Bookmarks.Insert(0, bookmark);
            _repositoryService.TrySaveRepository(_model);
            TriggerAutoSync();

            IsDraftVisible = false;
            DraftError = null;
            Reload();
        }

        private void Open(BookmarkItemViewModel bookmark)
        {
            if (bookmark == null)
                return;
            string target = TextFragmentBuilder.WithTextFragment(bookmark.Url, bookmark.Quote);
            _browserService.OpenWebsiteInNewTab(target);
        }

        private void Delete(BookmarkItemViewModel bookmark)
        {
            if (bookmark == null)
                return;
            BookmarkModel model = _model.Bookmarks.FindById(bookmark.Id);
            if (model == null)
                return;

            _model.Bookmarks.Remove(model);
            _repositoryService.TrySaveRepository(_model);
            TriggerAutoSync();
            Reload();
        }

        private async Task SyncBookmarks()
        {
            BookySyncResult result = await _bookySyncService.SyncAsync();
            ShowSyncFeedback(result);
            Reload();
        }

        private async void TriggerAutoSync()
        {
            try
            {
                await _bookySyncService.SyncAsync();
            }
            catch (Exception)
            {
                // Auto synchronization must never bubble up into the UI.
            }
        }

        private void ShowSyncFeedback(BookySyncResult result)
        {
            switch (result)
            {
                case BookySyncResult.Success:
                    _feedbackService.ShowToast(_languageService["booky_sync_success"]);
                    break;
                case BookySyncResult.NotConfigured:
                    _feedbackService.ShowToast(_languageService["booky_sync_not_configured"]);
                    break;
                case BookySyncResult.Failed:
                    _feedbackService.ShowToast(_languageService["booky_sync_failed"], FeedbackSeverity.Warning);
                    break;
            }
        }
    }

    /// <summary>
    /// View model for a single bookmark.
    /// </summary>
    public class BookmarkItemViewModel : ViewModelBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkItemViewModel"/> class.
        /// </summary>
        /// <param name="model">The wrapped bookmark.</param>
        public BookmarkItemViewModel(BookmarkModel model)
        {
            Model = model;
        }

        /// <summary>
        /// Gets the wrapped model.
        /// </summary>
        public BookmarkModel Model { get; }

        /// <summary>Gets the id of the bookmark.</summary>
        public Guid Id
        {
            get { return Model.Id; }
        }

        /// <summary>Gets the title of the bookmark.</summary>
        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Model.Title) ? Model.Url : Model.Title; }
        }

        /// <summary>Gets the url of the bookmark.</summary>
        public string Url
        {
            get { return Model.Url; }
        }

        /// <summary>Gets the selected text quote of the bookmark.</summary>
        public string Quote
        {
            get { return Model.Quote; }
        }

        /// <summary>Gets the host of the bookmarked page.</summary>
        public string Host
        {
            get { return BookyUrlUtils.HostOf(Model.Url); }
        }

        /// <summary>
        /// Determines whether the bookmark matches a search query.
        /// </summary>
        /// <param name="query">Lower case search query.</param>
        /// <returns>True if the bookmark matches, otherwise false.</returns>
        public bool Matches(string query)
        {
            return Contains(Model.Title, query) ||
                   Contains(Model.Url, query) ||
                   Contains(Model.Quote, query) ||
                   Contains(Model.Note, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.ToLowerInvariant().Contains(query);
        }
    }

    /// <summary>
    /// A group of bookmarks belonging to the same host.
    /// </summary>
    public class BookmarkGroupViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkGroupViewModel"/> class.
        /// </summary>
        /// <param name="host">The host name of the group.</param>
        /// <param name="items">The bookmarks of the group.</param>
        public BookmarkGroupViewModel(string host, List<BookmarkItemViewModel> items)
        {
            Host = host;
            Items = items;
        }

        /// <summary>Gets the host name of the group.</summary>
        public string Host { get; }

        /// <summary>Gets the bookmarks of the group.</summary>
        public IReadOnlyList<BookmarkItemViewModel> Items { get; }

        /// <summary>Gets the number of bookmarks in the group.</summary>
        public int Count
        {
            get { return Items.Count; }
        }
    }
}
