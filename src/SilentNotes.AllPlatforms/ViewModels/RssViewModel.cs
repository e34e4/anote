// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the RSS reader tab. Feed subscriptions and read state are stored in the
    /// shared <see cref="NoteRepositoryModel"/> and synchronized with notes.
    /// </summary>
    public class RssViewModel : ViewModelBase
    {
        public const int PageSizePerFeed = 20;
        public const int TabIndex = 18;

        private readonly NoteRepositoryModel _model;
        private readonly ILanguageService _languageService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly IRssArticleCacheService _articleCache;
        private readonly IFilePickerService _filePickerService;
        private readonly IFeedbackService _feedbackService;
        private readonly IWebdavQuickSaveService _webdavQuickSave;
        private readonly IWebdavFileService _webdavFileService;
        private readonly INativeBrowserService _browserService;

        private string _filter;
        private string _draftFeedUrl;
        private string _draftFeedTitle;
        private Guid? _editingFeedId;
        private bool _isFeedsPageOpen;
        private bool _isAddFeedOpen;
        private bool _isBusy;
        private string _statusMessage;
        private int _loadedPages = 1;
        private bool _cacheLoaded;
        private RssArticleViewModel _openedArticle;
        private readonly List<RssArticleViewModel> _allArticles = new List<RssArticleViewModel>();
        private readonly Dictionary<Guid, List<RssArticleViewModel>> _articlesByFeed =
            new Dictionary<Guid, List<RssArticleViewModel>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RssViewModel"/> class.
        /// </summary>
        public RssViewModel(
            NoteRepositoryModel model,
            ILanguageService languageService,
            IRepositoryStorageService repositoryService,
            IRssArticleCacheService articleCache,
            IFilePickerService filePickerService,
            IFeedbackService feedbackService,
            IWebdavQuickSaveService webdavQuickSave,
            IWebdavFileService webdavFileService,
            INativeBrowserService browserService)
        {
            _model = model;
            _languageService = languageService;
            _repositoryService = repositoryService;
            _articleCache = articleCache;
            _filePickerService = filePickerService;
            _feedbackService = feedbackService;
            _webdavQuickSave = webdavQuickSave;
            _webdavFileService = webdavFileService;
            _browserService = browserService;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            LoadMoreCommand = new RelayCommand(LoadMore);
            OpenFeedsPageCommand = new RelayCommand(() => IsFeedsPageOpen = true);
            CloseFeedsPageCommand = new RelayCommand(() => IsFeedsPageOpen = false);
            StartAddFeedCommand = new RelayCommand(StartAddFeed);
            StartEditFeedCommand = new RelayCommand<RssFeedItemViewModel>(StartEditFeed);
            SaveFeedCommand = new AsyncRelayCommand(SaveFeedAsync);
            CancelFeedEditCommand = new RelayCommand(CancelFeedEdit);
            DeleteFeedCommand = new RelayCommand<RssFeedItemViewModel>(DeleteFeed);
            OpenArticleCommand = new RelayCommand<RssArticleViewModel>(OpenArticle);
            CloseArticleCommand = new RelayCommand(CloseArticle);
            OpenArticleLinkCommand = new RelayCommand(OpenArticleLink);
            MarkUnreadCommand = new RelayCommand<RssArticleViewModel>(MarkUnread);
            ExportOpmlCommand = new AsyncRelayCommand(ExportOpmlAsync);
            ImportOpmlCommand = new AsyncRelayCommand(ImportOpmlAsync);
            SaveArticleToWebdavCommand = new AsyncRelayCommand(SaveOpenedArticleToWebdavAsync);
            SaveArticleItemToWebdavCommand = new AsyncRelayCommand<RssArticleViewModel>(SaveArticleToWebdavAsync);
        }

        /// <summary>Raised when the UI should re-render after async work.</summary>
        public event Action StateChanged;

        public ICommand RefreshCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand OpenFeedsPageCommand { get; }
        public ICommand CloseFeedsPageCommand { get; }
        public ICommand StartAddFeedCommand { get; }
        public ICommand StartEditFeedCommand { get; }
        public ICommand SaveFeedCommand { get; }
        public ICommand CancelFeedEditCommand { get; }
        public ICommand DeleteFeedCommand { get; }
        public ICommand OpenArticleCommand { get; }
        public ICommand CloseArticleCommand { get; }
        public ICommand OpenArticleLinkCommand { get; }
        public ICommand MarkUnreadCommand { get; }
        public ICommand ExportOpmlCommand { get; }
        public ICommand ImportOpmlCommand { get; }
        public ICommand SaveArticleToWebdavCommand { get; }
        public ICommand SaveArticleItemToWebdavCommand { get; }

        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    NotifyListChanged();
                    RaiseStateChanged();
                }
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseStateChanged();
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                    RaiseStateChanged();
            }
        }

        public bool IsFeedsPageOpen
        {
            get { return _isFeedsPageOpen; }
            set
            {
                if (SetProperty(ref _isFeedsPageOpen, value))
                    RaiseStateChanged();
            }
        }

        public bool IsAddFeedOpen
        {
            get { return _isAddFeedOpen; }
            private set
            {
                if (SetProperty(ref _isAddFeedOpen, value))
                    RaiseStateChanged();
            }
        }

        public string DraftFeedUrl
        {
            get { return _draftFeedUrl; }
            set { SetProperty(ref _draftFeedUrl, value); }
        }

        public string DraftFeedTitle
        {
            get { return _draftFeedTitle; }
            set { SetProperty(ref _draftFeedTitle, value); }
        }

        public bool IsEditingFeed
        {
            get { return _editingFeedId != null; }
        }

        public RssArticleViewModel OpenedArticle
        {
            get { return _openedArticle; }
            private set
            {
                if (SetProperty(ref _openedArticle, value))
                {
                    OnPropertyChanged(nameof(IsArticleOpen));
                    RaiseStateChanged();
                }
            }
        }

        public bool IsArticleOpen
        {
            get { return _openedArticle != null; }
        }

        public bool IsAnyOverlayOpen
        {
            get { return IsArticleOpen || IsAddFeedOpen || IsFeedsPageOpen; }
        }

        public IReadOnlyList<RssFeedItemViewModel> Feeds
        {
            get
            {
                return _model.RssFeeds
                    .Select(feed => new RssFeedItemViewModel(feed))
                    .ToList();
            }
        }

        public bool HasFeeds
        {
            get { return _model.RssFeeds.Count > 0; }
        }

        public IReadOnlyList<RssArticleViewModel> VisibleArticles
        {
            get
            {
                IEnumerable<RssArticleViewModel> source = BuildPagedArticles();
                string query = _filter?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(query))
                {
                    source = source.Where(a =>
                        (!string.IsNullOrEmpty(a.Title) && a.Title.ToLowerInvariant().Contains(query)) ||
                        (!string.IsNullOrEmpty(a.Summary) && a.Summary.ToLowerInvariant().Contains(query)) ||
                        (!string.IsNullOrEmpty(a.FeedTitle) && a.FeedTitle.ToLowerInvariant().Contains(query)));
                }
                return source.ToList();
            }
        }

        public bool CanLoadMore
        {
            get
            {
                if (_articlesByFeed.Count == 0)
                    return false;
                int limit = _loadedPages * PageSizePerFeed;
                return _articlesByFeed.Values.Any(list => list.Count > limit);
            }
        }

        public bool IsEmpty
        {
            get { return !HasFeeds; }
        }

        public bool HasNoArticles
        {
            get { return HasFeeds && VisibleArticles.Count == 0 && !IsBusy; }
        }

        /// <summary>
        /// Closes the topmost overlay (article, add/edit dialog, or feeds page).
        /// </summary>
        public bool CloseOverlay()
        {
            if (IsArticleOpen)
            {
                CloseArticle();
                return true;
            }
            if (IsAddFeedOpen)
            {
                CancelFeedEdit();
                return true;
            }
            if (IsFeedsPageOpen)
            {
                IsFeedsPageOpen = false;
                return true;
            }
            return false;
        }

        private IEnumerable<RssArticleViewModel> BuildPagedArticles()
        {
            int limit = Math.Max(1, _loadedPages) * PageSizePerFeed;
            var selected = new List<RssArticleViewModel>();
            foreach (KeyValuePair<Guid, List<RssArticleViewModel>> pair in _articlesByFeed)
            {
                selected.AddRange(pair.Value.Take(limit));
            }

            return selected
                .OrderByDescending(a => a.PublishedAt ?? DateTime.MinValue)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase);
        }

        private void StartAddFeed()
        {
            _editingFeedId = null;
            DraftFeedUrl = string.Empty;
            DraftFeedTitle = string.Empty;
            IsAddFeedOpen = true;
            OnPropertyChanged(nameof(IsEditingFeed));
        }

        private void StartEditFeed(RssFeedItemViewModel feed)
        {
            if (feed == null)
                return;
            _editingFeedId = feed.Id;
            DraftFeedUrl = feed.Url;
            DraftFeedTitle = feed.Title;
            IsAddFeedOpen = true;
            OnPropertyChanged(nameof(IsEditingFeed));
        }

        private void CancelFeedEdit()
        {
            IsAddFeedOpen = false;
            _editingFeedId = null;
            DraftFeedUrl = null;
            DraftFeedTitle = null;
            OnPropertyChanged(nameof(IsEditingFeed));
        }

        private async Task SaveFeedAsync()
        {
            string url = DraftFeedUrl?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                _feedbackService.ShowToast(_languageService["rss_url_required"], FeedbackSeverity.Warning);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _feedbackService.ShowToast(_languageService["rss_url_invalid"], FeedbackSeverity.Warning);
                return;
            }

            string title = DraftFeedTitle?.Trim();
            string siteUrl = null;

            // Validate / enrich from the live feed when possible.
            try
            {
                IsBusy = true;
                StatusMessage = _languageService["rss_fetching"];
                string xml = await DownloadFeedXmlAsync(url);
                RssFeedParser.ParsedFeed parsed = RssFeedParser.Parse(xml);
                if (parsed != null)
                {
                    if (string.IsNullOrWhiteSpace(title))
                        title = parsed.Title;
                    siteUrl = parsed.SiteUrl;
                }
            }
            catch (Exception ex)
            {
                // Allow saving the URL even if the first fetch fails (offline / temporary error).
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }

            if (string.IsNullOrWhiteSpace(title))
                title = uri.Host;

            if (_editingFeedId != null)
            {
                RssFeedModel existing = _model.RssFeeds.FindById(_editingFeedId.Value);
                if (existing != null)
                {
                    RssFeedModel duplicate = _model.RssFeeds.FindByUrl(url);
                    if (duplicate != null && duplicate.Id != existing.Id)
                    {
                        _feedbackService.ShowToast(_languageService["rss_feed_exists"], FeedbackSeverity.Warning);
                        return;
                    }

                    existing.Url = url;
                    existing.Title = title;
                    if (!string.IsNullOrWhiteSpace(siteUrl))
                        existing.SiteUrl = siteUrl;
                    existing.RefreshModifiedAt();
                }
            }
            else
            {
                if (_model.RssFeeds.FindByUrl(url) != null)
                {
                    _feedbackService.ShowToast(_languageService["rss_feed_exists"], FeedbackSeverity.Warning);
                    return;
                }

                _model.RssFeeds.Add(new RssFeedModel
                {
                    Url = url,
                    Title = title,
                    SiteUrl = siteUrl,
                });
            }

            Persist();
            CancelFeedEdit();
            NotifyFeedsChanged();
            await RefreshAsync();
        }

        private void DeleteFeed(RssFeedItemViewModel feed)
        {
            if (feed == null)
                return;
            RssFeedModel model = _model.RssFeeds.FindById(feed.Id);
            if (model == null)
                return;

            _model.RssFeeds.Remove(model);
            _articlesByFeed.Remove(feed.Id);
            try
            {
                _articleCache.RemoveFeed(feed.Id);
            }
            catch (Exception)
            {
            }
            Persist();
            NotifyFeedsChanged();
            RebuildAllArticles();
            NotifyListChanged();
            RaiseStateChanged();
        }

        /// <summary>
        /// Loads articles from the local SQLite cache so the RSS tab can render immediately.
        /// Safe to call repeatedly; subsequent calls are no-ops while articles are already loaded.
        /// </summary>
        public void LoadFromCache()
        {
            if (!HasFeeds)
            {
                _allArticles.Clear();
                _articlesByFeed.Clear();
                _loadedPages = 1;
                _cacheLoaded = true;
                NotifyListChanged();
                RaiseStateChanged();
                return;
            }

            if (_cacheLoaded && _articlesByFeed.Count > 0)
                return;

            try
            {
                IReadOnlyList<RssCachedArticleModel> cached = _articleCache.LoadArticles(
                    _model.RssFeeds.Select(f => f.Id));
                ApplyCachedArticles(cached);
                _cacheLoaded = true;
            }
            catch (Exception)
            {
                _cacheLoaded = true;
            }

            NotifyListChanged();
            RaiseStateChanged();
        }

        /// <summary>
        /// Fetches remote feeds and merges newer articles into the local cache and UI.
        /// Existing cached articles remain visible while the fetch runs.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (!HasFeeds)
            {
                _allArticles.Clear();
                _articlesByFeed.Clear();
                _loadedPages = 1;
                _cacheLoaded = true;
                NotifyListChanged();
                RaiseStateChanged();
                return;
            }

            if (!_cacheLoaded || _articlesByFeed.Count == 0)
                LoadFromCache();

            IsBusy = true;
            StatusMessage = _languageService["rss_fetching"];
            var errors = new List<string>();
            int mergedTotal = 0;

            try
            {
                foreach (RssFeedModel feed in _model.RssFeeds.ToList())
                {
                    try
                    {
                        string xml = await DownloadFeedXmlAsync(feed.Url);
                        RssFeedParser.ParsedFeed parsed = RssFeedParser.Parse(xml);
                        if (parsed == null)
                        {
                            errors.Add(feed.Title ?? feed.Url);
                            continue;
                        }

                        // Keep feed title/site in sync when the remote feed provides them.
                        bool feedTouched = false;
                        if (!string.IsNullOrWhiteSpace(parsed.Title) &&
                            !string.Equals(feed.Title, parsed.Title, StringComparison.Ordinal))
                        {
                            feed.Title = parsed.Title;
                            feedTouched = true;
                        }
                        if (!string.IsNullOrWhiteSpace(parsed.SiteUrl) &&
                            !string.Equals(feed.SiteUrl, parsed.SiteUrl, StringComparison.Ordinal))
                        {
                            feed.SiteUrl = parsed.SiteUrl;
                            feedTouched = true;
                        }
                        if (feedTouched)
                        {
                            feed.RefreshModifiedAt();
                            Persist();
                        }

                        List<RssCachedArticleModel> fetched = parsed.Items
                            .Select(item => ToCachedArticle(feed.Id, item))
                            .ToList();

                        int merged = _articleCache.MergeArticles(feed.Id, fetched);
                        mergedTotal += merged;

                        // Rebuild this feed's in-memory list from cache so we keep older items
                        // that are no longer in the remote window.
                        IReadOnlyList<RssCachedArticleModel> feedCached = _articleCache.LoadArticles(new[] { feed.Id });
                        string feedTitle = feed.Title ?? feed.Url;
                        _articlesByFeed[feed.Id] = feedCached
                            .Select(c => new RssArticleViewModel(
                                c,
                                feedTitle,
                                _model.RssReadItems.ContainsKey(c.ItemKey)))
                            .OrderByDescending(a => a.PublishedAt ?? DateTime.MinValue)
                            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    catch (Exception)
                    {
                        errors.Add(feed.Title ?? feed.Url);
                    }
                }

                RebuildAllArticles();
                NotifyFeedsChanged();
                NotifyListChanged();

                if (errors.Count > 0)
                {
                    StatusMessage = _languageService.LoadTextFmt("rss_fetch_partial", errors.Count);
                    _feedbackService.ShowToast(StatusMessage, FeedbackSeverity.Warning);
                }
                else
                {
                    StatusMessage = _languageService.LoadTextFmt("rss_fetch_ok", _allArticles.Count);
                    if (mergedTotal > 0)
                        StatusMessage = StatusMessage + " (+" + mergedTotal.ToString(CultureInfo.InvariantCulture) + ")";
                }
            }
            finally
            {
                IsBusy = false;
                RaiseStateChanged();
            }
        }

        private void ApplyCachedArticles(IReadOnlyList<RssCachedArticleModel> cached)
        {
            _articlesByFeed.Clear();
            _allArticles.Clear();
            _loadedPages = 1;

            if (cached == null || cached.Count == 0)
                return;

            Dictionary<Guid, string> feedTitles = _model.RssFeeds.ToDictionary(
                f => f.Id,
                f => f.Title ?? f.Url);

            foreach (IGrouping<Guid, RssCachedArticleModel> group in cached.GroupBy(c => c.FeedId))
            {
                string feedTitle = feedTitles.TryGetValue(group.Key, out string title)
                    ? title
                    : group.Key.ToString();
                _articlesByFeed[group.Key] = group
                    .Select(c => new RssArticleViewModel(
                        c,
                        feedTitle,
                        _model.RssReadItems.ContainsKey(c.ItemKey)))
                    .OrderByDescending(a => a.PublishedAt ?? DateTime.MinValue)
                    .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            RebuildAllArticles();
        }

        private static RssCachedArticleModel ToCachedArticle(Guid feedId, RssFeedParser.ParsedItem item)
        {
            return new RssCachedArticleModel
            {
                FeedId = feedId,
                ItemKey = item.ItemKey,
                Title = item.Title,
                Link = item.Link,
                Summary = item.Summary,
                ContentHtml = item.ContentHtml ?? string.Empty,
                PublishedAt = item.PublishedAt,
                CachedAt = DateTime.UtcNow,
            };
        }

        private void LoadMore()
        {
            if (!CanLoadMore)
                return;
            _loadedPages++;
            NotifyListChanged();
            RaiseStateChanged();
        }

        private void OpenArticle(RssArticleViewModel article)
        {
            if (article == null)
                return;

            MarkRead(article);
            OpenedArticle = article;
        }

        private void CloseArticle()
        {
            OpenedArticle = null;
        }

        private void OpenArticleLink()
        {
            if (OpenedArticle == null || string.IsNullOrWhiteSpace(OpenedArticle.Link))
                return;
            _browserService.OpenWebsite(OpenedArticle.Link);
        }

        private void MarkRead(RssArticleViewModel article)
        {
            if (article == null || article.IsRead)
                return;

            if (!_model.RssReadItems.ContainsKey(article.ItemKey))
            {
                _model.RssReadItems.Add(new RssReadItemModel
                {
                    Key = article.ItemKey,
                    ModifiedAt = DateTime.UtcNow,
                });
                Persist();
            }

            article.IsRead = true;
            RaiseStateChanged();
        }

        private void MarkUnread(RssArticleViewModel article)
        {
            if (article == null)
                return;

            RssReadItemModel existing = _model.RssReadItems.FindByKey(article.ItemKey);
            if (existing != null)
            {
                _model.RssReadItems.Remove(existing);
                Persist();
            }

            article.IsRead = false;
            RaiseStateChanged();
        }

        private async Task ExportOpmlAsync()
        {
            string opml = OpmlFormat.Export(_model.RssFeeds);
            byte[] bytes = Encoding.UTF8.GetBytes(opml);
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "anote_rss_{0:yyyyMMdd_HHmmss}.opml",
                DateTime.Now);

            bool shared = false;
            try
            {
                shared = await _webdavFileService.ShareDownloadedFileAsync(fileName, bytes);
            }
            catch (Exception)
            {
                shared = false;
            }

            bool saved = false;
            if (_webdavQuickSave.IsConfigured)
            {
                saved = await _webdavQuickSave.SaveBytesAsAsync(fileName, bytes);
            }

            if (saved)
            {
                _feedbackService.ShowToast(
                    _languageService.LoadTextFmt("rss_opml_exported_webdav", _webdavQuickSave.LastFileName),
                    FeedbackSeverity.Info);
            }
            else if (shared)
            {
                _feedbackService.ShowToast(_languageService["rss_opml_exported"], FeedbackSeverity.Info);
            }
            else if (!_webdavQuickSave.IsConfigured)
            {
                _feedbackService.ShowToast(
                    shared ? _languageService["rss_opml_exported"] : _languageService["webdav_quick_not_configured"],
                    shared ? FeedbackSeverity.Info : FeedbackSeverity.Warning);
            }
            else
            {
                _feedbackService.ShowToast(
                    _languageService["rss_opml_export_failed"] + " — " + (_webdavQuickSave.LastError ?? string.Empty),
                    FeedbackSeverity.Warning);
            }
        }

        private async Task ImportOpmlAsync()
        {
            bool picked = await _filePickerService.PickFile(new[] { ".opml", ".xml" }, "text/xml");
            if (!picked)
                return;

            byte[] bytes = await _filePickerService.ReadPickedFile();
            string text = (bytes == null) ? string.Empty : Encoding.UTF8.GetString(bytes);

            RssFeedListModel imported = OpmlFormat.Import(text);
            if (imported == null)
            {
                _feedbackService.ShowToast(_languageService["rss_opml_import_failed"], FeedbackSeverity.Warning);
                return;
            }

            int added = 0;
            foreach (RssFeedModel feed in imported)
            {
                if (_model.RssFeeds.FindByUrl(feed.Url) != null)
                    continue;
                _model.RssFeeds.Add(feed);
                added++;
            }

            Persist();
            NotifyFeedsChanged();
            _feedbackService.ShowToast(
                _languageService.LoadTextFmt("rss_opml_imported", added), FeedbackSeverity.Info);

            if (added > 0)
                await RefreshAsync();
            else
                RaiseStateChanged();
        }

        private Task SaveOpenedArticleToWebdavAsync()
        {
            return SaveArticleToWebdavAsync(OpenedArticle);
        }

        private async Task SaveArticleToWebdavAsync(RssArticleViewModel article)
        {
            if (article == null)
                return;

            if (!_webdavQuickSave.IsConfigured)
            {
                _feedbackService.ShowToast(_languageService["webdav_quick_not_configured"], FeedbackSeverity.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(article.Title ?? string.Empty);
            sb.AppendLine(article.FeedTitle ?? string.Empty);
            if (article.PublishedAt != null)
                sb.AppendLine(article.PublishedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            if (!string.IsNullOrWhiteSpace(article.Link))
                sb.AppendLine(article.Link);
            sb.AppendLine();
            string body = RssFeedParser.StripHtml(article.ContentHtml);
            if (string.IsNullOrWhiteSpace(body))
                body = article.Summary ?? string.Empty;
            sb.Append(body);

            bool ok = await _webdavQuickSave.SaveTextAsync("Rss", sb.ToString());
            if (ok)
            {
                _feedbackService.ShowToast(
                    _languageService.LoadTextFmt("webdav_quick_saved", _webdavQuickSave.LastFileName),
                    FeedbackSeverity.Info);
            }
            else
            {
                _feedbackService.ShowToast(
                    _languageService["webdav_quick_save_failed"] + " — " + (_webdavQuickSave.LastError ?? string.Empty),
                    FeedbackSeverity.Warning);
            }
        }

        private static async Task<string> DownloadFeedXmlAsync(string url)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
            };
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "aNote/1.0 (RSS Reader; +https://github.com/stommis/anote)");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/rss+xml, application/atom+xml, application/xml, text/xml, */*");

                using (HttpResponseMessage response = await client.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        private void RebuildAllArticles()
        {
            _allArticles.Clear();
            foreach (List<RssArticleViewModel> list in _articlesByFeed.Values)
                _allArticles.AddRange(list);
        }

        private void Persist()
        {
            _repositoryService.TrySaveRepository(_model);
        }

        private void NotifyFeedsChanged()
        {
            OnPropertyChanged(nameof(Feeds));
            OnPropertyChanged(nameof(HasFeeds));
            OnPropertyChanged(nameof(IsEmpty));
        }

        private void NotifyListChanged()
        {
            OnPropertyChanged(nameof(VisibleArticles));
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(HasNoArticles));
            OnPropertyChanged(nameof(IsEmpty));
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// View model for a single feed subscription row.
    /// </summary>
    public class RssFeedItemViewModel
    {
        private readonly RssFeedModel _model;

        public RssFeedItemViewModel(RssFeedModel model)
        {
            _model = model;
        }

        public Guid Id { get { return _model.Id; } }
        public string Title { get { return string.IsNullOrWhiteSpace(_model.Title) ? _model.Url : _model.Title; } }
        public string Url { get { return _model.Url; } }
        public string SiteUrl { get { return _model.SiteUrl; } }
    }

    /// <summary>
    /// View model for a single RSS/Atom article in the combined feed list.
    /// </summary>
    public class RssArticleViewModel : ViewModelBase
    {
        private bool _isRead;

        public RssArticleViewModel(Guid feedId, string feedTitle, RssFeedParser.ParsedItem item, bool isRead)
        {
            FeedId = feedId;
            FeedTitle = feedTitle;
            ItemKey = item.ItemKey;
            Title = item.Title;
            Link = item.Link;
            Summary = item.Summary;
            ContentHtml = RssFeedParser.SanitizeHtml(item.ContentHtml);
            ContentPlain = RssFeedParser.StripHtml(item.ContentHtml);
            PublishedAt = item.PublishedAt;
            _isRead = isRead;
        }

        public RssArticleViewModel(RssCachedArticleModel cached, string feedTitle, bool isRead)
        {
            FeedId = cached.FeedId;
            FeedTitle = feedTitle;
            ItemKey = cached.ItemKey;
            Title = cached.Title;
            Link = cached.Link;
            Summary = cached.Summary;
            ContentHtml = RssFeedParser.SanitizeHtml(cached.ContentHtml);
            ContentPlain = RssFeedParser.StripHtml(cached.ContentHtml);
            PublishedAt = cached.PublishedAt;
            _isRead = isRead;
        }

        public Guid FeedId { get; }
        public string FeedTitle { get; }
        public string ItemKey { get; }
        public string Title { get; }
        public string Link { get; }
        public string Summary { get; }
        public string ContentHtml { get; }
        public string ContentPlain { get; }
        public DateTime? PublishedAt { get; }

        public bool IsRead
        {
            get { return _isRead; }
            set { SetProperty(ref _isRead, value); }
        }

        public string PublishedText
        {
            get
            {
                if (PublishedAt == null)
                    return string.Empty;
                return PublishedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            }
        }

        public string TitleCssClass
        {
            get { return IsRead ? "rss-title-read" : "rss-title-unread"; }
        }
    }
}
