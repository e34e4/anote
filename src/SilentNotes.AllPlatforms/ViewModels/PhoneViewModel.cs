// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// Sub-sections inside the Phone tab.
    /// </summary>
    public enum PhoneSubView
    {
        Numpad = 0,
        History = 1,
        Blocklist = 2,
        SpamDb = 3,
    }

    /// <summary>
    /// View model for the Phone (dialer, call history, blocklist with masks, and AGContacts/Russian AON spam database) tab.
    /// </summary>
    public class PhoneViewModel : ViewModelBase
    {
        private readonly IDevicePhoneService _phoneService;
        private readonly IPhoneSpamService _spamService;
        private readonly IFilePickerService _filePickerService;
        private readonly INativeBrowserService _browserService;
        private readonly NoteRepositoryModel _repositoryModel;
        private readonly IClipboardService _clipboardService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private List<CallLogItemViewModel> _callLog = new List<CallLogItemViewModel>();
        private string _dialedNumber = string.Empty;
        private string _filter;
        private PhoneSubView _subView = PhoneSubView.Numpad;
        private bool _hasPermission;
        private bool _permissionRequested;

        // Blocklist editor fields
        private string _newBlockPattern = string.Empty;
        private string _newBlockLabel = string.Empty;

        // Spam DB & AON lookup fields
        private bool _isUpdatingSpamDb;
        private string _spamUpdateProgressText = string.Empty;
        private bool _isLookingUpNumber;
        private string _lookupNumberInput = string.Empty;
        private PhoneSpamEntryModel _lastLookupResult;
        private string _lastLookupStatusText = string.Empty;

        public PhoneViewModel(
            IDevicePhoneService phoneService,
            IPhoneSpamService spamService,
            IFilePickerService filePickerService,
            INativeBrowserService browserService,
            NoteRepositoryModel repositoryModel,
            IClipboardService clipboardService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _phoneService = phoneService;
            _spamService = spamService;
            _filePickerService = filePickerService;
            _browserService = browserService;
            _repositoryModel = repositoryModel;
            _clipboardService = clipboardService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _hasPermission = _phoneService.HasPermissions;

            ShowNumpadCommand = new RelayCommand(() => SubView = PhoneSubView.Numpad);
            ShowHistoryCommand = new RelayCommand(() => SubView = PhoneSubView.History);
            ShowBlocklistCommand = new RelayCommand(() => SubView = PhoneSubView.Blocklist);
            ShowSpamDbCommand = new RelayCommand(() => SubView = PhoneSubView.SpamDb);
            DigitCommand = new RelayCommand<string>(PressDigit);
            BackspaceCommand = new RelayCommand(Backspace);
            ClearNumberCommand = new RelayCommand(() => DialedNumber = string.Empty);
            CallCommand = new AsyncRelayCommand(PlaceCallAsync);
            CallNumberCommand = new AsyncRelayCommand<string>(CallSpecificNumberAsync);
            FillNumberCommand = new RelayCommand<string>(num => { DialedNumber = num ?? string.Empty; SubView = PhoneSubView.Numpad; });
            CopyNumberCommand = new AsyncRelayCommand<string>(CopyNumberAsync);
            GrantPermissionCommand = new AsyncRelayCommand(GrantPermissionAsync);
            SetDefaultAppCommand = new AsyncRelayCommand(SetDefaultAppAsync);
            AnswerCallCommand = new RelayCommand(() => _phoneService.AnswerCall());
            HangupCallCommand = new RelayCommand(() => _phoneService.HangupCall());
            ToggleMuteCommand = new RelayCommand(() => _phoneService.ToggleMute());
            ToggleSpeakerCommand = new RelayCommand(() => _phoneService.ToggleSpeaker());
        }

        public ICommand ShowNumpadCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand ShowBlocklistCommand { get; }
        public ICommand ShowSpamDbCommand { get; }
        public ICommand DigitCommand { get; }
        public ICommand BackspaceCommand { get; }
        public ICommand ClearNumberCommand { get; }
        public ICommand CallCommand { get; }
        public ICommand CallNumberCommand { get; }
        public ICommand FillNumberCommand { get; }
        public ICommand CopyNumberCommand { get; }
        public ICommand GrantPermissionCommand { get; }
        public ICommand SetDefaultAppCommand { get; }
        public ICommand AnswerCallCommand { get; }
        public ICommand HangupCallCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleSpeakerCommand { get; }

        /// <summary>Gets a value indicating whether telephony is supported on this platform.</summary>
        public bool IsSupported => _phoneService.IsSupported;

        /// <summary>Gets a value indicating whether call log and phone permissions are granted.</summary>
        public bool HasPermission
        {
            get => _hasPermission;
            private set => SetProperty(ref _hasPermission, value);
        }

        /// <summary>Gets a value indicating whether permissions are needed.</summary>
        public bool NeedsPermission => _phoneService.IsSupported && !_hasPermission;

        /// <summary>Gets a value indicating whether aNote is currently the default phone app.</summary>
        public bool IsDefaultPhoneApp => _phoneService.IsDefaultDialerApp;

        /// <summary>Gets or sets the active sub-view in the Phone tab.</summary>
        public PhoneSubView SubView
        {
            get => _subView;
            set
            {
                if (SetProperty(ref _subView, value))
                {
                    OnPropertyChanged(
                        nameof(IsHistoryView),
                        nameof(IsNumpadView),
                        nameof(IsBlocklistView),
                        nameof(IsSpamDbView),
                        nameof(BlockRules),
                        nameof(SpamDatabaseStatus),
                        nameof(FilteredSpamEntries));
                }
            }
        }

        public bool IsNumpadView => _subView == PhoneSubView.Numpad;
        public bool IsHistoryView => _subView == PhoneSubView.History;
        public bool IsBlocklistView => _subView == PhoneSubView.Blocklist;
        public bool IsSpamDbView => _subView == PhoneSubView.SpamDb;

        /// <summary>Gets or sets the number typed on the numpad.</summary>
        public string DialedNumber
        {
            get => _dialedNumber;
            set
            {
                if (SetProperty(ref _dialedNumber, value ?? string.Empty))
                {
                    OnPropertyChanged(
                        nameof(CanCall),
                        nameof(MatchedContactName),
                        nameof(DialedSpamInfo),
                        nameof(DialedBlockedRule));
                }
            }
        }

        /// <summary>Gets a value indicating whether there is a number to call.</summary>
        public bool CanCall => !string.IsNullOrWhiteSpace(_dialedNumber);

        /// <summary>Gets the name of the contact matching the currently dialed number, if any.</summary>
        public string MatchedContactName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_dialedNumber) || _repositoryModel?.Contacts == null)
                    return null;

                string norm = NormalizeDigits(_dialedNumber);
                if (norm.Length < 3)
                    return null;

                foreach (ContactModel contact in _repositoryModel.Contacts)
                {
                    foreach (string phone in contact.Phones)
                    {
                        string pNorm = NormalizeDigits(phone);
                        if (!string.IsNullOrEmpty(pNorm) && (pNorm == norm || pNorm.EndsWith(norm) || norm.EndsWith(pNorm)))
                            return contact.Name;
                    }
                }
                return null;
            }
        }

        /// <summary>Gets local spam database info for the currently dialed number, if any.</summary>
        public PhoneSpamEntryModel DialedSpamInfo
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_dialedNumber) || _dialedNumber.Length < 5)
                    return null;
                return _spamService?.LookupLocal(_dialedNumber);
            }
        }

        /// <summary>Gets the matching blocklist rule for the currently dialed number, if any.</summary>
        public PhoneBlockRuleModel DialedBlockedRule
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_dialedNumber) || _dialedNumber.Length < 3)
                    return null;
                return _spamService?.FindMatchingBlockRule(_dialedNumber);
            }
        }

        /// <summary>Gets or sets the search filter for call history / blocklist / spam DB.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(
                        nameof(FilteredCallLog),
                        nameof(HasNoMatches),
                        nameof(BlockRules),
                        nameof(FilteredSpamEntries));
                }
            }
        }

        /// <summary>Gets the filtered call history entries.</summary>
        public IReadOnlyList<CallLogItemViewModel> FilteredCallLog
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _callLog;
                return _callLog.Where(c => c.Matches(query)).ToList();
            }
        }

        /// <summary>Gets the most recent call log entries for preview under the numpad.</summary>
        public IReadOnlyList<CallLogItemViewModel> RecentCallsPreview => _callLog.Take(5).ToList();

        /// <summary>Gets a value indicating whether the call history is empty.</summary>
        public bool IsEmpty => _callLog.Count == 0;

        /// <summary>Gets a value indicating whether the filter has no matches.</summary>
        public bool HasNoMatches => !IsEmpty && FilteredCallLog.Count == 0;

        /// <summary>Gets the call count summary text.</summary>
        public string CountText => _languageService.LoadTextFmt("dialer_count", _callLog.Count);

        /// <summary>Gets the active or incoming call snapshot, or null if no call is active.</summary>
        public ActiveCallModel ActiveCall => _phoneService.GetActiveCall();

        // ---------------- Blocklist properties & actions ----------------

        public bool IsBlocklistEnabled
        {
            get => _spamService?.IsBlocklistEnabled ?? true;
            set
            {
                if (_spamService != null)
                {
                    _spamService.IsBlocklistEnabled = value;
                    OnPropertyChanged(nameof(IsBlocklistEnabled));
                }
            }
        }

        public bool AutoBlockSpam
        {
            get => _spamService?.AutoBlockSpam ?? false;
            set
            {
                if (_spamService != null)
                {
                    _spamService.AutoBlockSpam = value;
                    OnPropertyChanged(nameof(AutoBlockSpam));
                }
            }
        }

        public bool SkipContactsWhenBlocking
        {
            get => _spamService?.SkipContactsWhenBlocking ?? true;
            set
            {
                if (_spamService != null)
                {
                    _spamService.SkipContactsWhenBlocking = value;
                    OnPropertyChanged(nameof(SkipContactsWhenBlocking));
                }
            }
        }

        public string NewBlockPattern
        {
            get => _newBlockPattern;
            set => SetProperty(ref _newBlockPattern, value ?? string.Empty);
        }

        public string NewBlockLabel
        {
            get => _newBlockLabel;
            set => SetProperty(ref _newBlockLabel, value ?? string.Empty);
        }

        public IReadOnlyList<PhoneBlockRuleModel> BlockRules
        {
            get
            {
                IReadOnlyList<PhoneBlockRuleModel> all = _spamService?.GetBlockRules() ?? Array.Empty<PhoneBlockRuleModel>();
                string q = _filter?.Trim();
                if (string.IsNullOrEmpty(q))
                    return all;
                return all.Where(r =>
                    r.Pattern.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(r.Label) && r.Label.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    PhoneBlockRuleModel.MatchesPattern(r.Pattern, q)).ToList();
            }
        }

        public void AddBlockRuleFromInputs()
        {
            if (string.IsNullOrWhiteSpace(_newBlockPattern))
                return;

            PhoneBlockRuleModel rule = _spamService?.AddBlockRule(_newBlockPattern, _newBlockLabel);
            if (rule != null)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("dialer_block_added", rule.Pattern));
                NewBlockPattern = string.Empty;
                NewBlockLabel = string.Empty;
                RefreshCallLogAnnotations();
                OnPropertyChanged(nameof(BlockRules), nameof(DialedBlockedRule));
            }
        }

        public void QuickBlockNumber(string number, string label = null)
        {
            if (string.IsNullOrWhiteSpace(number))
                return;
            PhoneBlockRuleModel rule = _spamService?.AddBlockRule(number, label);
            if (rule != null)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("dialer_block_added", rule.Pattern));
                RefreshCallLogAnnotations();
                OnPropertyChanged(nameof(BlockRules), nameof(DialedBlockedRule));
            }
        }

        public void QuickBlockPrefix(string number)
        {
            string mask = BuildPrefixMask(number);
            if (string.IsNullOrEmpty(mask))
                return;
            PhoneBlockRuleModel rule = _spamService?.AddBlockRule(mask, "Prefix mask");
            if (rule != null)
            {
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("dialer_block_added", rule.Pattern));
                RefreshCallLogAnnotations();
                OnPropertyChanged(nameof(BlockRules), nameof(DialedBlockedRule));
            }
        }

        public static string BuildPrefixMask(string number)
        {
            string canonical = PhoneBlockRuleModel.NormalizeCanonicalDigits(number);
            if (canonical.Length >= 4)
            {
                int prefixLen = canonical.StartsWith("7") && canonical.Length >= 4 ? 4 : Math.Min(4, canonical.Length);
                return "+" + canonical.Substring(0, prefixLen) + "*";
            }
            return null;
        }

        public void ToggleBlockRule(PhoneBlockRuleModel rule)
        {
            if (rule == null)
                return;
            _spamService?.SetBlockRuleEnabled(rule.Id, !rule.IsEnabled);
            RefreshCallLogAnnotations();
            OnPropertyChanged(nameof(BlockRules), nameof(DialedBlockedRule));
        }

        public void DeleteBlockRule(PhoneBlockRuleModel rule)
        {
            if (rule == null)
                return;
            _spamService?.DeleteBlockRule(rule.Id);
            RefreshCallLogAnnotations();
            OnPropertyChanged(nameof(BlockRules), nameof(DialedBlockedRule));
        }

        // ---------------- Spam DB & Russian AON / MNP properties & actions ----------------

        public PhoneSpamDatabaseStatus SpamDatabaseStatus =>
            _spamService?.GetDatabaseStatus() ?? new PhoneSpamDatabaseStatus();

        public IReadOnlyList<PhoneSpamEntryModel> FilteredSpamEntries =>
            _spamService?.SearchLocalSpamEntries(_filter, 150) ?? Array.Empty<PhoneSpamEntryModel>();

        public string CustomSpamSourceUrl
        {
            get => _spamService?.CustomSpamSourceUrl ?? string.Empty;
            set
            {
                if (_spamService != null)
                {
                    _spamService.CustomSpamSourceUrl = value;
                    OnPropertyChanged(nameof(CustomSpamSourceUrl));
                }
            }
        }

        public bool IsUpdatingSpamDb
        {
            get => _isUpdatingSpamDb;
            private set => SetProperty(ref _isUpdatingSpamDb, value);
        }

        public string SpamUpdateProgressText
        {
            get => _spamUpdateProgressText;
            private set => SetProperty(ref _spamUpdateProgressText, value);
        }

        public bool IsLookingUpNumber
        {
            get => _isLookingUpNumber;
            private set => SetProperty(ref _isLookingUpNumber, value);
        }

        public string LookupNumberInput
        {
            get => _lookupNumberInput;
            set => SetProperty(ref _lookupNumberInput, value ?? string.Empty);
        }

        public PhoneSpamEntryModel LastLookupResult
        {
            get => _lastLookupResult;
            private set => SetProperty(ref _lastLookupResult, value);
        }

        public string LastLookupStatusText
        {
            get => _lastLookupStatusText;
            private set => SetProperty(ref _lastLookupStatusText, value);
        }

        public async Task UpdateSpamDatabaseAsync(Action onProgressUiUpdate = null)
        {
            if (_spamService == null || IsUpdatingSpamDb)
                return;

            IsUpdatingSpamDb = true;
            SpamUpdateProgressText = _languageService["dialer_spam_updating"];
            onProgressUiUpdate?.Invoke();

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    SpamUpdateProgressText = msg;
                    onProgressUiUpdate?.Invoke();
                });

                var (total, updated, sources) = await _spamService.UpdateSpamDatabaseAsync(progress);
                SpamUpdateProgressText = _languageService.LoadTextFmt("dialer_spam_updated_fmt", total, updated, sources);
                _feedbackService?.ShowToast(SpamUpdateProgressText);
                RefreshCallLogAnnotations();
            }
            catch (Exception ex)
            {
                SpamUpdateProgressText = ex.Message;
            }
            finally
            {
                IsUpdatingSpamDb = false;
                OnPropertyChanged(
                    nameof(SpamDatabaseStatus),
                    nameof(FilteredSpamEntries),
                    nameof(DialedSpamInfo));
                onProgressUiUpdate?.Invoke();
            }
        }

        public async Task ImportSpamFileAsync()
        {
            if (_filePickerService == null || _spamService == null)
                return;

            try
            {
                bool picked = await _filePickerService.PickFile();
                if (!picked)
                    return;

                byte[] bytes = await _filePickerService.ReadPickedFile();
                if (bytes == null || bytes.Length == 0)
                    return;

                string text = Encoding.UTF8.GetString(bytes);
                int count = _spamService.ImportSpamData(text, "file-import");
                _feedbackService?.ShowToast(_languageService.LoadTextFmt("dialer_spam_imported_fmt", count));
                RefreshCallLogAnnotations();
                OnPropertyChanged(
                    nameof(SpamDatabaseStatus),
                    nameof(FilteredSpamEntries),
                    nameof(DialedSpamInfo));
            }
            catch (Exception)
            {
            }
        }

        public async Task CheckNumberOnlineAsync(string number = null)
        {
            string target = string.IsNullOrWhiteSpace(number)
                ? (!string.IsNullOrWhiteSpace(_lookupNumberInput) ? _lookupNumberInput : _dialedNumber)
                : number;

            if (string.IsNullOrWhiteSpace(target) || _spamService == null)
                return;

            LookupNumberInput = target.Trim();
            IsLookingUpNumber = true;
            LastLookupStatusText = _languageService["dialer_aon_checking"];
            OnPropertyChanged(nameof(IsLookingUpNumber), nameof(LastLookupStatusText));

            try
            {
                PhoneSpamEntryModel result = await _spamService.LookupOnlineAsync(target);
                LastLookupResult = result;
                LastLookupStatusText = result != null
                    ? result.SummaryText
                    : _languageService["dialer_aon_not_found"];

                RefreshCallLogAnnotations();
                OnPropertyChanged(
                    nameof(LastLookupResult),
                    nameof(LastLookupStatusText),
                    nameof(SpamDatabaseStatus),
                    nameof(FilteredSpamEntries),
                    nameof(DialedSpamInfo));
            }
            finally
            {
                IsLookingUpNumber = false;
                OnPropertyChanged(nameof(IsLookingUpNumber));
            }
        }

        public void MarkNumberAsSpam(string number, string category = "Спам / Вручную")
        {
            string target = string.IsNullOrWhiteSpace(number) ? _lookupNumberInput : number;
            if (string.IsNullOrWhiteSpace(target) || _spamService == null)
                return;

            var entry = new PhoneSpamEntryModel
            {
                Number = PhoneBlockRuleModel.NormalizeCanonicalDigits(target),
                Rating = PhoneSpamRating.Negative,
                NegativeCount = 1,
                Category = category,
                Source = "user",
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _spamService.UpsertSpamEntry(entry);
            LastLookupResult = entry;
            RefreshCallLogAnnotations();
            _feedbackService?.ShowToast(_languageService.LoadTextFmt("dialer_spam_marked_fmt", target));
            OnPropertyChanged(
                nameof(SpamDatabaseStatus),
                nameof(FilteredSpamEntries),
                nameof(DialedSpamInfo),
                nameof(LastLookupResult));
        }

        public void RemoveSpamEntry(PhoneSpamEntryModel entry)
        {
            if (entry == null || _spamService == null)
                return;
            _spamService.RemoveSpamEntry(entry.Number);
            RefreshCallLogAnnotations();
            OnPropertyChanged(
                nameof(SpamDatabaseStatus),
                nameof(FilteredSpamEntries),
                nameof(DialedSpamInfo));
        }

        public void OpenExternalLookup(string provider, string number = null)
        {
            string target = string.IsNullOrWhiteSpace(number)
                ? (!string.IsNullOrWhiteSpace(_lookupNumberInput) ? _lookupNumberInput : _dialedNumber)
                : number;
            string digits = PhoneBlockRuleModel.NormalizeCanonicalDigits(target);
            if (string.IsNullOrEmpty(digits) || _browserService == null)
                return;

            string url = provider switch
            {
                "yandex" => $"https://yandex.ru/search/?text=%2B{digits}",
                "2gis" => $"https://2gis.ru/search/{digits}",
                "niir" => "https://www.niir.ru/bdpn/bdpn-proverka-nomera/",
                "mbagc" => $"https://callerid.mbagc.ru/{digits}?lang=ru",
                "zvonilime" => $"https://zvonili.me/phone/{digits}",
                "whocalls" => $"https://whocalls.ru/{(digits.Length == 11 && digits.StartsWith("7") ? digits.Substring(1) : digits)}",
                _ => $"https://yandex.ru/search/?text=%2B{digits}",
            };
            _browserService.OpenWebsite(url);
        }

        /// <summary>Initializes the Phone tab and loads the call history.</summary>
        public async Task InitializeAsync()
        {
            if (!_phoneService.IsSupported)
                return;

            HasPermission = _phoneService.HasPermissions;
            if (!HasPermission && !_permissionRequested)
            {
                _permissionRequested = true;
                HasPermission = await _phoneService.RequestPermissionsAsync();
            }

            await ReloadAsync();
        }

        /// <summary>Reloads the call log and refreshes permission/default-app state.</summary>
        public async Task ReloadAsync()
        {
            HasPermission = _phoneService.HasPermissions;
            try
            {
                IReadOnlyList<CallLogEntryModel> entries = HasPermission
                    ? await _phoneService.GetCallLogAsync()
                    : new List<CallLogEntryModel>();
                _callLog = entries.Select(e => new CallLogItemViewModel(
                    e,
                    _spamService?.LookupLocal(e.Number),
                    _spamService?.FindMatchingBlockRule(e.Number))).ToList();
            }
            catch (Exception)
            {
                _callLog = new List<CallLogItemViewModel>();
            }

            OnPropertyChanged(
                nameof(HasPermission),
                nameof(NeedsPermission),
                nameof(IsDefaultPhoneApp),
                nameof(FilteredCallLog),
                nameof(RecentCallsPreview),
                nameof(IsEmpty),
                nameof(HasNoMatches),
                nameof(CountText),
                nameof(ActiveCall),
                nameof(BlockRules),
                nameof(SpamDatabaseStatus),
                nameof(FilteredSpamEntries));
        }

        private void RefreshCallLogAnnotations()
        {
            foreach (CallLogItemViewModel item in _callLog)
            {
                item.SpamInfo = _spamService?.LookupLocal(item.Number);
                item.BlockedRule = _spamService?.FindMatchingBlockRule(item.Number);
            }
            OnPropertyChanged(nameof(FilteredCallLog), nameof(RecentCallsPreview));
        }

        /// <summary>Handles a pending dial intent (e.g. tel: link).</summary>
        public void HandleIntent(PhoneIntentRequest request)
        {
            if (request == null)
                return;

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                DialedNumber = request.PhoneNumber.Trim();
                SubView = PhoneSubView.Numpad;
            }
            OnPropertyChanged(nameof(ActiveCall));
        }

        private void PressDigit(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            DialedNumber = (_dialedNumber ?? string.Empty) + token;
            if (token.Length == 1 && ActiveCall != null)
                _phoneService.SendDtmf(token[0]);
        }

        private void Backspace()
        {
            if (string.IsNullOrEmpty(_dialedNumber))
                return;
            DialedNumber = _dialedNumber.Substring(0, _dialedNumber.Length - 1);
        }

        /// <summary>Places a call to the number currently in the numpad (or copies last dialed number if empty).</summary>
        public async Task PlaceCallAsync()
        {
            if (string.IsNullOrWhiteSpace(_dialedNumber))
            {
                if (_callLog.Count > 0)
                    DialedNumber = _callLog[0].Number;
                return;
            }

            bool ok = await _phoneService.PlaceCallAsync(_dialedNumber);
            if (!ok)
                _feedbackService?.ShowToast(_languageService["dialer_call_failed"], FeedbackSeverity.Warning);
            await ReloadAsync();
        }

        /// <summary>Places a call to a specific number (e.g. from call history).</summary>
        public async Task CallSpecificNumberAsync(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return;
            DialedNumber = number;
            await PlaceCallAsync();
        }

        private async Task CopyNumberAsync(string number)
        {
            string target = string.IsNullOrWhiteSpace(number) ? _dialedNumber : number;
            if (string.IsNullOrWhiteSpace(target))
                return;
            await _clipboardService.SetTextAsync(target);
            _feedbackService?.ShowToast(_languageService.LoadTextFmt("contact_copied", target));
        }

        /// <summary>Requests call log and phone permissions from the user.</summary>
        public async Task GrantPermissionAsync()
        {
            HasPermission = await _phoneService.RequestPermissionsAsync();
            await ReloadAsync();
        }

        /// <summary>Requests the system to make aNote the default Phone app.</summary>
        public async Task SetDefaultAppAsync()
        {
            await _phoneService.RequestPermissionsAsync();
            await _phoneService.RequestDefaultDialerAppAsync();
            await ReloadAsync();
        }

        /// <summary>Deletes a single call log entry.</summary>
        public async Task DeleteEntryAsync(CallLogItemViewModel item)
        {
            if (item == null)
                return;
            await _phoneService.DeleteCallLogEntryAsync(item.Model.Id);
            await ReloadAsync();
        }

        /// <summary>Clears the entire call log after confirmation.</summary>
        public async Task ClearHistoryAsync()
        {
            MessageBoxResult answer = await _feedbackService.ShowMessageAsync(
                _languageService["dialer_clear_confirm"],
                _languageService["dialer_clear_history"],
                MessageBoxButtons.YesNoCancel,
                false);
            if (answer != MessageBoxResult.Yes)
                return;

            await _phoneService.ClearCallLogAsync();
            await ReloadAsync();
        }

        private static string NormalizeDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            string digits = new string(input.Where(char.IsDigit).ToArray());
            if (digits.Length > 9)
                digits = digits.Substring(digits.Length - 9);
            return digits;
        }
    }

    /// <summary>
    /// Item view model for a single call history entry.
    /// </summary>
    public class CallLogItemViewModel
    {
        public CallLogItemViewModel(
            CallLogEntryModel model,
            PhoneSpamEntryModel spamInfo = null,
            PhoneBlockRuleModel blockedRule = null)
        {
            Model = model;
            SpamInfo = spamInfo;
            BlockedRule = blockedRule;
        }

        public CallLogEntryModel Model { get; }

        public PhoneSpamEntryModel SpamInfo { get; set; }

        public PhoneBlockRuleModel BlockedRule { get; set; }

        public string Number => string.IsNullOrWhiteSpace(Model.Number) ? "Unknown" : Model.Number;

        public string Title => string.IsNullOrWhiteSpace(Model.DisplayName) ? Number : Model.DisplayName;

        public string Subtitle => string.IsNullOrWhiteSpace(Model.DisplayName) ? null : Model.Number;

        public CallDirection Direction => Model.Direction;

        public bool IsMissed => Model.Direction == CallDirection.Missed || Model.Direction == CallDirection.Rejected;

        public bool IsBlocked => BlockedRule != null;

        public bool IsSpam => SpamInfo?.IsSpam ?? false;

        public string TimeText
        {
            get
            {
                DateTime local = Model.DateUtc.ToLocalTime();
                return (local.Date == DateTime.Today)
                    ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        public string DurationText
        {
            get
            {
                long sec = Model.DurationSeconds;
                if (sec <= 0)
                    return null;
                if (sec < 60)
                    return sec + "s";
                long min = sec / 60;
                long rem = sec % 60;
                return min + "m " + rem + "s";
            }
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            if (!string.IsNullOrEmpty(Model.DisplayName) && Model.DisplayName.ToLowerInvariant().Contains(query))
                return true;
            if (!string.IsNullOrEmpty(Model.Number) && Model.Number.ToLowerInvariant().Replace(" ", "").Contains(query.Replace(" ", "")))
                return true;
            if (SpamInfo != null && !string.IsNullOrEmpty(SpamInfo.SummaryText) && SpamInfo.SummaryText.ToLowerInvariant().Contains(query))
                return true;
            return false;
        }
    }
}
