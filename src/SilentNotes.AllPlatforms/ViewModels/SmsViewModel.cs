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

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model of the sms tab. It shows the conversations of the device message store, allows
    /// to read them and to send text messages.
    /// </summary>
    public class SmsViewModel : ViewModelBase
    {
        private readonly IDeviceSmsService _smsService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private string _filter;
        private bool _hasPermission;
        private bool _permissionRequested;
        private List<SmsThreadItemViewModel> _threads = new List<SmsThreadItemViewModel>();

        private bool _isConversationOpen;
        private long _openThreadId;
        private string _openAddress;
        private string _openTitle;
        private List<SmsMessageItemViewModel> _messages = new List<SmsMessageItemViewModel>();
        private string _composerText;

        /// <summary>
        /// Initializes a new instance of the <see cref="SmsViewModel"/> class.
        /// </summary>
        public SmsViewModel(IDeviceSmsService smsService, ILanguageService languageService, IFeedbackService feedbackService)
        {
            _smsService = smsService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            OpenThreadCommand = new AsyncRelayCommand<SmsThreadItemViewModel>(OpenThreadAsync);
            ComposeNewCommand = new RelayCommand(ComposeNew);
            CloseConversationCommand = new RelayCommand(CloseConversation);
            SendCommand = new AsyncRelayCommand(SendAsync);
            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            SetDefaultAppCommand = new AsyncRelayCommand(SetDefaultAppAsync);

            // Loading the conversations is deferred to InitializeAsync, so the app start is not
            // slowed down for users who never open the sms tab.
            _hasPermission = _smsService.HasReadPermission && _smsService.HasSendPermission;
        }

        /// <summary>Gets a value indicating whether the platform supports messages.</summary>
        public bool IsSupported
        {
            get { return _smsService.IsSupported; }
        }

        /// <summary>Gets a value indicating whether the messages may be read and sent.</summary>
        public bool HasPermission
        {
            get { return _hasPermission; }
            private set { SetProperty(ref _hasPermission, value); }
        }

        /// <summary>Gets a value indicating whether the message permission is missing.</summary>
        public bool NeedsPermission
        {
            get { return _smsService.IsSupported && !_hasPermission; }
        }

        /// <summary>Gets a value indicating whether aNote is the default SMS app.</summary>
        public bool IsDefaultSmsApp
        {
            get { return _smsService.IsDefaultSmsApp; }
        }

        /// <summary>Gets a value indicating whether the app should be set as default SMS app.</summary>
        public bool NeedsDefaultApp
        {
            get { return _smsService.IsSupported && _hasPermission && !_smsService.IsDefaultSmsApp; }
        }

        /// <summary>Gets or sets the search filter.</summary>
        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilteredThreads), nameof(HasNoMatches));
                }
            }
        }

        /// <summary>Gets the conversations matching the filter.</summary>
        public IReadOnlyList<SmsThreadItemViewModel> FilteredThreads
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _threads;
                return _threads.Where(t => t.Matches(query)).ToList();
            }
        }

        /// <summary>Gets a value indicating whether there are no conversations at all.</summary>
        public bool IsEmpty
        {
            get { return _threads.Count == 0; }
        }

        /// <summary>Gets a value indicating whether the filter has no matches.</summary>
        public bool HasNoMatches
        {
            get { return !IsEmpty && (FilteredThreads.Count == 0); }
        }

        /// <summary>Gets the text showing the number of conversations.</summary>
        public string CountText
        {
            get { return _languageService.LoadTextFmt("sms_count", _threads.Count); }
        }

        /// <summary>Gets a value indicating whether a conversation is currently open.</summary>
        public bool IsConversationOpen
        {
            get { return _isConversationOpen; }
            private set { SetProperty(ref _isConversationOpen, value); }
        }

        /// <summary>Gets a value indicating whether a new (empty) conversation is open.</summary>
        public bool IsNewConversation
        {
            get { return _openThreadId == 0; }
        }

        /// <summary>Gets the title of the open conversation.</summary>
        public string ConversationTitle
        {
            get { return _openTitle; }
        }

        /// <summary>Gets or sets the phone number of a new conversation.</summary>
        public string OpenAddress
        {
            get { return _openAddress; }
            set { SetProperty(ref _openAddress, value); }
        }

        /// <summary>Gets the messages of the open conversation, the oldest first.</summary>
        public IReadOnlyList<SmsMessageItemViewModel> Messages
        {
            get { return _messages; }
        }

        /// <summary>Gets a value indicating whether the open conversation has no messages.</summary>
        public bool HasNoMessages
        {
            get { return _messages.Count == 0; }
        }

        /// <summary>Gets or sets the text which is about to be sent.</summary>
        public string ComposerText
        {
            get { return _composerText; }
            set
            {
                if (SetProperty(ref _composerText, value))
                    OnPropertyChanged(nameof(CanSend));
            }
        }

        /// <summary>Gets a value indicating whether the message can be sent.</summary>
        public bool CanSend
        {
            get
            {
                bool hasAddress = (_openThreadId != 0) || !string.IsNullOrWhiteSpace(_openAddress);
                return !string.IsNullOrWhiteSpace(_composerText) && hasAddress;
            }
        }

        public ICommand OpenThreadCommand { get; }
        public ICommand ComposeNewCommand { get; }
        public ICommand CloseConversationCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand GrantPermissionCommand { get; }
        public ICommand SetDefaultAppCommand { get; }

        /// <summary>
        /// Initializes the sms tab, requests the permission if necessary and loads the threads.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!_smsService.IsSupported)
                return;

            HasPermission = _smsService.HasReadPermission && _smsService.HasSendPermission;
            if (!HasPermission && !_permissionRequested)
            {
                _permissionRequested = true;
                HasPermission = await _smsService.RequestPermissionsAsync();
            }

            Reload();
            OnPropertyChanged(nameof(IsDefaultSmsApp), nameof(NeedsDefaultApp), nameof(NeedsPermission));
        }

        /// <summary>Reloads the conversations, and the open conversation if any.</summary>
        public void Reload()
        {
            _hasPermission = _smsService.HasReadPermission && _smsService.HasSendPermission;
            try
            {
                IReadOnlyList<SmsThreadModel> threads = _hasPermission
                    ? _smsService.GetThreadsAsync().GetAwaiter().GetResult()
                    : new List<SmsThreadModel>();
                _threads = threads.Select(t => new SmsThreadItemViewModel(t)).ToList();
            }
            catch (Exception)
            {
                _threads = new List<SmsThreadItemViewModel>();
            }

            if (_isConversationOpen && (_openThreadId != 0))
                LoadMessages();

            OnPropertyChanged(nameof(FilteredThreads), nameof(IsEmpty), nameof(HasNoMatches), nameof(CountText));
            OnPropertyChanged(nameof(HasPermission), nameof(IsDefaultSmsApp), nameof(NeedsDefaultApp), nameof(NeedsPermission));
        }

        /// <summary>Closes the open conversation.</summary>
        public void CloseConversation()
        {
            IsConversationOpen = false;
            _openThreadId = 0;
            _openAddress = null;
            _openTitle = null;
            _messages = new List<SmsMessageItemViewModel>();
            OnPropertyChanged(nameof(Messages), nameof(IsNewConversation), nameof(HasNoMessages), nameof(CanSend));
        }

        private async Task OpenThreadAsync(SmsThreadItemViewModel thread)
        {
            if (thread == null)
                return;

            _openThreadId = thread.Model.ThreadId;
            _openAddress = thread.Model.Address;
            _openTitle = thread.Model.Title;
            _messages = new List<SmsMessageItemViewModel>();
            IsConversationOpen = true;
            OnPropertyChanged(nameof(IsNewConversation), nameof(ConversationTitle), nameof(OpenAddress), nameof(Messages), nameof(HasNoMessages), nameof(CanSend));

            LoadMessages();

            try
            {
                await _smsService.MarkThreadReadAsync(_openThreadId);
            }
            catch (Exception)
            {
            }
        }

        private void ComposeNew()
        {
            _openThreadId = 0;
            _openAddress = string.Empty;
            _openTitle = _languageService["sms_new_message"];
            _messages = new List<SmsMessageItemViewModel>();
            IsConversationOpen = true;
            OnPropertyChanged(nameof(IsNewConversation), nameof(ConversationTitle), nameof(OpenAddress), nameof(Messages), nameof(HasNoMessages), nameof(CanSend));
        }

        private void LoadMessages()
        {
            try
            {
                IReadOnlyList<SmsMessageModel> messages = _smsService.GetMessagesAsync(_openThreadId).GetAwaiter().GetResult();
                _messages = messages.Select(m => new SmsMessageItemViewModel(m)).ToList();
            }
            catch (Exception)
            {
                _messages = new List<SmsMessageItemViewModel>();
            }
            OnPropertyChanged(nameof(Messages), nameof(HasNoMessages));
        }

        private async Task SendAsync()
        {
            string body = _composerText?.Trim();
            if (string.IsNullOrEmpty(body))
                return;

            string address = _openAddress?.Trim();
            if (string.IsNullOrEmpty(address))
                return;

            try
            {
                await _smsService.SendSmsAsync(address, body);
                ComposerText = string.Empty;
                _feedbackService?.ShowToast(_languageService["sms_sent"]);

                if (_openThreadId != 0)
                {
                    LoadMessages();
                }
                else
                {
                    _messages.Add(new SmsMessageItemViewModel(new SmsMessageModel
                    {
                        Address = address,
                        Body = body,
                        Incoming = false,
                        DateUtc = DateTime.UtcNow,
                    }));
                    OnPropertyChanged(nameof(Messages), nameof(HasNoMessages));
                }
                Reload();
            }
            catch (Exception)
            {
                _feedbackService?.ShowToast(_languageService["sms_send_failed"], FeedbackSeverity.Warning);
            }
        }

        /// <summary>Asks the user for the message permissions.</summary>
        public async Task GrantPermissionAsync()
        {
            HasPermission = await _smsService.RequestPermissionsAsync();
            Reload();
            OnPropertyChanged(nameof(NeedsPermission), nameof(NeedsDefaultApp));
        }

        /// <summary>Asks the user to make aNote the default SMS app.</summary>
        public async Task SetDefaultAppAsync()
        {
            await _smsService.RequestDefaultSmsAppAsync();
            OnPropertyChanged(nameof(IsDefaultSmsApp), nameof(NeedsDefaultApp));
        }
    }

    /// <summary>
    /// View model for a single conversation.
    /// </summary>
    public class SmsThreadItemViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmsThreadItemViewModel"/> class.
        /// </summary>
        public SmsThreadItemViewModel(SmsThreadModel model)
        {
            Model = model;
        }

        /// <summary>Gets the wrapped thread.</summary>
        public SmsThreadModel Model { get; }

        /// <summary>Gets the name to show for the conversation.</summary>
        public string Title
        {
            get { return Model.Title; }
        }

        /// <summary>Gets the last message.</summary>
        public string Snippet
        {
            get { return Model.Snippet; }
        }

        /// <summary>Gets the time of the last message.</summary>
        public string TimeText
        {
            get
            {
                DateTime local = Model.DateUtc.ToLocalTime();
                return (local.Date == DateTime.Today)
                    ? local.ToString("HH:mm")
                    : local.ToString("d MMM");
            }
        }

        /// <summary>Gets a value indicating whether the conversation has unread messages.</summary>
        public bool HasUnread
        {
            get { return Model.UnreadCount > 0; }
        }

        /// <summary>Determines whether the conversation matches a search query.</summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            if (Title != null && Title.ToLowerInvariant().Contains(query))
                return true;
            if (Model.Address != null && Model.Address.ToLowerInvariant().Contains(query.Replace(" ", "")))
                return true;
            return false;
        }
    }

    /// <summary>
    /// View model for a single message.
    /// </summary>
    public class SmsMessageItemViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmsMessageItemViewModel"/> class.
        /// </summary>
        public SmsMessageItemViewModel(SmsMessageModel model)
        {
            Model = model;
        }

        /// <summary>Gets the wrapped message.</summary>
        public SmsMessageModel Model { get; }

        /// <summary>Gets the text of the message.</summary>
        public string Body
        {
            get { return Model.Body; }
        }

        /// <summary>Gets a value indicating whether the message was received.</summary>
        public bool IsIncoming
        {
            get { return Model.Incoming; }
        }

        /// <summary>Gets the time of the message.</summary>
        public string TimeText
        {
            get { return Model.DateUtc.ToLocalTime().ToString("g"); }
        }
    }
}
