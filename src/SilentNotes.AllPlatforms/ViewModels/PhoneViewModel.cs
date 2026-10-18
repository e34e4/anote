// Copyright © 2025 aNote.
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
    /// View model for the Phone (dialer, call history, and default phone app) tab.
    /// </summary>
    public class PhoneViewModel : ViewModelBase
    {
        private readonly IDevicePhoneService _phoneService;
        private readonly NoteRepositoryModel _repositoryModel;
        private readonly IClipboardService _clipboardService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private List<CallLogItemViewModel> _callLog = new List<CallLogItemViewModel>();
        private string _dialedNumber = string.Empty;
        private string _filter;
        private bool _isHistoryView;
        private bool _hasPermission;
        private bool _permissionRequested;

        public PhoneViewModel(
            IDevicePhoneService phoneService,
            NoteRepositoryModel repositoryModel,
            IClipboardService clipboardService,
            ILanguageService languageService,
            IFeedbackService feedbackService)
        {
            _phoneService = phoneService;
            _repositoryModel = repositoryModel;
            _clipboardService = clipboardService;
            _languageService = languageService;
            _feedbackService = feedbackService;

            _hasPermission = _phoneService.HasPermissions;

            ShowNumpadCommand = new RelayCommand(() => IsHistoryView = false);
            ShowHistoryCommand = new RelayCommand(() => IsHistoryView = true);
            DigitCommand = new RelayCommand<string>(PressDigit);
            BackspaceCommand = new RelayCommand(Backspace);
            ClearNumberCommand = new RelayCommand(() => DialedNumber = string.Empty);
            CallCommand = new AsyncRelayCommand(PlaceCallAsync);
            CallNumberCommand = new AsyncRelayCommand<string>(CallSpecificNumberAsync);
            FillNumberCommand = new RelayCommand<string>(num => { DialedNumber = num ?? string.Empty; IsHistoryView = false; });
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

        /// <summary>Gets or sets a value indicating whether the full call history view is shown.</summary>
        public bool IsHistoryView
        {
            get => _isHistoryView;
            set => SetProperty(ref _isHistoryView, value);
        }

        /// <summary>Gets or sets the number typed on the numpad.</summary>
        public string DialedNumber
        {
            get => _dialedNumber;
            set
            {
                if (SetProperty(ref _dialedNumber, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(CanCall), nameof(MatchedContactName));
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

        /// <summary>Gets or sets the search filter for call history.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    OnPropertyChanged(nameof(FilteredCallLog), nameof(HasNoMatches));
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
                _callLog = entries.Select(e => new CallLogItemViewModel(e)).ToList();
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
                nameof(ActiveCall));
        }

        /// <summary>Handles a pending dial intent (e.g. tel: link).</summary>
        public void HandleIntent(PhoneIntentRequest request)
        {
            if (request == null)
                return;

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                DialedNumber = request.PhoneNumber.Trim();
                IsHistoryView = false;
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
        public CallLogItemViewModel(CallLogEntryModel model)
        {
            Model = model;
        }

        public CallLogEntryModel Model { get; }

        public string Number => string.IsNullOrWhiteSpace(Model.Number) ? "Unknown" : Model.Number;

        public string Title => string.IsNullOrWhiteSpace(Model.DisplayName) ? Number : Model.DisplayName;

        public string Subtitle => string.IsNullOrWhiteSpace(Model.DisplayName) ? null : Model.Number;

        public CallDirection Direction => Model.Direction;

        public bool IsMissed => Model.Direction == CallDirection.Missed || Model.Direction == CallDirection.Rejected;

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
            return false;
        }
    }
}
