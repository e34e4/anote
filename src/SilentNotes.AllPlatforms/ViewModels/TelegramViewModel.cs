// Copyright © 2026 aNote.
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

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the TG tab — a privacy-oriented multi-account Telegram client.
    /// </summary>
    public class TelegramViewModel : ViewModelBase
    {
        private readonly ITelegramService _telegramService;
        private readonly IFilePickerService _filePickerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly INativeBrowserService _nativeBrowserService;
        private readonly IQrService _qrService;
        private readonly IClipboardService _clipboardService;
        private readonly IWebdavQuickSaveService _webdavQuickSave;

        private TelegramStoreModel _store;
        private TelegramAuthState _authState = TelegramAuthState.LoggedOut;
        private string _statusMessage = string.Empty;
        private bool _isBusy;
        private bool _isAccountsMenuOpen;
        private bool _isApiSetupOpen;
        private string _filter = string.Empty;
        private string _composerText = string.Empty;
        private string _openChatKey;
        private string _openChatTitle = string.Empty;
        private bool _openChatIsBot;
        private bool _openChatHasBotApp;
        private string _openChatBotAppLabel = string.Empty;

        private string _apiIdText = string.Empty;
        private string _apiHash = string.Empty;
        private string _loginPhone = string.Empty;
        private string _loginCode = string.Empty;
        private string _loginPassword = string.Empty;
        private string _codeDeliveryHint = string.Empty;
        private string _qrLoginImageDataUrl = string.Empty;
        private string _lastRenderedQrUrl = string.Empty;
        private bool _isArchiveFolderOpen;
        private readonly HashSet<string> _selectedChatIds = new HashSet<string>(StringComparer.Ordinal);
        private bool _isLoadingOlder;
        private bool _hasMoreMessages;
        private bool _isForwardPickerOpen;
        private int _forwardMessageId;
        private bool _forwardDropAuthor;
        private int _editingMessageId;
        private int _replyToMsgId;
        private string _replyPreview = string.Empty;
        private bool _sendWithSound;

        private readonly List<TelegramChatModel> _chats = new List<TelegramChatModel>();
        private readonly List<TelegramMessageModel> _messages = new List<TelegramMessageModel>();
        private readonly List<(string FileName, byte[] Content, bool IsPhoto)> _pendingAttachments =
            new List<(string FileName, byte[] Content, bool IsPhoto)>();

        public TelegramViewModel(
            ITelegramService telegramService,
            IFilePickerService filePickerService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            INativeBrowserService nativeBrowserService = null,
            IQrService qrService = null,
            IClipboardService clipboardService = null,
            IWebdavQuickSaveService webdavQuickSave = null)
        {
            _telegramService = telegramService;
            _filePickerService = filePickerService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _nativeBrowserService = nativeBrowserService;
            _qrService = qrService;
            _clipboardService = clipboardService;
            _webdavQuickSave = webdavQuickSave;

            _store = _telegramService?.LoadStore() ?? new TelegramStoreModel();
            _apiIdText = _store.ApiId > 0 ? _store.ApiId.ToString() : string.Empty;
            _apiHash = _store.ApiHash ?? string.Empty;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            OpenAccountsCommand = new RelayCommand(() => IsAccountsMenuOpen = !IsAccountsMenuOpen);
            CloseOverlayCommand = new RelayCommand(CloseOverlay);
            AddAccountCommand = new AsyncRelayCommand(AddAccountAsync);
            StartQrLoginCommand = new AsyncRelayCommand(StartQrLoginAsync);
            SaveApiCredentialsCommand = new AsyncRelayCommand(SaveApiCredentialsAsync);
            ContinueLoginCommand = new AsyncRelayCommand(ContinueLoginAsync);
            CancelLoginCommand = new AsyncRelayCommand(CancelLoginAsync);
            ResendCodeCommand = new AsyncRelayCommand(ResendCodeAsync);
            CloseChatCommand = new RelayCommand(CloseChat);
            SendCommand = new AsyncRelayCommand(SendAsync);
            AttachFileCommand = new AsyncRelayCommand(AttachFileAsync);
            AttachPhotoCommand = new AsyncRelayCommand(AttachPhotoAsync);
            ShowApiSetupCommand = new RelayCommand(() =>
            {
                IsApiSetupOpen = true;
                NotifyChanged();
            });

            if (_telegramService != null)
            {
                _telegramService.EnsureDirectories();
                _telegramService.ChatsChanged += OnServiceChatsChanged;
                _telegramService.MessagesChanged += OnServiceMessagesChanged;
                _telegramService.AuthChanged += OnServiceAuthChanged;
                _telegramService.MentionOrReplyReceived += OnMentionOrReply;
            }
        }

        public event Action StateChanged;

        public ICommand RefreshCommand { get; }
        public ICommand OpenAccountsCommand { get; }
        public ICommand CloseOverlayCommand { get; }
        public ICommand AddAccountCommand { get; }
        public ICommand StartQrLoginCommand { get; }
        public ICommand SaveApiCredentialsCommand { get; }
        public ICommand ContinueLoginCommand { get; }
        public ICommand CancelLoginCommand { get; }
        public ICommand ResendCodeCommand { get; }
        public ICommand CloseChatCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand AttachFileCommand { get; }
        public ICommand AttachPhotoCommand { get; }
        public ICommand ShowApiSetupCommand { get; }

        public IReadOnlyList<TelegramAccountModel> Accounts => _store.Accounts;

        public bool HasAccounts => _store.Accounts.Count > 0;

        public bool HasApiCredentials => _store.ApiId > 0 && !string.IsNullOrWhiteSpace(_store.ApiHash);

        public string ActiveAccountId => _store.ActiveAccountId ?? string.Empty;

        public string ActiveAccountLabel
        {
            get
            {
                TelegramAccountModel account = _store.Accounts.FirstOrDefault(a => a.Id == ActiveAccountId);
                if (account == null)
                    return _languageService?["tg_no_account"] ?? "No account";
                if (!string.IsNullOrWhiteSpace(account.DisplayName))
                    return account.DisplayName;
                if (!string.IsNullOrWhiteSpace(account.Username))
                    return "@" + account.Username;
                return account.Phone ?? account.Id;
            }
        }

        public TelegramAuthState AuthState
        {
            get => _authState;
            private set
            {
                if (SetProperty(ref _authState, value))
                    NotifyChanged();
            }
        }

        public bool NeedsApiCredentials => !HasApiCredentials || AuthState == TelegramAuthState.NeedApiCredentials || IsApiSetupOpen;

        public bool IsLoginFlow =>
            AuthState == TelegramAuthState.NeedPhone
            || AuthState == TelegramAuthState.NeedCode
            || AuthState == TelegramAuthState.NeedPassword
            || AuthState == TelegramAuthState.NeedQr
            || AuthState == TelegramAuthState.Connecting;

        public string QrLoginImageDataUrl => _qrLoginImageDataUrl;

        public bool ShowChatList => HasAccounts && AuthState == TelegramAuthState.Ready && !IsChatOpen && !IsLoginFlow && !NeedsApiCredentials;

        public bool IsChatOpen => !string.IsNullOrEmpty(_openChatKey);

        public string OpenChatTitle => _openChatTitle;

        public bool OpenChatIsBot => _openChatIsBot;

        public bool OpenChatHasBotApp => _openChatHasBotApp;

        public string OpenChatBotAppLabel =>
            string.IsNullOrWhiteSpace(_openChatBotAppLabel)
                ? (_languageService?["tg_open_app"] ?? "Open")
                : _openChatBotAppLabel;

        public string OpenChatKeyIfAny => _openChatKey;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    NotifyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value ?? string.Empty);
        }

        public bool IsAccountsMenuOpen
        {
            get => _isAccountsMenuOpen;
            set
            {
                if (SetProperty(ref _isAccountsMenuOpen, value))
                    NotifyChanged();
            }
        }

        public bool IsApiSetupOpen
        {
            get => _isApiSetupOpen;
            set => SetProperty(ref _isApiSetupOpen, value);
        }

        // Login flow is NOT an overlay for back-key purposes — cancel only via the Cancel button.
        public bool IsAnyOverlayOpen => IsAccountsMenuOpen || IsApiSetupOpen;

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value ?? string.Empty))
                    NotifyChanged();
            }
        }

        public string ComposerText
        {
            get => _composerText;
            set => SetProperty(ref _composerText, value ?? string.Empty);
        }

        public string ApiIdText
        {
            get => _apiIdText;
            set => SetProperty(ref _apiIdText, value ?? string.Empty);
        }

        public string ApiHash
        {
            get => _apiHash;
            set => SetProperty(ref _apiHash, value ?? string.Empty);
        }

        public string LoginPhone
        {
            get => _loginPhone;
            set => SetProperty(ref _loginPhone, value ?? string.Empty);
        }

        public string LoginCode
        {
            get => _loginCode;
            set => SetProperty(ref _loginCode, value ?? string.Empty);
        }

        public string LoginPassword
        {
            get => _loginPassword;
            set => SetProperty(ref _loginPassword, value ?? string.Empty);
        }

        public string CodeDeliveryHint
        {
            get => _codeDeliveryHint;
            private set => SetProperty(ref _codeDeliveryHint, value ?? string.Empty);
        }

        public bool IsArchiveFolderOpen
        {
            get => _isArchiveFolderOpen;
            private set
            {
                if (SetProperty(ref _isArchiveFolderOpen, value))
                    NotifyChanged();
            }
        }

        public int ArchivedChatCount => _chats.Count(c => c.IsArchived);

        public int ArchivedUnreadCount => _chats.Where(c => c.IsArchived).Sum(c => Math.Max(0, c.UnreadCount));

        public bool ShowArchivedFolderRow => ShowChatList && !IsArchiveFolderOpen && ArchivedChatCount > 0;

        public int SelectedChatCount => _selectedChatIds.Count;

        public bool HasChatSelection => _selectedChatIds.Count > 0;

        public IReadOnlyList<TelegramChatModel> FilteredChats
        {
            get
            {
                string filter = (_filter ?? string.Empty).Trim();
                IEnumerable<TelegramChatModel> query = _chats.Where(c => c.IsArchived == _isArchiveFolderOpen);
                if (!string.IsNullOrEmpty(filter))
                {
                    query = query.Where(c =>
                        (c.Title ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || (c.LastMessagePreview ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                // Guard against duplicate keys crashing Blazor (@key="chat.Id").
                return query
                    .GroupBy(c => c.Id, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderByDescending(c => c.IsPinned)
                    .ThenByDescending(c => c.LastMessageAt)
                    .ToList();
            }
        }

        public bool SelectionHasPinned =>
            _selectedChatIds.Any(id => _chats.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal) && c.IsPinned));

        public bool SelectionHasUnpinned =>
            _selectedChatIds.Any(id => _chats.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal) && !c.IsPinned));

        public bool HasMoreMessages => _hasMoreMessages;

        public bool IsLoadingOlder => _isLoadingOlder;

        public bool IsForwardPickerOpen => _isForwardPickerOpen;

        public bool IsEditingMessage => _editingMessageId > 0;

        public bool IsReplying => _replyToMsgId > 0;

        public string ReplyPreview => _replyPreview;

        /// <summary>When true, this send uses notification sound; default is silent.</summary>
        public bool SendWithSound
        {
            get => _sendWithSound;
            set
            {
                if (SetProperty(ref _sendWithSound, value))
                    NotifyChanged();
            }
        }

        public IReadOnlyList<TelegramChatModel> ForwardTargets
        {
            get
            {
                return _chats
                    .GroupBy(c => c.Id, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderByDescending(c => c.LastMessageAt)
                    .ToList();
            }
        }

        public bool IsChatSelected(string chatId)
            => !string.IsNullOrEmpty(chatId) && _selectedChatIds.Contains(chatId);

        public void ToggleChatSelection(string chatId)
        {
            if (string.IsNullOrEmpty(chatId))
                return;
            if (!_selectedChatIds.Add(chatId))
                _selectedChatIds.Remove(chatId);
            NotifyChanged();
        }

        public void ClearChatSelection()
        {
            if (_selectedChatIds.Count == 0)
                return;
            _selectedChatIds.Clear();
            NotifyChanged();
        }

        public void OpenArchiveFolder()
        {
            ClearChatSelection();
            IsArchiveFolderOpen = true;
        }

        public void CloseArchiveFolder()
        {
            ClearChatSelection();
            IsArchiveFolderOpen = false;
        }

        public IReadOnlyList<TelegramMessageModel> Messages => _messages;

        public IReadOnlyList<(string FileName, byte[] Content, bool IsPhoto)> PendingAttachments => _pendingAttachments;

        public bool CanSend =>
            !IsBusy
            && IsChatOpen
            && (!string.IsNullOrWhiteSpace(_composerText) || _pendingAttachments.Count > 0);

        public bool HasNoChats =>
            ShowChatList
            && FilteredChats.Count == 0
            && string.IsNullOrWhiteSpace(_filter)
            && (IsArchiveFolderOpen || ArchivedChatCount == 0);

        public bool HasNoMatches => ShowChatList && FilteredChats.Count == 0 && !string.IsNullOrWhiteSpace(_filter);

        /// <summary>Initializes the tab: reconnects the active account when possible.</summary>
        public async Task InitializeAsync()
        {
            _store = _telegramService?.LoadStore() ?? new TelegramStoreModel();
            _apiIdText = _store.ApiId > 0 ? _store.ApiId.ToString() : string.Empty;
            _apiHash = _store.ApiHash ?? string.Empty;
            _telegramService?.EnsureDirectories();

            // Prefer an in-progress login over wiping the UI (e.g. after process pause).
            TelegramAuthState pending = _telegramService?.GetAuthState() ?? TelegramAuthState.LoggedOut;
            if (pending == TelegramAuthState.NeedPhone
                || pending == TelegramAuthState.NeedCode
                || pending == TelegramAuthState.NeedPassword
                || pending == TelegramAuthState.NeedQr
                || pending == TelegramAuthState.Connecting)
            {
                AuthState = pending;
                if (!string.IsNullOrWhiteSpace(_telegramService.LastCodeDeliveryHint))
                    CodeDeliveryHint = _telegramService.LastCodeDeliveryHint;
                _ = RefreshQrImageAsync();
                NotifyChanged();
                return;
            }

            if (!HasApiCredentials)
            {
                AuthState = TelegramAuthState.NeedApiCredentials;
                IsApiSetupOpen = true;
                NotifyChanged();
                return;
            }

            if (!HasAccounts)
            {
                AuthState = TelegramAuthState.LoggedOut;
                NotifyChanged();
                return;
            }

            string accountId = ActiveAccountId;
            if (string.IsNullOrEmpty(accountId))
                accountId = _store.Accounts[0].Id;

            await ConnectAndLoadAsync(accountId);
        }

        public async Task SwitchAccountAsync(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || _telegramService == null)
                return;

            IsAccountsMenuOpen = false;
            CloseChat();
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult switchResult = await _telegramService.SwitchActiveAccountAsync(accountId);
                if (!switchResult.Success)
                {
                    StatusMessage = switchResult.ErrorMessage ?? "Switch failed";
                    return;
                }

                _store = _telegramService.LoadStore();
                await ConnectAndLoadAsync(accountId);
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public async Task RemoveAccountAsync(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || _telegramService == null)
                return;

            IsBusy = true;
            try
            {
                await _telegramService.RemoveAccountAsync(accountId);
                _store = _telegramService.LoadStore();
                CloseChat();
                _chats.Clear();
                if (_store.Accounts.Count > 0)
                {
                    string next = _store.ActiveAccountId;
                    if (string.IsNullOrEmpty(next))
                        next = _store.Accounts[0].Id;
                    await ConnectAndLoadAsync(next);
                }
                else
                {
                    AuthState = TelegramAuthState.LoggedOut;
                }
            }
            finally
            {
                IsBusy = false;
                IsAccountsMenuOpen = false;
                NotifyChanged();
            }
        }

        public async Task OpenChatAsync(TelegramChatModel chat)
        {
            if (chat == null || _telegramService == null)
                return;

            // If chats are selected, first tap clears selection instead of opening.
            if (_selectedChatIds.Count > 0)
            {
                ClearChatSelection();
                return;
            }

            IsAccountsMenuOpen = false;
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.OpenChatAsync(chat.Id);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Open chat failed";
                    return;
                }

                ApplyOpenedChat(chat);
                ReloadMessages();
                // Read receipts only when the user opens/interacts with the chat.
                await _telegramService.MarkChatReadOnInteractAsync(chat.Id);
                chat.UnreadCount = 0;
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        /// <summary>
        /// Opens a Telegram deep link (t.me / tg:// / Mini App path) in the TG tab.
        /// </summary>
        public async Task HandleTelegramLinkAsync(string linkOrPath)
        {
            if (_telegramService == null || string.IsNullOrWhiteSpace(linkOrPath))
                return;

            IsAccountsMenuOpen = false;
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.OpenTelegramLinkAsync(linkOrPath);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Could not open Telegram link";
                    return;
                }

                string chatKey = result.ChatKey;
                if (string.IsNullOrEmpty(chatKey))
                    return;

                _chats.Clear();
                _chats.AddRange(_telegramService.GetChats());
                TelegramChatModel chat = _chats.FirstOrDefault(c => string.Equals(c.Id, chatKey, StringComparison.Ordinal));
                if (chat == null)
                {
                    chat = new TelegramChatModel { Id = chatKey, Title = chatKey };
                }

                ApplyOpenedChat(chat);
                ReloadMessages();
                await _telegramService.MarkChatReadOnInteractAsync(chatKey);
                chat.UnreadCount = 0;
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private void ApplyOpenedChat(TelegramChatModel chat)
        {
            _openChatKey = chat.Id;
            _openChatTitle = chat.Title ?? string.Empty;
            _openChatIsBot = chat.IsBot;
            _openChatHasBotApp = chat.HasBotApp;
            _openChatBotAppLabel = chat.BotAppLabel ?? string.Empty;
            _editingMessageId = 0;
            CancelReply();
            _sendWithSound = false;
            CancelForwardPicker();
        }

        public async Task LoadOlderMessagesAsync()
        {
            if (_telegramService == null || string.IsNullOrEmpty(_openChatKey) || _isLoadingOlder || !_hasMoreMessages)
                return;

            _isLoadingOlder = true;
            NotifyChanged();
            try
            {
                TelegramOperationResult result = await _telegramService.LoadOlderMessagesAsync(_openChatKey);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Could not load older messages";
                ReloadMessages();
            }
            finally
            {
                _isLoadingOlder = false;
                NotifyChanged();
            }
        }

        public void BeginForward(TelegramMessageModel message, bool dropAuthor)
        {
            if (message == null)
                return;
            _forwardMessageId = message.Id;
            _forwardDropAuthor = dropAuthor;
            _isForwardPickerOpen = true;
            NotifyChanged();
        }

        public void CancelForwardPicker()
        {
            _isForwardPickerOpen = false;
            _forwardMessageId = 0;
            NotifyChanged();
        }

        public async Task CompleteForwardAsync(string toChatKey)
        {
            if (_telegramService == null || string.IsNullOrEmpty(_openChatKey) || _forwardMessageId <= 0 || string.IsNullOrEmpty(toChatKey))
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.ForwardMessageAsync(
                    _openChatKey,
                    _forwardMessageId,
                    toChatKey,
                    _forwardDropAuthor);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Forward failed";
                else
                    StatusMessage = _languageService?["tg_forwarded"] ?? "Forwarded.";
                CancelForwardPicker();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public async Task CopyMessageTextAsync(TelegramMessageModel message)
        {
            if (message == null || _clipboardService == null)
                return;
            string text = message.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                text = message.MediaLabel ?? string.Empty;
            await _clipboardService.SetTextAsync(text);
            StatusMessage = _languageService?["tg_copied"] ?? "Copied.";
            NotifyChanged();
        }

        public async Task SaveMessageToWebdavAsync(TelegramMessageModel message)
        {
            if (message == null || _webdavQuickSave == null)
                return;
            if (!_webdavQuickSave.IsConfigured)
            {
                StatusMessage = _languageService?["tg_webdav_missing"] ?? "WebDAV is not configured (File tab).";
                NotifyChanged();
                return;
            }

            IsBusy = true;
            try
            {
                string text = message.Text ?? string.Empty;
                if (string.IsNullOrEmpty(text))
                    text = message.MediaLabel ?? string.Empty;
                bool ok = await _webdavQuickSave.SaveTextAsync("tg", text);
                StatusMessage = ok
                    ? (_languageService?["tg_webdav_saved"] ?? "Saved to WebDAV: ") + (_webdavQuickSave.LastFileName ?? string.Empty)
                    : (_webdavQuickSave.LastError ?? "WebDAV save failed");
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public async Task DeleteMessageAsync(TelegramMessageModel message)
        {
            if (message == null || _telegramService == null || string.IsNullOrEmpty(_openChatKey))
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.DeleteMessageForMeAsync(_openChatKey, message.Id);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Delete failed";
                ReloadMessages();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public void BeginEditMessage(TelegramMessageModel message)
        {
            if (message == null || !message.IsOutgoing)
                return;
            CancelReply();
            _editingMessageId = message.Id;
            _composerText = message.Text ?? string.Empty;
            NotifyChanged();
        }

        public void CancelEditMessage()
        {
            _editingMessageId = 0;
            NotifyChanged();
        }

        public void BeginReply(TelegramMessageModel message)
        {
            if (message == null)
                return;
            CancelEditMessage();
            _replyToMsgId = message.Id;
            string preview = message.Text;
            if (string.IsNullOrWhiteSpace(preview))
                preview = message.MediaLabel;
            if (string.IsNullOrWhiteSpace(preview))
                preview = "#" + message.Id;
            string who = message.IsOutgoing
                ? (_languageService?["tg_you"] ?? "You")
                : (string.IsNullOrEmpty(message.FromName) ? string.Empty : message.FromName);
            _replyPreview = string.IsNullOrEmpty(who) ? preview : (who + ": " + preview);
            if (_replyPreview.Length > 120)
                _replyPreview = _replyPreview.Substring(0, 117) + "…";
            NotifyChanged();
        }

        public void CancelReply()
        {
            if (_replyToMsgId == 0 && string.IsNullOrEmpty(_replyPreview))
                return;
            _replyToMsgId = 0;
            _replyPreview = string.Empty;
            NotifyChanged();
        }

        public async Task ToggleLikeAsync(TelegramMessageModel message)
        {
            if (message == null || _telegramService == null || string.IsNullOrEmpty(_openChatKey))
                return;

            try
            {
                TelegramOperationResult result = await _telegramService.ToggleLikeReactionAsync(_openChatKey, message.Id);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Reaction failed";
                ReloadMessages();
            }
            finally
            {
                NotifyChanged();
            }
        }

        public async Task ArchiveSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.SetChatsArchivedAsync(keys, archived: !_isArchiveFolderOpen));
        }

        public async Task DeleteSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.DeleteChatsForMeAsync(keys));
        }

        public async Task MuteSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.SetChatsMutedAsync(keys, muted: true));
        }

        public async Task UnmuteSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.SetChatsMutedAsync(keys, muted: false));
        }

        public async Task ReadSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.MarkChatsReadAsync(keys));
        }

        public async Task PinSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.SetChatsPinnedAsync(keys, pinned: true));
        }

        public async Task UnpinSelectedChatsAsync()
        {
            await RunSelectionActionAsync(keys => _telegramService.SetChatsPinnedAsync(keys, pinned: false));
        }

        public async Task OpenBotAppAsync(TelegramChatModel chat)
        {
            if (chat == null || !chat.IsBot || _telegramService == null)
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.OpenBotMainAppAsync(chat.Id);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Could not open bot app";
                    return;
                }

                chat.HasBotApp = true;
                if (string.Equals(_openChatKey, chat.Id, StringComparison.Ordinal))
                {
                    _openChatHasBotApp = true;
                    if (!string.IsNullOrWhiteSpace(chat.BotAppLabel))
                        _openChatBotAppLabel = chat.BotAppLabel;
                }

                if (!string.IsNullOrWhiteSpace(result.Url))
                    _nativeBrowserService?.OpenWebsiteInApp(result.Url);
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public Task OpenCurrentBotAppAsync()
        {
            if (string.IsNullOrEmpty(_openChatKey))
                return Task.CompletedTask;

            TelegramChatModel chat = _chats.FirstOrDefault(c => string.Equals(c.Id, _openChatKey, StringComparison.Ordinal));
            if (chat == null)
            {
                chat = new TelegramChatModel
                {
                    Id = _openChatKey,
                    IsBot = true,
                    HasBotApp = _openChatHasBotApp,
                    BotAppLabel = _openChatBotAppLabel,
                    Title = _openChatTitle,
                };
            }

            return OpenBotAppAsync(chat);
        }

        private async Task RunSelectionActionAsync(Func<IReadOnlyList<string>, Task<TelegramOperationResult>> action)
        {
            if (_telegramService == null || _selectedChatIds.Count == 0)
                return;

            var keys = _selectedChatIds.ToList();
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await action(keys);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Action failed";
                ClearChatSelection();
                await RefreshChatsQuietAsync();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public void CloseChat()
        {
            _telegramService?.CloseChat();
            _openChatKey = null;
            _openChatIsBot = false;
            _openChatHasBotApp = false;
            _openChatBotAppLabel = string.Empty;
            _editingMessageId = 0;
            _hasMoreMessages = false;
            CancelReply();
            _sendWithSound = false;
            CancelForwardPicker();
            _openChatTitle = string.Empty;
            _messages.Clear();
            _pendingAttachments.Clear();
            _composerText = string.Empty;
            NotifyChanged();
        }

        public void CloseOverlay()
        {
            if (IsAccountsMenuOpen)
            {
                IsAccountsMenuOpen = false;
                return;
            }

            if (IsApiSetupOpen && HasApiCredentials)
            {
                IsApiSetupOpen = false;
                NotifyChanged();
                return;
            }

            if (IsChatOpen)
                CloseChat();
        }

        /// <summary>Re-reads pending login / connection state from the singleton service (after resume).</summary>
        public void SyncFromService()
        {
            if (_telegramService == null)
                return;

            _store = _telegramService.LoadStore() ?? _store;
            TelegramAuthState pending = _telegramService.GetAuthState();
            if (pending == TelegramAuthState.NeedPhone
                || pending == TelegramAuthState.NeedCode
                || pending == TelegramAuthState.NeedPassword
                || pending == TelegramAuthState.NeedQr
                || pending == TelegramAuthState.Connecting
                || pending == TelegramAuthState.NeedApiCredentials)
            {
                AuthState = pending;
                if (!string.IsNullOrWhiteSpace(_telegramService.LastCodeDeliveryHint))
                    CodeDeliveryHint = _telegramService.LastCodeDeliveryHint;
                _ = RefreshQrImageAsync();
            }
            else if (!string.IsNullOrEmpty(ActiveAccountId))
            {
                AuthState = _telegramService.GetAuthState(ActiveAccountId);
            }

            NotifyChanged();
        }

        public void OpenOfficialTelegram()
        {
            if (AuthState == TelegramAuthState.NeedQr)
                _telegramService?.OpenOfficialTelegramDevices();
            else
                _telegramService?.OpenOfficialTelegramApp();
        }

        public void WrapComposerFormat(string prefix, string suffix = null)
        {
            suffix ??= prefix;
            string text = _composerText ?? string.Empty;
            _composerText = prefix + text + suffix;
            NotifyChanged();
        }

        public void InsertComposerSnippet(string snippet)
        {
            _composerText = (_composerText ?? string.Empty) + snippet;
            NotifyChanged();
        }

        public void ClearPendingAttachments()
        {
            _pendingAttachments.Clear();
            NotifyChanged();
        }

        public void RemovePendingAttachment(int index)
        {
            if (index < 0 || index >= _pendingAttachments.Count)
                return;
            _pendingAttachments.RemoveAt(index);
            NotifyChanged();
        }

        public async Task DownloadMediaAsync(TelegramMessageModel message)
        {
            if (message == null || !message.HasMedia || _telegramService == null)
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                var mediaRef = new TelegramMediaRef
                {
                    AccountId = ActiveAccountId,
                    ChatKey = message.ChatKey,
                    MessageId = message.Id,
                    MediaKind = message.MediaKind,
                    FileName = message.MediaFileName,
                };
                TelegramOperationResult result = await _telegramService.DownloadMediaAsync(mediaRef);
                if (result.Success)
                    StatusMessage = string.Format(_languageService?["tg_downloaded"] ?? "Saved: {0}", result.LocalPath ?? string.Empty);
                else
                    StatusMessage = result.ErrorMessage ?? "Download failed";
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public async Task ClickBotButtonAsync(TelegramBotButtonModel button)
        {
            if (button == null || !IsChatOpen || _telegramService == null)
                return;

            IsBusy = true;
            try
            {
                TelegramOperationResult result = await _telegramService.ClickBotButtonAsync(_openChatKey, button);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Button failed";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.Url))
                    _nativeBrowserService?.OpenWebsite(result.Url);

                ReloadMessages();
                await RefreshChatsQuietAsync();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task ConnectAndLoadAsync(string accountId)
        {
            if (_telegramService == null || string.IsNullOrEmpty(accountId))
                return;

            IsBusy = true;
            AuthState = TelegramAuthState.Connecting;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.ConnectAccountAsync(accountId);
                AuthState = result.AuthState;
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Connect failed";
                    return;
                }

                _store = _telegramService.LoadStore();
                await RefreshChatsQuietAsync();
                AuthState = TelegramAuthState.Ready;
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task RefreshAsync()
        {
            if (_telegramService == null)
                return;

            IsBusy = true;
            try
            {
                if (IsChatOpen)
                {
                    await _telegramService.OpenChatAsync(_openChatKey);
                    ReloadMessages();
                }

                await RefreshChatsQuietAsync();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task RefreshChatsQuietAsync()
        {
            if (_telegramService == null)
                return;

            TelegramOperationResult result = await _telegramService.RefreshChatsAsync();
            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                StatusMessage = result.ErrorMessage;

            _chats.Clear();
            _chats.AddRange(_telegramService.GetChats());
        }

        private void ReloadMessages()
        {
            _messages.Clear();
            if (_telegramService != null && !string.IsNullOrEmpty(_openChatKey))
            {
                _messages.AddRange(
                    _telegramService.GetMessages(_openChatKey)
                        .GroupBy(m => m.Id)
                        .Select(g => g.First()));
                _hasMoreMessages = _telegramService.HasMoreMessages(_openChatKey);
            }
            else
            {
                _hasMoreMessages = false;
            }
        }

        private async Task SaveApiCredentialsAsync()
        {
            if (_telegramService == null)
                return;

            if (!int.TryParse((_apiIdText ?? string.Empty).Trim(), out int apiId) || apiId <= 0)
            {
                StatusMessage = _languageService?["tg_api_id_invalid"] ?? "Enter a valid api_id from my.telegram.org";
                NotifyChanged();
                return;
            }

            if (string.IsNullOrWhiteSpace(_apiHash))
            {
                StatusMessage = _languageService?["tg_api_hash_required"] ?? "Enter api_hash from my.telegram.org";
                NotifyChanged();
                return;
            }

            _store.ApiId = apiId;
            _store.ApiHash = _apiHash.Trim();
            if (!_telegramService.SaveStore(_store))
            {
                StatusMessage = "Failed to save API credentials";
                NotifyChanged();
                return;
            }

            _store = _telegramService.LoadStore();
            IsApiSetupOpen = false;
            StatusMessage = string.Empty;
            AuthState = HasAccounts ? TelegramAuthState.Ready : TelegramAuthState.LoggedOut;
            NotifyChanged();
        }

        private async Task AddAccountAsync()
        {
            if (_telegramService == null)
                return;

            if (!HasApiCredentials)
            {
                IsApiSetupOpen = true;
                AuthState = TelegramAuthState.NeedApiCredentials;
                NotifyChanged();
                return;
            }

            IsAccountsMenuOpen = false;
            IsBusy = true;
            StatusMessage = string.Empty;
            _loginPhone = string.Empty;
            _loginCode = string.Empty;
            _loginPassword = string.Empty;
            ClearQrImage();
            try
            {
                TelegramOperationResult result = await _telegramService.StartAddAccountAsync();
                AuthState = result.AuthState;
                if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                    StatusMessage = result.ErrorMessage;
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task StartQrLoginAsync()
        {
            if (_telegramService == null)
                return;

            if (!HasApiCredentials)
            {
                IsApiSetupOpen = true;
                AuthState = TelegramAuthState.NeedApiCredentials;
                NotifyChanged();
                return;
            }

            IsAccountsMenuOpen = false;
            IsBusy = true;
            StatusMessage = string.Empty;
            _loginPhone = string.Empty;
            _loginCode = string.Empty;
            _loginPassword = string.Empty;
            ClearQrImage();
            try
            {
                TelegramOperationResult result = await _telegramService.StartQrLoginAsync();
                AuthState = result.AuthState;
                ApplyCodeDeliveryHint(result);
                if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                    StatusMessage = result.ErrorMessage;
                else
                    await RefreshQrImageAsync();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task ContinueLoginAsync()
        {
            if (_telegramService == null)
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                string phone = AuthState == TelegramAuthState.NeedPhone ? _loginPhone : null;
                string code = AuthState == TelegramAuthState.NeedCode ? _loginCode : null;
                string password = AuthState == TelegramAuthState.NeedPassword ? _loginPassword : null;
                TelegramOperationResult result = await _telegramService.ContinueLoginAsync(phone, code, password);
                AuthState = result.AuthState;
                ApplyCodeDeliveryHint(result);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage ?? "Login failed";
                    return;
                }

                if (result.AuthState == TelegramAuthState.Ready)
                {
                    _store = _telegramService.LoadStore();
                    await RefreshChatsQuietAsync();
                    _loginPhone = string.Empty;
                    _loginCode = string.Empty;
                    _loginPassword = string.Empty;
                    CodeDeliveryHint = string.Empty;
                }
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task ResendCodeAsync()
        {
            if (_telegramService == null)
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                TelegramOperationResult result = await _telegramService.ResendVerificationCodeAsync();
                AuthState = result.AuthState;
                ApplyCodeDeliveryHint(result);
                if (!result.Success)
                    StatusMessage = result.ErrorMessage ?? "Resend failed";
                else
                    StatusMessage = _languageService?["tg_code_resent"] ?? "Requested another delivery method.";
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private void ApplyCodeDeliveryHint(TelegramOperationResult result)
        {
            if (!string.IsNullOrWhiteSpace(result?.CodeDeliveryHint))
                CodeDeliveryHint = result.CodeDeliveryHint;
            else if (_telegramService != null && !string.IsNullOrWhiteSpace(_telegramService.LastCodeDeliveryHint))
                CodeDeliveryHint = _telegramService.LastCodeDeliveryHint;
        }

        private async Task CancelLoginAsync()
        {
            if (_telegramService == null)
                return;

            await _telegramService.CancelLoginAsync();
            AuthState = HasAccounts ? TelegramAuthState.Ready : TelegramAuthState.LoggedOut;
            StatusMessage = string.Empty;
            CodeDeliveryHint = string.Empty;
            ClearQrImage();
            NotifyChanged();
        }

        private void ClearQrImage()
        {
            _qrLoginImageDataUrl = string.Empty;
            _lastRenderedQrUrl = string.Empty;
        }

        private async Task RefreshQrImageAsync()
        {
            if (_telegramService == null || _qrService == null)
                return;

            string url = _telegramService.LastQrLoginUrl;
            if (string.IsNullOrEmpty(url) || string.Equals(url, _lastRenderedQrUrl, StringComparison.Ordinal))
                return;

            try
            {
                byte[] png = await _qrService.GenerateQrPngAsync(url, 512);
                if (png == null || png.Length == 0)
                    return;

                _lastRenderedQrUrl = url;
                _qrLoginImageDataUrl = "data:image/png;base64," + Convert.ToBase64String(png);
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        private async Task SendAsync()
        {
            if (!CanSend || _telegramService == null)
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                bool silent = !_sendWithSound;
                int replyTo = _replyToMsgId;
                bool replyConsumed = false;

                foreach (var attachment in _pendingAttachments.ToList())
                {
                    int attachReply = replyConsumed ? 0 : replyTo;
                    TelegramOperationResult attachResult = attachment.IsPhoto
                        ? await _telegramService.SendPhotoAttachmentAsync(
                            _openChatKey, attachment.Content, attachment.FileName, caption: null,
                            replyToMsgId: attachReply, silent: silent)
                        : await _telegramService.SendFileAttachmentAsync(
                            _openChatKey, attachment.Content, attachment.FileName, caption: null,
                            replyToMsgId: attachReply, silent: silent);
                    if (!attachResult.Success)
                    {
                        StatusMessage = attachResult.ErrorMessage ?? "Send attachment failed";
                        return;
                    }
                    replyConsumed = true;
                }

                _pendingAttachments.Clear();

                string text = (_composerText ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    TelegramOperationResult sendResult;
                    if (_editingMessageId > 0)
                    {
                        sendResult = await _telegramService.EditMessageTextAsync(_openChatKey, _editingMessageId, text);
                        _editingMessageId = 0;
                    }
                    else
                    {
                        sendResult = await _telegramService.SendTextMessageAsync(
                            _openChatKey,
                            text,
                            replyToMsgId: replyConsumed ? 0 : replyTo,
                            format: TelegramTextFormat.MarkdownLite,
                            silent: silent);
                        replyConsumed = true;
                    }

                    if (!sendResult.Success)
                    {
                        StatusMessage = sendResult.ErrorMessage ?? "Send failed";
                        return;
                    }

                    _composerText = string.Empty;
                }

                CancelReply();
                _sendWithSound = false;

                await _telegramService.MarkChatReadOnInteractAsync(_openChatKey);
                ReloadMessages();
                await RefreshChatsQuietAsync();
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        private async Task AttachFileAsync()
        {
            if (_filePickerService == null)
                return;

            bool picked = await _filePickerService.PickFile();
            if (!picked)
                return;

            byte[] content = await _filePickerService.ReadPickedFile();
            string name = _filePickerService.GetPickedFileName() ?? "file.bin";
            if (content == null || content.Length == 0)
                return;

            _pendingAttachments.Add((name, content, false));
            NotifyChanged();
        }

        private async Task AttachPhotoAsync()
        {
            if (_filePickerService == null)
                return;

            bool picked = await _filePickerService.PickImage();
            if (!picked)
                return;

            byte[] content = await _filePickerService.ReadPickedFile();
            string name = _filePickerService.GetPickedFileName() ?? "photo.jpg";
            if (content == null || content.Length == 0)
                return;

            _pendingAttachments.Add((name, content, true));
            NotifyChanged();
        }

        private void OnServiceChatsChanged()
        {
            try
            {
                _chats.Clear();
                if (_telegramService != null)
                    _chats.AddRange(_telegramService.GetChats());
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        private void OnServiceMessagesChanged(string chatKey)
        {
            try
            {
                if (!string.IsNullOrEmpty(_openChatKey) && string.Equals(_openChatKey, chatKey, StringComparison.Ordinal))
                    ReloadMessages();
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        private void OnServiceAuthChanged()
        {
            try
            {
                _store = _telegramService?.LoadStore() ?? _store;
                string id = ActiveAccountId;
                if (_telegramService != null)
                {
                    // Prefer pending login state while adding an account.
                    TelegramAuthState pending = _telegramService.GetAuthState();
                    if (pending == TelegramAuthState.NeedPhone
                        || pending == TelegramAuthState.NeedCode
                        || pending == TelegramAuthState.NeedPassword
                        || pending == TelegramAuthState.NeedQr
                        || pending == TelegramAuthState.Connecting
                        || pending == TelegramAuthState.NeedApiCredentials)
                    {
                        AuthState = pending;
                    }
                    else if (!string.IsNullOrEmpty(id))
                    {
                        AuthState = _telegramService.GetAuthState(id);
                    }
                    else
                    {
                        AuthState = pending;
                    }

                    if (!string.IsNullOrWhiteSpace(_telegramService.LastCodeDeliveryHint))
                        CodeDeliveryHint = _telegramService.LastCodeDeliveryHint;

                    if (pending == TelegramAuthState.NeedQr || pending == TelegramAuthState.NeedPassword)
                        _ = RefreshQrImageAsync();
                    else if (pending == TelegramAuthState.Ready || pending == TelegramAuthState.LoggedOut)
                        ClearQrImage();
                }
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        private void OnMentionOrReply(TelegramMessageModel message)
        {
            // System notification is raised by the Android service; refresh UI if visible.
            try
            {
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
