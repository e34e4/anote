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
using SilentNotes.Crypto;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Mail tab (Gmail / Thunderbird style multi-account IMAP/SMTP client).
    /// </summary>
    public class MailViewModel : ViewModelBase
    {
        private readonly IMailService _mailService;
        private readonly IFilePickerService _filePickerService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly INativeBrowserService _nativeBrowserService;
        private readonly IMessengerService _messengerService;
        private readonly ICryptoRandomService _randomService;
        private string _handledGoogleRedirect;

        private MailStoreModel _store;
        private string _selectedAccountId = string.Empty; // empty = All accounts
        private string _selectedFolder = MailFolders.Inbox;
        private string _filter = string.Empty;
        private bool _isBusy;
        private string _statusMessage;

        private MailMessageItemViewModel _openMessage;
        private bool _isComposeOpen;
        private bool _isAccountsOverlayOpen;
        private bool _isEditingAccount;

        // Compose fields
        private string _composeAccountId;
        private string _composeDraftId;
        private string _composeTo = string.Empty;
        private string _composeCc = string.Empty;
        private string _composeBcc = string.Empty;
        private bool _showCcBcc;
        private string _composeSubject = string.Empty;
        private string _composeBody = string.Empty;
        private readonly List<(string FileName, byte[] Content)> _composeAttachments = new List<(string FileName, byte[] Content)>();

        // Account editor fields
        private string _editAccountId;
        private string _editDisplayName = string.Empty;
        private string _editEmail = string.Empty;
        private string _editUsername = string.Empty;
        private string _editPassword = string.Empty;
        private string _editImapHost = string.Empty;
        private int _editImapPort = 993;
        private bool _editImapSsl = true;
        private string _editSmtpHost = string.Empty;
        private int _editSmtpPort = 465;
        private bool _editSmtpSsl = true;
        private string _editAuthMode = MailAuthModes.Password;
        private string _editRefreshToken = string.Empty;
        private string _editAccessToken = string.Empty;
        private DateTime _editAccessExpiry;

        public MailViewModel(
            IMailService mailService,
            IFilePickerService filePickerService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            INativeBrowserService nativeBrowserService = null,
            IMessengerService messengerService = null,
            ICryptoRandomService randomService = null)
        {
            _mailService = mailService;
            _filePickerService = filePickerService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _nativeBrowserService = nativeBrowserService;
            _messengerService = messengerService;
            _randomService = randomService;
            _messengerService?.Register<SilentNotes.MailGoogleOAuthRedirectMessage>(this, (_, message) =>
            {
                _ = FinishGoogleSignInAsync(message.RedirectUrl);
            });
            if (SilentNotes.MailOAuthSession.Peek() != null)
                _ = FinishGoogleSignInAsync(SilentNotes.MailOAuthSession.Peek());

            _store = _mailService?.LoadStore() ?? new MailStoreModel();
            if (_store.Accounts.Count == 1)
                _selectedAccountId = _store.Accounts[0].Id;

            SyncCommand = new AsyncRelayCommand(SyncAllOrSelectedAsync);
            StartComposeCommand = new RelayCommand(StartNewCompose);
            OpenAccountsCommand = new RelayCommand(OpenAccountsOverlay);
            CloseOverlayCommand = new RelayCommand(CloseOverlay);
        }

        public event Action StateChanged;

        public ICommand SyncCommand { get; }
        public ICommand StartComposeCommand { get; }
        public ICommand OpenAccountsCommand { get; }
        public ICommand CloseOverlayCommand { get; }

        public IReadOnlyList<MailAccountModel> Accounts => _store.Accounts;

        public bool HasAccounts => _store.Accounts.Count > 0;

        public string SelectedAccountId
        {
            get => _selectedAccountId;
            set
            {
                if (SetProperty(ref _selectedAccountId, value ?? string.Empty))
                    NotifyChanged();
            }
        }

        public string SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (SetProperty(ref _selectedFolder, value ?? MailFolders.Inbox))
                {
                    _openMessage = null;
                    NotifyChanged();
                }
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value ?? string.Empty))
                    NotifyChanged();
            }
        }

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
            private set => SetProperty(ref _statusMessage, value);
        }

        public MailMessageItemViewModel OpenMessage => _openMessage;

        public bool IsMessageOpen => _openMessage != null;

        public bool IsComposeOpen
        {
            get => _isComposeOpen;
            set => SetProperty(ref _isComposeOpen, value);
        }

        public bool IsAccountsOverlayOpen
        {
            get => _isAccountsOverlayOpen;
            set => SetProperty(ref _isAccountsOverlayOpen, value);
        }

        public bool IsEditingAccount
        {
            get => _isEditingAccount;
            set => SetProperty(ref _isEditingAccount, value);
        }

        public bool IsAnyOverlayOpen => IsComposeOpen || IsAccountsOverlayOpen || IsMessageOpen;

        // Compose properties
        public string ComposeAccountId
        {
            get => _composeAccountId;
            set => SetProperty(ref _composeAccountId, value);
        }

        public string ComposeTo
        {
            get => _composeTo;
            set => SetProperty(ref _composeTo, value);
        }

        public string ComposeCc
        {
            get => _composeCc;
            set => SetProperty(ref _composeCc, value);
        }

        public string ComposeBcc
        {
            get => _composeBcc;
            set => SetProperty(ref _composeBcc, value);
        }

        public bool ShowCcBcc
        {
            get => _showCcBcc;
            set => SetProperty(ref _showCcBcc, value);
        }

        public string ComposeSubject
        {
            get => _composeSubject;
            set => SetProperty(ref _composeSubject, value);
        }

        public string ComposeBody
        {
            get => _composeBody;
            set => SetProperty(ref _composeBody, value);
        }

        public IReadOnlyList<(string FileName, byte[] Content)> ComposeAttachments => _composeAttachments;

        // Account editor properties
        public string EditDisplayName
        {
            get => _editDisplayName;
            set => SetProperty(ref _editDisplayName, value);
        }

        public string EditEmail
        {
            get => _editEmail;
            set
            {
                if (SetProperty(ref _editEmail, value))
                    AutoFillAccountPreset();
            }
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

        public string EditImapHost
        {
            get => _editImapHost;
            set => SetProperty(ref _editImapHost, value);
        }

        public int EditImapPort
        {
            get => _editImapPort;
            set => SetProperty(ref _editImapPort, value);
        }

        public bool EditImapSsl
        {
            get => _editImapSsl;
            set => SetProperty(ref _editImapSsl, value);
        }

        public string EditSmtpHost
        {
            get => _editSmtpHost;
            set => SetProperty(ref _editSmtpHost, value);
        }

        public int EditSmtpPort
        {
            get => _editSmtpPort;
            set => SetProperty(ref _editSmtpPort, value);
        }

        public bool EditSmtpSsl
        {
            get => _editSmtpSsl;
            set => SetProperty(ref _editSmtpSsl, value);
        }

        public IReadOnlyList<MailMessageItemViewModel> FilteredMessages
        {
            get
            {
                IEnumerable<MailMessageModel> query = _store.Messages;

                if (!string.IsNullOrEmpty(_selectedAccountId))
                    query = query.Where(m => m.AccountId == _selectedAccountId);

                if (_selectedFolder == MailFolders.Starred)
                {
                    query = query.Where(m => m.IsStarred && m.Folder != MailFolders.Trash);
                }
                else if (_selectedFolder == MailFolders.All)
                {
                    query = query.Where(m => m.Folder != MailFolders.Trash);
                }
                else
                {
                    query = query.Where(m => string.Equals(m.Folder, _selectedFolder, StringComparison.OrdinalIgnoreCase));
                }

                string search = _filter?.Trim();
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(m =>
                        (m.Subject != null && m.Subject.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                        (m.From != null && m.From.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                        (m.To != null && m.To.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                        (m.BodyText != null && m.BodyText.Contains(search, StringComparison.OrdinalIgnoreCase)));
                }

                return query
                    .OrderByDescending(m => m.DateUtc)
                    .Select(m => new MailMessageItemViewModel(m, GetAccountEmail(m.AccountId)))
                    .ToList();
            }
        }

        public bool IsEmpty => FilteredMessages.Count == 0 && string.IsNullOrWhiteSpace(_filter);

        public bool HasNoMatches => FilteredMessages.Count == 0 && !string.IsNullOrWhiteSpace(_filter);

        public int GetUnreadCount(string folder)
        {
            IEnumerable<MailMessageModel> query = _store.Messages.Where(m => !m.IsRead);
            if (!string.IsNullOrEmpty(_selectedAccountId))
                query = query.Where(m => m.AccountId == _selectedAccountId);

            if (folder == MailFolders.Starred)
                return query.Count(m => m.IsStarred && m.Folder != MailFolders.Trash);
            if (folder == MailFolders.All)
                return query.Count(m => m.Folder != MailFolders.Trash);
            return query.Count(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase));
        }

        public void CloseOverlay()
        {
            if (IsEditingAccount)
            {
                IsEditingAccount = false;
                NotifyChanged();
                return;
            }
            if (IsAccountsOverlayOpen)
            {
                IsAccountsOverlayOpen = false;
                NotifyChanged();
                return;
            }
            if (IsComposeOpen)
            {
                IsComposeOpen = false;
                NotifyChanged();
                return;
            }
            if (_openMessage != null)
            {
                _openMessage = null;
                NotifyChanged();
            }
        }

        public void SelectMessage(MailMessageItemViewModel item)
        {
            if (item == null)
                return;

            if (string.Equals(item.Model.Folder, MailFolders.Drafts, StringComparison.OrdinalIgnoreCase))
            {
                OpenDraftInComposer(item.Model);
                return;
            }

            if (!item.Model.IsRead)
            {
                item.Model.IsRead = true;
                _mailService?.SaveStore(_store);
            }
            _openMessage = item;
            NotifyChanged();
        }

        public void ToggleStar(MailMessageItemViewModel item)
        {
            if (item == null)
                return;
            item.Model.IsStarred = !item.Model.IsStarred;
            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void ToggleRead(MailMessageItemViewModel item)
        {
            if (item == null)
                return;
            item.Model.IsRead = !item.Model.IsRead;
            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void MarkAllReadInCurrentFolder()
        {
            foreach (var item in FilteredMessages)
            {
                item.Model.IsRead = true;
            }
            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void DeleteOrTrashMessage(MailMessageItemViewModel item)
        {
            if (item == null)
                return;

            if (string.Equals(item.Model.Folder, MailFolders.Trash, StringComparison.OrdinalIgnoreCase))
            {
                _store.Messages.Remove(item.Model);
            }
            else
            {
                item.Model.Folder = MailFolders.Trash;
            }

            if (_openMessage?.Model.Id == item.Model.Id)
                _openMessage = null;

            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void RestoreFromTrash(MailMessageItemViewModel item)
        {
            if (item == null)
                return;
            item.Model.Folder = MailFolders.Inbox;
            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void EmptyTrash()
        {
            _store.Messages.RemoveAll(m => string.Equals(m.Folder, MailFolders.Trash, StringComparison.OrdinalIgnoreCase));
            if (_openMessage != null && string.Equals(_openMessage.Model.Folder, MailFolders.Trash, StringComparison.OrdinalIgnoreCase))
                _openMessage = null;
            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        public void StartNewCompose()
        {
            if (!HasAccounts)
            {
                OpenAccountsOverlay();
                StartAddAccount();
                return;
            }

            _composeDraftId = null;
            _composeAccountId = !string.IsNullOrEmpty(_selectedAccountId)
                ? _selectedAccountId
                : _store.Accounts[0].Id;
            _composeTo = string.Empty;
            _composeCc = string.Empty;
            _composeBcc = string.Empty;
            _showCcBcc = false;
            _composeSubject = string.Empty;
            _composeBody = string.Empty;
            _composeAttachments.Clear();
            IsComposeOpen = true;
            NotifyChanged();
        }

        public void StartReply(MailMessageItemViewModel item, bool replyAll)
        {
            if (item == null)
                return;

            _composeDraftId = null;
            _composeAccountId = !string.IsNullOrEmpty(item.Model.AccountId)
                ? item.Model.AccountId
                : (_store.Accounts.FirstOrDefault()?.Id ?? string.Empty);
            _composeTo = item.Model.From;
            _composeCc = replyAll ? item.Model.Cc : string.Empty;
            _composeBcc = string.Empty;
            _showCcBcc = replyAll && !string.IsNullOrWhiteSpace(item.Model.Cc);

            string subj = item.Model.Subject ?? string.Empty;
            _composeSubject = subj.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subj : "Re: " + subj;
            _composeBody = "\n\n--- " + item.DateFullText + " " + item.Model.From + " ---\n" +
                QuoteLines(item.PlainBodyForDisplay);
            _composeAttachments.Clear();
            IsComposeOpen = true;
            NotifyChanged();
        }

        public void StartForward(MailMessageItemViewModel item)
        {
            if (item == null)
                return;

            _composeDraftId = null;
            _composeAccountId = !string.IsNullOrEmpty(item.Model.AccountId)
                ? item.Model.AccountId
                : (_store.Accounts.FirstOrDefault()?.Id ?? string.Empty);
            _composeTo = string.Empty;
            _composeCc = string.Empty;
            _composeBcc = string.Empty;
            _showCcBcc = false;

            string subj = item.Model.Subject ?? string.Empty;
            _composeSubject = subj.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase) ? subj : "Fwd: " + subj;
            _composeBody = "\n\n---------- Forwarded message ----------\n" +
                "From: " + item.Model.From + "\n" +
                "Date: " + item.DateFullText + "\n" +
                "Subject: " + item.Model.Subject + "\n" +
                "To: " + item.Model.To + "\n\n" +
                item.PlainBodyForDisplay;
            _composeAttachments.Clear();
            IsComposeOpen = true;
            NotifyChanged();
        }

        private void OpenDraftInComposer(MailMessageModel draft)
        {
            _composeDraftId = draft.Id;
            _composeAccountId = !string.IsNullOrEmpty(draft.AccountId)
                ? draft.AccountId
                : (_store.Accounts.FirstOrDefault()?.Id ?? string.Empty);
            _composeTo = draft.To ?? string.Empty;
            _composeCc = draft.Cc ?? string.Empty;
            _composeBcc = draft.Bcc ?? string.Empty;
            _showCcBcc = !string.IsNullOrWhiteSpace(draft.Cc) || !string.IsNullOrWhiteSpace(draft.Bcc);
            _composeSubject = draft.Subject ?? string.Empty;
            _composeBody = draft.BodyText ?? string.Empty;
            _composeAttachments.Clear();
            IsComposeOpen = true;
            NotifyChanged();
        }

        public async Task AddAttachmentAsync()
        {
            if (_filePickerService == null)
                return;

            try
            {
                if (!await _filePickerService.PickFile(null, "*/*"))
                    return;

                byte[] bytes = await _filePickerService.ReadPickedFile();
                if (bytes == null || bytes.Length == 0)
                    return;

                string name = _filePickerService.GetPickedFileName();
                if (string.IsNullOrWhiteSpace(name))
                    name = "attachment_" + (_composeAttachments.Count + 1) + ".bin";

                _composeAttachments.Add((name, bytes));
                NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        public void RemoveAttachmentAt(int index)
        {
            if (index >= 0 && index < _composeAttachments.Count)
            {
                _composeAttachments.RemoveAt(index);
                NotifyChanged();
            }
        }

        public void SaveDraft()
        {
            MailAccountModel acc = GetComposeAccount();
            MailMessageModel draft = null;
            if (!string.IsNullOrEmpty(_composeDraftId))
                draft = _store.Messages.FirstOrDefault(m => m.Id == _composeDraftId);

            if (draft == null)
            {
                draft = new MailMessageModel();
                _store.Messages.Add(draft);
            }

            draft.AccountId = acc?.Id ?? string.Empty;
            draft.Folder = MailFolders.Drafts;
            draft.From = acc?.EmailAddress ?? string.Empty;
            draft.To = _composeTo?.Trim() ?? string.Empty;
            draft.Cc = _composeCc?.Trim() ?? string.Empty;
            draft.Bcc = _composeBcc?.Trim() ?? string.Empty;
            draft.Subject = string.IsNullOrWhiteSpace(_composeSubject) ? "(No subject)" : _composeSubject.Trim();
            draft.BodyText = _composeBody ?? string.Empty;
            draft.Snippet = MailServiceBase.BuildSnippet(draft.BodyText, null);
            draft.DateUtc = DateTime.UtcNow;
            draft.IsRead = true;

            _mailService?.SaveStore(_store);
            IsComposeOpen = false;
            _feedbackService?.ShowToast(_languageService["mail_draft_saved"]);
            NotifyChanged();
        }

        public async Task SendComposedMessageAsync()
        {
            if (IsBusy)
                return;

            MailAccountModel acc = GetComposeAccount();
            if (acc == null)
            {
                _feedbackService?.ShowToast(_languageService["mail_no_account"], FeedbackSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_composeTo))
            {
                _feedbackService?.ShowToast(_languageService["mail_recipient_required"], FeedbackSeverity.Warning);
                return;
            }

            var outgoing = new MailMessageModel
            {
                AccountId = acc.Id,
                Folder = MailFolders.Sent,
                From = string.IsNullOrWhiteSpace(acc.DisplayName)
                    ? acc.EmailAddress
                    : acc.DisplayName + " <" + acc.EmailAddress + ">",
                To = _composeTo.Trim(),
                Cc = _composeCc?.Trim() ?? string.Empty,
                Bcc = _composeBcc?.Trim() ?? string.Empty,
                Subject = string.IsNullOrWhiteSpace(_composeSubject) ? "(No subject)" : _composeSubject.Trim(),
                BodyText = _composeBody ?? string.Empty,
                Snippet = MailServiceBase.BuildSnippet(_composeBody, null),
                DateUtc = DateTime.UtcNow,
                IsRead = true,
                Attachments = _composeAttachments.Select(a => new MailAttachmentMetaModel
                {
                    FileName = a.FileName,
                    SizeBytes = a.Content?.Length ?? 0,
                }).ToList(),
            };

            IsBusy = true;
            StatusMessage = _languageService["mail_sending"];
            try
            {
                MailOperationResult res = await _mailService.SendMessageAsync(acc, outgoing, _composeAttachments);
                if (!res.Success)
                {
                    _feedbackService?.ShowToast(_languageService["mail_send_failed"] + ": " + res.ErrorMessage, FeedbackSeverity.Warning);
                    return;
                }

                if (!string.IsNullOrEmpty(_composeDraftId))
                    _store.Messages.RemoveAll(m => m.Id == _composeDraftId);

                _store.Messages.Add(outgoing);
                _mailService.SaveStore(_store);
                IsComposeOpen = false;
                _composeAttachments.Clear();
                _feedbackService?.ShowToast(_languageService["mail_sent_toast"]);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = null;
                NotifyChanged();
            }
        }

        public async Task SyncAllOrSelectedAsync()
        {
            if (IsBusy)
                return;

            if (!HasAccounts)
            {
                OpenAccountsOverlay();
                StartAddAccount();
                return;
            }

            List<MailAccountModel> targets = !string.IsNullOrEmpty(_selectedAccountId)
                ? _store.Accounts.Where(a => a.Id == _selectedAccountId).ToList()
                : _store.Accounts.ToList();

            if (targets.Count == 0)
                targets = _store.Accounts.ToList();

            IsBusy = true;
            StatusMessage = _languageService["mail_syncing"];
            int totalFetched = 0;
            string lastErr = null;

            try
            {
                foreach (MailAccountModel acc in targets)
                {
                    MailOperationResult res = await _mailService.SyncAccountAsync(acc, _store, 40);
                    if (res.Success)
                        totalFetched += res.FetchedCount;
                    else
                        lastErr = res.ErrorMessage;
                }

                if (lastErr != null && targets.Count == 1)
                {
                    _feedbackService?.ShowToast(_languageService["mail_sync_failed"] + ": " + lastErr, FeedbackSeverity.Warning);
                }
                else
                {
                    _feedbackService?.ShowToast(_languageService.LoadTextFmt("mail_synced_fmt", totalFetched));
                }
            }
            finally
            {
                IsBusy = false;
                StatusMessage = null;
                NotifyChanged();
            }
        }

        public void OpenAccountsOverlay()
        {
            IsAccountsOverlayOpen = true;
            IsEditingAccount = false;
            NotifyChanged();
        }

        public void StartAddAccount()
        {
            _editAccountId = null;
            _editDisplayName = string.Empty;
            _editEmail = string.Empty;
            _editUsername = string.Empty;
            _editPassword = string.Empty;
            _editImapHost = string.Empty;
            _editImapPort = 993;
            _editImapSsl = true;
            _editSmtpHost = string.Empty;
            _editSmtpPort = 465;
            _editSmtpSsl = true;
            _editAuthMode = MailAuthModes.Password;
            _editRefreshToken = string.Empty;
            _editAccessToken = string.Empty;
            _editAccessExpiry = default;
            IsAccountsOverlayOpen = true;
            IsEditingAccount = true;
            NotifyChanged();
        }

        public bool EditedAccountUsesGoogle => string.Equals(_editAuthMode, MailAuthModes.Google, StringComparison.OrdinalIgnoreCase);

        public async Task StartGoogleSignInAsync()
        {
            if (_mailService == null || _nativeBrowserService == null || _randomService == null)
            {
                _feedbackService?.ShowToast(_languageService["mail_google_failed"], FeedbackSeverity.Warning);
                return;
            }

            string state = SilentNotes.MailOAuthSession.StatePrefix + CryptoUtils.GenerateRandomBase62String(16, _randomService);
            string verifier = CryptoUtils.GenerateRandomBase62String(64, _randomService);
            _mailService.SaveGoogleOAuthPending(state, verifier);
            string url = _mailService.BuildGoogleMailAuthorizationUrl(state, verifier);
            _nativeBrowserService.OpenWebsiteInApp(url);
            _feedbackService?.ShowToast(_languageService["mail_google_waiting"]);
        }

        public async Task FinishGoogleSignInAsync(string redirectUrl)
        {
            if (string.IsNullOrEmpty(redirectUrl) || redirectUrl == _handledGoogleRedirect || _mailService == null)
                return;

            _handledGoogleRedirect = redirectUrl;
            SilentNotes.MailOAuthSession.Clear(redirectUrl);
            IsBusy = true;
            try
            {
                MailAccountModel signedIn = await _mailService.FinishGoogleMailAuthorizationAsync(redirectUrl);
                MailAccountModel existing = _store.Accounts.FirstOrDefault(a =>
                    string.Equals(a.EmailAddress, signedIn.EmailAddress, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    _store.Accounts.Add(signedIn);
                    existing = signedIn;
                }
                else
                {
                    existing.AuthMode = MailAuthModes.Google;
                    existing.Username = signedIn.Username;
                    existing.AccessToken = signedIn.AccessToken;
                    existing.RefreshToken = signedIn.RefreshToken;
                    existing.AccessTokenExpiryUtc = signedIn.AccessTokenExpiryUtc;
                    existing.Password = string.Empty;
                    if (string.IsNullOrWhiteSpace(existing.ImapHost))
                    {
                        existing.ImapHost = signedIn.ImapHost;
                        existing.ImapPort = signedIn.ImapPort;
                        existing.ImapUseSsl = signedIn.ImapUseSsl;
                        existing.SmtpHost = signedIn.SmtpHost;
                        existing.SmtpPort = signedIn.SmtpPort;
                        existing.SmtpUseSsl = signedIn.SmtpUseSsl;
                    }
                }

                _selectedAccountId = existing.Id;
                _mailService.SaveStore(_store);
                StartEditAccount(existing);
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("mail_google_ok_fmt", existing.EmailAddress));
            }
            catch (Exception ex)
            {
                _feedbackService?.ShowToast(_languageService["mail_google_failed"] + ": " + ex.Message, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                NotifyChanged();
            }
        }

        public void StartEditAccount(MailAccountModel account)
        {
            if (account == null)
                return;
            _editAccountId = account.Id;
            _editDisplayName = account.DisplayName;
            _editEmail = account.EmailAddress;
            _editUsername = account.Username;
            _editPassword = account.Password;
            _editImapHost = account.ImapHost;
            _editImapPort = account.ImapPort;
            _editImapSsl = account.ImapUseSsl;
            _editSmtpHost = account.SmtpHost;
            _editSmtpPort = account.SmtpPort;
            _editSmtpSsl = account.SmtpUseSsl;
            _editAuthMode = string.IsNullOrEmpty(account.AuthMode) ? MailAuthModes.Password : account.AuthMode;
            _editRefreshToken = account.RefreshToken ?? string.Empty;
            _editAccessToken = account.AccessToken ?? string.Empty;
            _editAccessExpiry = account.AccessTokenExpiryUtc;
            IsAccountsOverlayOpen = true;
            IsEditingAccount = true;
            NotifyChanged();
        }

        public void ApplyQuickProvider(string providerDomain)
        {
            if (string.IsNullOrWhiteSpace(_editEmail) || !_editEmail.Contains('@'))
            {
                _editEmail = "user@" + providerDomain;
            }
            AutoFillAccountPreset();
            NotifyChanged();
        }

        private void AutoFillAccountPreset()
        {
            var temp = new MailAccountModel
            {
                EmailAddress = _editEmail ?? string.Empty,
                Username = _editUsername ?? string.Empty,
                ImapHost = _editImapHost ?? string.Empty,
                ImapPort = _editImapPort,
                ImapUseSsl = _editImapSsl,
                SmtpHost = _editSmtpHost ?? string.Empty,
                SmtpPort = _editSmtpPort,
                SmtpUseSsl = _editSmtpSsl,
            };
            _mailService?.ApplyProviderPreset(temp);
            _editUsername = temp.Username;
            _editImapHost = temp.ImapHost;
            _editImapPort = temp.ImapPort;
            _editImapSsl = temp.ImapUseSsl;
            _editSmtpHost = temp.SmtpHost;
            _editSmtpPort = temp.SmtpPort;
            _editSmtpSsl = temp.SmtpUseSsl;
        }

        public async Task TestEditedAccountAsync()
        {
            if (IsBusy)
                return;

            MailAccountModel candidate = BuildEditedAccountModel();
            IsBusy = true;
            StatusMessage = _languageService["mail_testing"];
            try
            {
                MailOperationResult res = await _mailService.TestConnectionAsync(candidate);
                if (res.Success)
                    _feedbackService?.ShowToast(_languageService["mail_test_ok"]);
                else
                    _feedbackService?.ShowToast(_languageService["mail_test_failed"] + ": " + res.ErrorMessage, FeedbackSeverity.Warning);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = null;
                NotifyChanged();
            }
        }

        public async Task SaveEditedAccountAsync()
        {
            if (string.IsNullOrWhiteSpace(_editEmail) || string.IsNullOrWhiteSpace(_editImapHost))
            {
                _feedbackService?.ShowToast(_languageService["mail_account_incomplete"], FeedbackSeverity.Warning);
                return;
            }

            MailAccountModel existing = !string.IsNullOrEmpty(_editAccountId)
                ? _store.Accounts.FirstOrDefault(a => a.Id == _editAccountId)
                : null;

            bool isNew = existing == null;
            MailAccountModel target = existing ?? new MailAccountModel();
            target.DisplayName = (_editDisplayName ?? string.Empty).Trim();
            target.EmailAddress = (_editEmail ?? string.Empty).Trim();
            target.Username = string.IsNullOrWhiteSpace(_editUsername) ? target.EmailAddress : _editUsername.Trim();
            target.Password = _editPassword ?? string.Empty;
            target.ImapHost = (_editImapHost ?? string.Empty).Trim();
            target.ImapPort = _editImapPort > 0 ? _editImapPort : 993;
            target.ImapUseSsl = _editImapSsl;
            target.SmtpHost = string.IsNullOrWhiteSpace(_editSmtpHost)
                ? target.ImapHost.Replace("imap.", "smtp.", StringComparison.OrdinalIgnoreCase)
                : _editSmtpHost.Trim();
            target.SmtpPort = _editSmtpPort > 0 ? _editSmtpPort : 465;
            target.SmtpUseSsl = _editSmtpSsl;
            if (!string.IsNullOrWhiteSpace(target.Password))
            {
                target.AuthMode = MailAuthModes.Password;
                target.RefreshToken = string.Empty;
                target.AccessToken = string.Empty;
            }
            else
            {
                target.AuthMode = _editAuthMode ?? MailAuthModes.Password;
                target.RefreshToken = _editRefreshToken ?? string.Empty;
                target.AccessToken = _editAccessToken ?? string.Empty;
                target.AccessTokenExpiryUtc = _editAccessExpiry;
            }
            target.Password = MailServiceBase.NormalizeAppPassword(target);

            if (isNew)
                _store.Accounts.Add(target);

            _selectedAccountId = target.Id;
            _mailService?.SaveStore(_store);
            IsEditingAccount = false;
            IsAccountsOverlayOpen = false;
            NotifyChanged();

            if (isNew && (target.UsesGoogleOAuth || !string.IsNullOrEmpty(target.Password)))
                await SyncAllOrSelectedAsync();
        }

        public void DeleteAccount(MailAccountModel account)
        {
            if (account == null)
                return;

            _store.Accounts.RemoveAll(a => a.Id == account.Id);
            _store.Messages.RemoveAll(m => m.AccountId == account.Id);
            if (_selectedAccountId == account.Id)
                _selectedAccountId = _store.Accounts.FirstOrDefault()?.Id ?? string.Empty;

            _mailService?.SaveStore(_store);
            NotifyChanged();
        }

        private MailAccountModel BuildEditedAccountModel()
        {
            return new MailAccountModel
            {
                Id = _editAccountId ?? Guid.NewGuid().ToString("N"),
                DisplayName = (_editDisplayName ?? string.Empty).Trim(),
                EmailAddress = (_editEmail ?? string.Empty).Trim(),
                Username = string.IsNullOrWhiteSpace(_editUsername) ? (_editEmail ?? string.Empty).Trim() : _editUsername.Trim(),
                Password = _editPassword ?? string.Empty,
                ImapHost = (_editImapHost ?? string.Empty).Trim(),
                ImapPort = _editImapPort > 0 ? _editImapPort : 993,
                ImapUseSsl = _editImapSsl,
                SmtpHost = (_editSmtpHost ?? string.Empty).Trim(),
                SmtpPort = _editSmtpPort > 0 ? _editSmtpPort : 465,
                SmtpUseSsl = _editSmtpSsl,
                AuthMode = string.IsNullOrWhiteSpace(_editPassword) ? (_editAuthMode ?? MailAuthModes.Password) : MailAuthModes.Password,
                RefreshToken = string.IsNullOrWhiteSpace(_editPassword) ? _editRefreshToken : string.Empty,
                AccessToken = string.IsNullOrWhiteSpace(_editPassword) ? _editAccessToken : string.Empty,
                AccessTokenExpiryUtc = _editAccessExpiry,
            };
        }

        private MailAccountModel GetComposeAccount()
        {
            if (!string.IsNullOrEmpty(_composeAccountId))
            {
                var found = _store.Accounts.FirstOrDefault(a => a.Id == _composeAccountId);
                if (found != null)
                    return found;
            }
            return _store.Accounts.FirstOrDefault();
        }

        private string GetAccountEmail(string accountId)
        {
            return _store.Accounts.FirstOrDefault(a => a.Id == accountId)?.EmailAddress ?? string.Empty;
        }

        private static string QuoteLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => "> " + l));
        }

        private void NotifyChanged()
        {
            OnPropertyChanged(
                nameof(Accounts),
                nameof(HasAccounts),
                nameof(FilteredMessages),
                nameof(IsEmpty),
                nameof(HasNoMatches),
                nameof(OpenMessage),
                nameof(IsMessageOpen));
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Item view model for displaying a single email row or full message in the Mail tab.
    /// </summary>
    public class MailMessageItemViewModel
    {
        public MailMessageItemViewModel(MailMessageModel model, string accountEmail)
        {
            Model = model;
            AccountEmail = accountEmail;
        }

        public MailMessageModel Model { get; }

        public string AccountEmail { get; }

        public string SenderDisplay
        {
            get
            {
                string raw = string.Equals(Model.Folder, MailFolders.Sent, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(Model.Folder, MailFolders.Drafts, StringComparison.OrdinalIgnoreCase)
                    ? ("To: " + (string.IsNullOrWhiteSpace(Model.To) ? "(No recipient)" : Model.To))
                    : Model.From;

                if (string.IsNullOrWhiteSpace(raw))
                    return "(Unknown)";

                int angle = raw.IndexOf('<');
                if (angle > 1)
                {
                    string name = raw.Substring(0, angle).Trim().Trim('"', '\'');
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
                return raw.Trim();
            }
        }

        public string AvatarInitial
        {
            get
            {
                string s = SenderDisplay.Replace("To:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                foreach (char c in s)
                {
                    if (char.IsLetterOrDigit(c))
                        return char.ToUpperInvariant(c).ToString();
                }
                return "✉";
            }
        }

        public string SubjectDisplay => string.IsNullOrWhiteSpace(Model.Subject) ? "(No subject)" : Model.Subject;

        public string SnippetDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Model.Snippet))
                    return Model.Snippet;
                return MailServiceBase.BuildSnippet(Model.BodyText, Model.BodyHtml);
            }
        }

        public string DateShortText
        {
            get
            {
                DateTime local = Model.DateUtc.ToLocalTime();
                if (local.Date == DateTime.Today)
                    return local.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (local.Year == DateTime.Today.Year)
                    return local.ToString("MMM d", CultureInfo.InvariantCulture);
                return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        public string DateFullText => Model.DateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        public string PlainBodyForDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Model.BodyText))
                    return Model.BodyText;
                if (!string.IsNullOrWhiteSpace(Model.BodyHtml))
                    return MailServiceBase.StripHtmlTags(Model.BodyHtml);
                return string.Empty;
            }
        }

        public bool HasAttachments => Model.Attachments != null && Model.Attachments.Count > 0;
    }
}
