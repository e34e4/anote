// Copyright В© 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// Searches all local tabs at once: notes, clips, bookmarks, mail, Telegram, RSS articles and
    /// password logins. Results are grouped per source and can be opened in their tab.
    /// </summary>
    public class GlobalSearchViewModel
    {
        private const int MaxNotes = 40;
        private const int MaxClips = 25;
        private const int MaxBookmarks = 25;
        private const int MaxMail = 30;
        private const int MaxTelegram = 20;
        private const int MaxRss = 30;
        private const int MaxPasswords = 20;

        private readonly IRepositoryStorageService _repositoryService;
        private readonly IClipboardHistoryService _clipboardHistoryService;
        private readonly IMailService _mailService;
        private readonly ITelegramService _telegramService;
        private readonly IRssArticleCacheService _rssArticleCacheService;
        private readonly IPasswordVaultService _passwordVaultService;
        private readonly ISafeKeyService _safeKeyService;
        private readonly List<GlobalSearchGroup> _groups = new List<GlobalSearchGroup>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalSearchViewModel"/> class.
        /// </summary>
        public GlobalSearchViewModel(
            IRepositoryStorageService repositoryService,
            IClipboardHistoryService clipboardHistoryService,
            IMailService mailService,
            ITelegramService telegramService,
            IRssArticleCacheService rssArticleCacheService,
            IPasswordVaultService passwordVaultService,
            ISafeKeyService safeKeyService)
        {
            _repositoryService = repositoryService;
            _clipboardHistoryService = clipboardHistoryService;
            _mailService = mailService;
            _telegramService = telegramService;
            _rssArticleCacheService = rssArticleCacheService;
            _passwordVaultService = passwordVaultService;
            _safeKeyService = safeKeyService;
        }

        /// <summary>Gets or sets the search text.</summary>
        public string Query { get; set; }

        /// <summary>Gets the result groups of the last search.</summary>
        public IReadOnlyList<GlobalSearchGroup> Groups => _groups;

        /// <summary>Gets the number of hits over all groups.</summary>
        public int TotalCount => _groups.Sum(group => group.Hits.Count);

        /// <summary>Gets a value indicating whether a search text was entered.</summary>
        public bool HasQuery => !string.IsNullOrWhiteSpace(Query) && Query.Trim().Length >= 2;

        /// <summary>Gets a value indicating whether the search returned nothing.</summary>
        public bool IsEmpty => HasQuery && TotalCount == 0;

        /// <summary>Gets the hint shown when the query is too short.</summary>
        public bool NeedsMoreCharacters => !HasQuery;

        /// <summary>Runs the search over all sources and rebuilds the groups.</summary>
        public void Search()
        {
            _groups.Clear();
            string query = (Query ?? string.Empty).Trim();
            if (query.Length < 2)
                return;

            _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            if (repository == null)
                return;

            AddGroup("global_search_group_notes", 0, SearchNotes(repository, query));
            AddGroup("global_search_group_clip", 1, SearchClips(query));
            AddGroup("global_search_group_bookmarks", 2, SearchBookmarks(repository, query));
            AddGroup("global_search_group_mail", 13, SearchMail(query));
            AddGroup("global_search_group_tg", 17, SearchTelegram(query));
            AddGroup("global_search_group_rss", 18, SearchRss(repository, query));
            AddGroup("global_search_group_password", 16, SearchPasswords(query));
        }

        private void AddGroup(string titleKey, int tabIndex, List<GlobalSearchHit> hits)
        {
            if (hits == null || hits.Count == 0)
                return;
            _groups.Add(new GlobalSearchGroup(titleKey, tabIndex, hits));
        }

        private List<GlobalSearchHit> SearchNotes(NoteRepositoryModel repository, string query)
        {
            var result = new List<GlobalSearchHit>();
            foreach (NoteModel note in repository.Notes)
            {
                if (note.InRecyclingBin)
                    continue;

                // Content of a note in a closed safe cannot be read, only its tags.
                bool locked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty
                    && !_safeKeyService.IsSafeOpen(note.SafeId.Value);
                string plain = locked ? string.Empty : NoteHtmlUtils.GetPlainText(note.HtmlContent);
                bool tagMatch = (note.Tags ?? new List<string>()).Any(tag => Contains(tag, query));
                if (locked && !tagMatch)
                    continue;

                if (!tagMatch && !Contains(plain, query))
                    continue;

                string title = locked ? "Encrypted note" : NoteHtmlUtils.GetTitle(note.HtmlContent);
                if (string.IsNullOrWhiteSpace(title))
                    title = FirstLine(plain);
                if (string.IsNullOrWhiteSpace(title))
                    title = "Untitled note";

                string detail = locked
                    ? string.Join(", ", note.Tags ?? new List<string>())
                    : Snippet(plain, query, 140);

                result.Add(new GlobalSearchHit
                {
                    Kind = GlobalSearchKind.Note,
                    Title = title,
                    Detail = detail,
                    Route = RouteNames.Combine(note.NoteType.GetRouteName(), note.Id),
                    TabIndex = 0,
                });

                if (result.Count >= MaxNotes)
                    break;
            }
            return result;
        }

        private List<GlobalSearchHit> SearchClips(string query)
        {
            var result = new List<GlobalSearchHit>();
            try
            {
                ClipboardHistoryModel history = _clipboardHistoryService?.LoadHistoryOrDefault();
                if (history?.Items == null)
                    return result;

                foreach (ClipboardItemModel item in history.Items)
                {
                    if (string.IsNullOrEmpty(item.Text) || !Contains(item.Text, query))
                        continue;
                    result.Add(new GlobalSearchHit
                    {
                        Kind = GlobalSearchKind.Clip,
                        Title = Snippet(item.Text, query, 80),
                        Detail = Snippet(item.Text, query, 160),
                        TabIndex = 1,
                        Filter = query,
                    });
                    if (result.Count >= MaxClips)
                        break;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private List<GlobalSearchHit> SearchBookmarks(NoteRepositoryModel repository, string query)
        {
            var result = new List<GlobalSearchHit>();
            foreach (BookmarkModel bookmark in repository.Bookmarks)
            {
                if (!Contains(bookmark.Title, query) && !Contains(bookmark.Url, query) && !Contains(bookmark.Quote, query))
                    continue;
                result.Add(new GlobalSearchHit
                {
                    Kind = GlobalSearchKind.Bookmark,
                    Title = bookmark.Title ?? bookmark.Url,
                    Detail = bookmark.Url,
                    TabIndex = 2,
                    Filter = query,
                });
                if (result.Count >= MaxBookmarks)
                    break;
            }
            return result;
        }

        private List<GlobalSearchHit> SearchMail(string query)
        {
            var result = new List<GlobalSearchHit>();
            try
            {
                MailStoreModel store = _mailService?.LoadStore();
                if (store?.Messages == null)
                    return result;

                foreach (MailMessageModel message in store.Messages.OrderByDescending(item => item.DateUtc))
                {
                    if (!Contains(message.Subject, query)
                        && !Contains(message.From, query)
                        && !Contains(message.To, query)
                        && !Contains(message.BodyText, query))
                    {
                        continue;
                    }

                    result.Add(new GlobalSearchHit
                    {
                        Kind = GlobalSearchKind.Mail,
                        Title = string.IsNullOrWhiteSpace(message.Subject) ? message.From : message.Subject,
                        Detail = (message.From ?? string.Empty) + " В· " + message.DateUtc.ToLocalTime().ToString("g"),
                        TabIndex = 13,
                        Filter = query,
                    });
                    if (result.Count >= MaxMail)
                        break;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private List<GlobalSearchHit> SearchTelegram(string query)
        {
            var result = new List<GlobalSearchHit>();
            try
            {
                IReadOnlyList<TelegramChatModel> chats = _telegramService?.GetChats();
                if (chats == null)
                    return result;

                foreach (TelegramChatModel chat in chats)
                {
                    if (!Contains(chat.Title, query) && !Contains(chat.LastMessagePreview, query))
                        continue;
                    result.Add(new GlobalSearchHit
                    {
                        Kind = GlobalSearchKind.Telegram,
                        Title = chat.Title,
                        Detail = chat.LastMessagePreview ?? string.Empty,
                        TabIndex = 17,
                        Filter = query,
                    });
                    if (result.Count >= MaxTelegram)
                        break;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private List<GlobalSearchHit> SearchRss(NoteRepositoryModel repository, string query)
        {
            var result = new List<GlobalSearchHit>();
            try
            {
                if (repository.RssFeeds == null || repository.RssFeeds.Count == 0)
                    return result;

                IReadOnlyList<RssCachedArticleModel> articles = _rssArticleCacheService
                    ?.LoadArticles(repository.RssFeeds.Select(feed => feed.Id));
                if (articles == null)
                    return result;

                foreach (RssCachedArticleModel article in articles)
                {
                    if (!Contains(article.Title, query) && !Contains(article.Summary, query))
                        continue;
                    result.Add(new GlobalSearchHit
                    {
                        Kind = GlobalSearchKind.Rss,
                        Title = article.Title,
                        Detail = Snippet(article.Summary, query, 140),
                        TabIndex = 18,
                        Filter = query,
                    });
                    if (result.Count >= MaxRss)
                        break;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private List<GlobalSearchHit> SearchPasswords(string query)
        {
            var result = new List<GlobalSearchHit>();
            try
            {
                if (_passwordVaultService?.IsUnlocked != true)
                    return result;

                foreach (PasswordEntryModel entry in _passwordVaultService.Vault?.Entries ?? new List<PasswordEntryModel>())
                {
                    bool match = Contains(entry.Name, query)
                        || Contains(entry.Username, query)
                        || Contains(entry.Notes, query)
                        || (entry.Uris != null && entry.Uris.Any(uri => Contains(uri, query)));
                    if (!match)
                        continue;

                    result.Add(new GlobalSearchHit
                    {
                        Kind = GlobalSearchKind.Password,
                        Title = entry.Name,
                        Detail = entry.Username ?? string.Empty,
                        TabIndex = 16,
                        Filter = query,
                    });
                    if (result.Count >= MaxPasswords)
                        break;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private static bool Contains(string text, string query)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            string line = text.Split('\n')[0].Trim();
            return line.Length > 90 ? line.Substring(0, 90).TrimEnd() : line;
        }

        private static string Snippet(string text, string query, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            int index = flat.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index > 40)
                flat = "вЂ¦" + flat.Substring(index - 20);

            return flat.Length > maxLength ? flat.Substring(0, maxLength).TrimEnd() + "вЂ¦" : flat;
        }
    }

    /// <summary>Source of a global search hit.</summary>
    public enum GlobalSearchKind
    {
        /// <summary>A note.</summary>
        Note,

        /// <summary>A clipboard item.</summary>
        Clip,

        /// <summary>A bookmark.</summary>
        Bookmark,

        /// <summary>A mail message.</summary>
        Mail,

        /// <summary>A Telegram chat.</summary>
        Telegram,

        /// <summary>An RSS article.</summary>
        Rss,

        /// <summary>A password vault entry.</summary>
        Password,
    }

    /// <summary>A single global search result.</summary>
    public class GlobalSearchHit
    {
        /// <summary>Gets or sets the source of the hit.</summary>
        public GlobalSearchKind Kind { get; set; }

        /// <summary>Gets or sets the title shown in the list.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the detail line shown under the title.</summary>
        public string Detail { get; set; }

        /// <summary>Gets or sets the direct route for notes, or null.</summary>
        public string Route { get; set; }

        /// <summary>Gets or sets the tab which shows the hit.</summary>
        public int TabIndex { get; set; }

        /// <summary>Gets or sets the filter text applied when the tab is opened.</summary>
        public string Filter { get; set; }
    }

    /// <summary>A group of hits belonging to the same source.</summary>
    public class GlobalSearchGroup
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalSearchGroup"/> class.
        /// </summary>
        public GlobalSearchGroup(string titleKey, int tabIndex, List<GlobalSearchHit> hits)
        {
            TitleKey = titleKey;
            TabIndex = tabIndex;
            Hits = hits;
        }

        /// <summary>Gets the localization key of the group title.</summary>
        public string TitleKey { get; }

        /// <summary>Gets the tab which shows all hits of the group.</summary>
        public int TabIndex { get; }

        /// <summary>Gets the hits of the group.</summary>
        public List<GlobalSearchHit> Hits { get; }
    }
}
