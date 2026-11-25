// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private const string OfflineItemKey = "bookmark";

        private readonly NoteRepositoryModel _model;
        private readonly ILanguageService _languageService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly INativeBrowserService _browserService;
        private readonly IAbroBrowserService _abroBrowserService;
        private readonly IEnvironmentService _environmentService;
        private readonly IBookySyncService _bookySyncService;
        private readonly IFeedbackService _feedbackService;
        private readonly ArticleOfflineService _offlineService;
        private readonly IBookmarkLinkCheckService _linkCheckService;
        private string _filter;
        private bool _filterBrokenOnly;
        private bool _isLinkCheckBusy;
        private bool _isDraftVisible;
        private string _draftUrl;
        private string _draftTitle;
        private string _draftQuote;
        private string _draftError;
        private bool _isOfflineVisible;
        private bool _isOfflineBusy;
        private string _offlineTitle;
        private string _offlineHtml;
        private string _offlinePlain;
        private Guid? _offlineBookmarkId;
        private List<BookmarkItemViewModel> _bookmarks = new List<BookmarkItemViewModel>();
        private AsyncRelayCommand _checkLinksCommand;
        private AsyncRelayCommand _checkSelectedLinksCommand;

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
            IFeedbackService feedbackService,
            IAbroBrowserService abroBrowserService = null,
            IRssArticleCacheService articleCache = null,
            IBookmarkLinkCheckService linkCheckService = null)
        {
            _model = model;
            _languageService = languageService;
            _repositoryService = repositoryService;
            _browserService = browserService;
            _abroBrowserService = abroBrowserService;
            _environmentService = environmentService;
            _bookySyncService = bookySyncService;
            _feedbackService = feedbackService;
            _offlineService = new ArticleOfflineService(articleCache);
            _linkCheckService = linkCheckService ?? new NullBookmarkLinkCheckService();

            OpenCommand = new RelayCommand<BookmarkItemViewModel>(Open);
            DeleteCommand = new AsyncRelayCommand<BookmarkItemViewModel>(Delete);
            StartAddCommand = new RelayCommand(StartAdd);
            SaveDraftCommand = new RelayCommand(SaveDraft);
            CancelDraftCommand = new RelayCommand(CancelDraft);
            SyncCommand = new AsyncRelayCommand(SyncBookmarks);
            SaveOfflineCommand = new AsyncRelayCommand<BookmarkItemViewModel>(SaveOfflineAsync);
            ReadOfflineCommand = new RelayCommand<BookmarkItemViewModel>(ReadOffline);
            CloseOfflineCommand = new RelayCommand(CloseOffline);
            _checkLinksCommand = new AsyncRelayCommand(CheckAllLinksAsync, () => !IsLinkCheckBusy && !IsEmpty);
            _checkSelectedLinksCommand = new AsyncRelayCommand(CheckFilteredLinksAsync, () => !IsLinkCheckBusy && FilteredBookmarks.Count > 0);
            CheckLinksCommand = _checkLinksCommand;
            CheckSelectedLinksCommand = _checkSelectedLinksCommand;

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

        /// <summary>Gets the command to download and cache a readable offline copy of a bookmark.</summary>
        public ICommand SaveOfflineCommand { get; }

        /// <summary>Gets the command to open the stored offline copy.</summary>
        public ICommand ReadOfflineCommand { get; }

        /// <summary>Gets the command to close the offline reader.</summary>
        public ICommand CloseOfflineCommand { get; }

        /// <summary>Gets the command to check all bookmark links.</summary>
        public ICommand CheckLinksCommand { get; }

        /// <summary>Gets the command to check links for the currently filtered bookmarks.</summary>
        public ICommand CheckSelectedLinksCommand { get; }

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
                    RefreshBookmarkListProperties();
                    _checkSelectedLinksCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether only broken bookmarks are shown.
        /// </summary>
        public bool FilterBrokenOnly
        {
            get { return _filterBrokenOnly; }
            set
            {
                if (SetProperty(ref _filterBrokenOnly, value))
                    RefreshBookmarkListProperties();
            }
        }

        /// <summary>
        /// Gets a value indicating whether a link check is in progress.
        /// </summary>
        public bool IsLinkCheckBusy
        {
            get { return _isLinkCheckBusy; }
            private set
            {
                if (SetProperty(ref _isLinkCheckBusy, value))
                {
                    _checkLinksCommand?.NotifyCanExecuteChanged();
                    _checkSelectedLinksCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets the filtered bookmarks, newest first.
        /// </summary>
        public IReadOnlyList<BookmarkItemViewModel> FilteredBookmarks
        {
            get { return GetDisplayBookmarks(); }
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
            get { return string.IsNullOrEmpty(_filter) && !_filterBrokenOnly && (_bookmarks.Count > 10); }
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

                return GetDisplayBookmarks()
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

        /// <summary>Gets or sets a value indicating whether the offline reader overlay is open.</summary>
        public bool IsOfflineVisible
        {
            get { return _isOfflineVisible; }
            private set { SetProperty(ref _isOfflineVisible, value); }
        }

        /// <summary>Gets a value indicating whether an offline copy is being downloaded.</summary>
        public bool IsOfflineBusy
        {
            get { return _isOfflineBusy; }
            private set { SetProperty(ref _isOfflineBusy, value); }
        }

        /// <summary>Gets the title of the article shown in the offline reader.</summary>
        public string OfflineTitle
        {
            get { return _offlineTitle; }
            private set { SetProperty(ref _offlineTitle, value); }
        }

        /// <summary>Gets the HTML body of the article shown in the offline reader.</summary>
        public string OfflineHtml
        {
            get { return _offlineHtml; }
            private set { SetProperty(ref _offlineHtml, value); }
        }

        /// <summary>Gets the plain text of the article shown in the offline reader.</summary>
        public string OfflinePlain
        {
            get { return _offlinePlain; }
            private set { SetProperty(ref _offlinePlain, value); }
        }

        /// <summary>
        /// Reloads the bookmarks from the shared repository model.
        /// </summary>
        public void Reload()
        {
            _model.Bookmarks.SortByCreatedDescending();
            _bookmarks = _model.Bookmarks
                .Select(bookmark =>
                {
                    var item = new BookmarkItemViewModel(bookmark)
                    {
                        IsOffline = _offlineService.Load(bookmark.Id, OfflineItemKey)?.IsOffline == true,
                    };
                    item.UpdateLinkStatus(_languageService);
                    return item;
                })
                .ToList();

            RefreshBookmarkListProperties();
            _checkLinksCommand?.NotifyCanExecuteChanged();
            _checkSelectedLinksCommand?.NotifyCanExecuteChanged();
        }

        private List<BookmarkItemViewModel> GetDisplayBookmarks()
        {
            IEnumerable<BookmarkItemViewModel> list = _bookmarks;
            if (_filterBrokenOnly)
                list = list.Where(b => b.IsBroken);
            string query = _filter?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(b => b.Matches(query));
            return list.ToList();
        }

        private void RefreshBookmarkListProperties()
        {
            OnPropertyChanged(nameof(FilteredBookmarks));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsGrouped));
            OnPropertyChanged(nameof(Groups));
        }

        private async Task CheckAllLinksAsync()
        {
            await CheckLinksCoreAsync(_bookmarks);
        }

        private async Task CheckFilteredLinksAsync()
        {
            await CheckLinksCoreAsync(FilteredBookmarks);
        }

        private async Task CheckLinksCoreAsync(IEnumerable<BookmarkItemViewModel> items)
        {
            var targets = items?
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Url))
                .ToList();
            if (targets == null || targets.Count == 0)
                return;

            try
            {
                IsLinkCheckBusy = true;
                var models = targets.Select(t => _model.Bookmarks.FindById(t.Id)).Where(m => m != null).ToList();
                await BookmarkLinkChecker.CheckBookmarksAsync(models, CancellationToken.None);

                foreach (BookmarkItemViewModel item in targets)
                    item.UpdateLinkStatus(_languageService);

                _repositoryService.TrySaveRepository(_model);
                RefreshBookmarkListProperties();

                int brokenCount = targets.Count(t => t.IsBroken);
                string message = brokenCount > 0
                    ? string.Format(_languageService["booky_link_check_done_broken"], brokenCount)
                    : _languageService["booky_link_check_done"];
                _feedbackService.ShowToast(message, brokenCount > 0 ? FeedbackSeverity.Warning : FeedbackSeverity.Info);
            }
            catch (Exception)
            {
                _feedbackService.ShowToast(_languageService["booky_link_check_failed"], FeedbackSeverity.Warning);
            }
            finally
            {
                IsLinkCheckBusy = false;
            }
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

            url = UrlCheckUtils.CleanTrackingParams(url, out _);

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
            if (_abroBrowserService?.IsSupported == true)
            {
                _ = _abroBrowserService.PresentUrlAsync(target);
                return;
            }

            _browserService.OpenWebsiteInNewTab(target);
        }

        private async Task SaveOfflineAsync(BookmarkItemViewModel bookmark)
        {
            if (bookmark == null)
                return;

            if (bookmark.IsOffline)
            {
                _offlineService.Remove(bookmark.Id, OfflineItemKey);
                bookmark.IsOffline = false;
                _feedbackService.ShowToast(_languageService["rss_offline_removed"], FeedbackSeverity.Info);
                return;
            }

            try
            {
                IsOfflineBusy = true;
                bool saved = await _offlineService.SaveAsync(
                    bookmark.Id, OfflineItemKey, bookmark.Url, bookmark.Title);
                bookmark.IsOffline = saved;
                _feedbackService.ShowToast(
                    saved ? _languageService["rss_offline_saved"] : _languageService["rss_offline_failed"],
                    saved ? FeedbackSeverity.Info : FeedbackSeverity.Warning);
            }
            finally
            {
                IsOfflineBusy = false;
            }
        }

        private void ReadOffline(BookmarkItemViewModel bookmark)
        {
            if (bookmark == null)
                return;

            RssCachedArticleModel cached = _offlineService.Load(bookmark.Id, OfflineItemKey);
            if (cached == null || !cached.IsOffline || string.IsNullOrWhiteSpace(cached.OfflineHtml))
            {
                _feedbackService.ShowToast(_languageService["booky_offline_missing"], FeedbackSeverity.Warning);
                return;
            }

            _offlineBookmarkId = bookmark.Id;
            OfflineTitle = bookmark.Title;
            OfflineHtml = RssFeedParser.SanitizeHtml(cached.OfflineHtml);
            OfflinePlain = RssFeedParser.StripHtml(cached.OfflineHtml);
            IsOfflineVisible = true;
        }

        private void CloseOffline()
        {
            IsOfflineVisible = false;
            OfflineTitle = null;
            OfflineHtml = null;
            OfflinePlain = null;
            _offlineBookmarkId = null;
        }

        private async Task Delete(BookmarkItemViewModel bookmark)
        {
            if (bookmark == null)
                return;
            BookmarkModel model = _model.Bookmarks.FindById(bookmark.Id);
            if (model == null)
                return;

            _model.Bookmarks.Remove(model);
            _repositoryService.TrySaveRepository(_model);
            Reload();

            try
            {
                // Push the deletion to the shared file, otherwise a later synchronization would
                // merge the still existing bookmark from the server back into the local list.
                await _bookySyncService.SyncAfterDeleteAsync(bookmark.Id);
            }
            catch (Exception)
            {
                // A failing synchronization must never bubble up into the UI.
            }
        }

        private async Task SyncBookmarks()
        {
            if (_bookySyncService.IsConfigured)
            {
                MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                    _languageService["booky_sync_overwrite_question"],
                    _languageService["booky_sync"],
                    MessageBoxButtons.YesNoCancel,
                    false);

                if (answer == MessageBoxResult.Cancel)
                    return;

                if (answer == MessageBoxResult.Yes)
                {
                    ShowSyncFeedback(await _bookySyncService.OverwriteRemoteAsync());
                    Reload();
                    return;
                }
            }

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
                    string detail = _bookySyncService?.LastError;
                    string message = string.IsNullOrWhiteSpace(detail)
                        ? _languageService["booky_sync_failed"]
                        : _languageService["booky_sync_failed"] + " — " + detail;
                    _feedbackService.ShowToast(message, FeedbackSeverity.Warning);
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

        private bool _isOffline;

        /// <summary>Gets or sets a value indicating whether a readable offline copy is stored.</summary>
        public bool IsOffline
        {
            get { return _isOffline; }
            set
            {
                if (SetProperty(ref _isOffline, value))
                    OnPropertyChanged(nameof(OfflineBadge));
            }
        }

        /// <summary>Gets a short badge shown next to offline bookmarks.</summary>
        public string OfflineBadge
        {
            get { return _isOffline ? "★" : string.Empty; }
        }

        /// <summary>Gets a value indicating whether the last link check marked this bookmark as broken.</summary>
        public bool IsBroken
        {
            get
            {
                return string.Equals(
                    Model.LinkCheckStatus,
                    BookmarkLinkCheckStatuses.Broken,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Gets a short human-readable link-check status for the UI.</summary>
        public string LinkStatusText { get; private set; }

        /// <summary>Gets a warning glyph for broken bookmarks.</summary>
        public string LinkWarningGlyph
        {
            get { return IsBroken ? "⚠" : string.Empty; }
        }

        /// <summary>
        /// Refreshes link-check related properties from the underlying model.
        /// </summary>
        /// <param name="languageService">Used to localize status text.</param>
        public void UpdateLinkStatus(ILanguageService languageService)
        {
            LinkStatusText = BuildLinkStatusText(languageService, Model.LinkCheckStatus, Model.LinkHttpStatus);
            OnPropertyChanged(nameof(IsBroken));
            OnPropertyChanged(nameof(LinkStatusText));
            OnPropertyChanged(nameof(LinkWarningGlyph));
        }

        private static string BuildLinkStatusText(ILanguageService languageService, string status, int httpStatus)
        {
            if (string.IsNullOrWhiteSpace(status) ||
                string.Equals(status, BookmarkLinkCheckStatuses.Unknown, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (string.Equals(status, BookmarkLinkCheckStatuses.Broken, StringComparison.OrdinalIgnoreCase))
            {
                if (httpStatus > 0)
                    return string.Format(languageService["booky_link_status_broken_code"], httpStatus);
                return languageService["booky_link_status_broken"];
            }

            if (string.Equals(status, BookmarkLinkCheckStatuses.Redirect, StringComparison.OrdinalIgnoreCase))
                return languageService["booky_link_status_redirect"];

            if (httpStatus > 0)
                return string.Format(languageService["booky_link_status_ok_code"], httpStatus);
            return languageService["booky_link_status_ok"];
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
