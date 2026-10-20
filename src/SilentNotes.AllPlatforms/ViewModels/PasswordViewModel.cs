// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Password vault tab.
    /// </summary>
    public class PasswordViewModel : ViewModelBase
    {
        private readonly IPasswordVaultService _vaultService;
        private readonly ILanguageService _languageService;
        private readonly IClipboardService _clipboardService;
        private readonly IFilePickerService _filePickerService;
        private readonly IFeedbackService _feedbackService;
        private readonly INativeBrowserService _browserService;

        private string _filter;
        private List<PasswordEntryItemViewModel> _entries = new List<PasswordEntryItemViewModel>();
        private Guid? _editingEntryId;
        private SecureString _masterPassword;
        private SecureString _masterPasswordConfirmation;
        private SecureString _pin;
        private SecureString _pinConfirmation;
        private string _errorText;
        private bool _showPinSetup;
        private bool _showPassword;
        private string _draftName;
        private string _draftUsername;
        private string _draftPassword;
        private string _draftUris;
        private string _draftNotes;
        private string _draftTotpSecret;
        private DateTime _totpTick = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordViewModel"/> class.
        /// </summary>
        public PasswordViewModel(
            IPasswordVaultService vaultService,
            ILanguageService languageService,
            IClipboardService clipboardService,
            IFilePickerService filePickerService,
            IFeedbackService feedbackService,
            INativeBrowserService browserService)
        {
            _vaultService = vaultService;
            _languageService = languageService;
            _clipboardService = clipboardService;
            _filePickerService = filePickerService;
            _feedbackService = feedbackService;
            _browserService = browserService;

            CreateVaultCommand = new RelayCommand(CreateVault);
            UnlockMasterCommand = new RelayCommand(UnlockMaster);
            UnlockPinCommand = new RelayCommand(UnlockPin);
            LockCommand = new RelayCommand(LockVault);
            SetPinCommand = new RelayCommand(SetPin);
            ClearPinCommand = new RelayCommand(ClearPin);
            ShowPinSetupCommand = new RelayCommand(() => { ShowPinSetup = true; ErrorText = null; });
            CancelPinSetupCommand = new RelayCommand(() => { ShowPinSetup = false; Pin = null; PinConfirmation = null; });
            AddCommand = new RelayCommand(Add);
            DeleteCommand = new RelayCommand<PasswordEntryItemViewModel>(Delete);
            StartEditCommand = new RelayCommand<PasswordEntryItemViewModel>(StartEdit);
            SaveEntryCommand = new RelayCommand(SaveEntry);
            CancelEditCommand = new RelayCommand(CancelEdit);
            CopyUsernameCommand = new AsyncRelayCommand<PasswordEntryItemViewModel>(CopyUsername);
            CopyPasswordCommand = new AsyncRelayCommand<PasswordEntryItemViewModel>(CopyPassword);
            CopyTotpCommand = new AsyncRelayCommand<PasswordEntryItemViewModel>(CopyTotp);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            OpenAutofillSettingsCommand = new RelayCommand(OpenAutofillSettings);
            TickTotpCommand = new RelayCommand(TickTotp);
        }

        public ICommand CreateVaultCommand { get; }
        public ICommand UnlockMasterCommand { get; }
        public ICommand UnlockPinCommand { get; }
        public ICommand LockCommand { get; }
        public ICommand SetPinCommand { get; }
        public ICommand ClearPinCommand { get; }
        public ICommand ShowPinSetupCommand { get; }
        public ICommand CancelPinSetupCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand StartEditCommand { get; }
        public ICommand SaveEntryCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand CopyUsernameCommand { get; }
        public ICommand CopyPasswordCommand { get; }
        public ICommand CopyTotpCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand OpenAutofillSettingsCommand { get; }
        public ICommand TickTotpCommand { get; }

        public bool VaultExists => _vaultService.VaultExists;
        public bool IsUnlocked => _vaultService.IsUnlocked;
        public bool HasPin => _vaultService.HasPin;
        public bool NeedsSetup => !VaultExists;
        public bool NeedsUnlock => VaultExists && !IsUnlocked;

        public SecureString MasterPassword
        {
            get => _masterPassword;
            set
            {
                _masterPassword?.Clear();
                _masterPassword = value;
            }
        }

        public SecureString MasterPasswordConfirmation
        {
            get => _masterPasswordConfirmation;
            set
            {
                _masterPasswordConfirmation?.Clear();
                _masterPasswordConfirmation = value;
            }
        }

        public SecureString Pin
        {
            get => _pin;
            set
            {
                _pin?.Clear();
                _pin = value;
            }
        }

        public SecureString PinConfirmation
        {
            get => _pinConfirmation;
            set
            {
                _pinConfirmation?.Clear();
                _pinConfirmation = value;
            }
        }

        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        public bool ShowPinSetup
        {
            get => _showPinSetup;
            set => SetProperty(ref _showPinSetup, value);
        }

        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    NotifyList();
            }
        }

        public IReadOnlyList<PasswordEntryItemViewModel> FilteredEntries
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _entries;
                return _entries.Where(e => e.Matches(query)).ToList();
            }
        }

        public bool IsEmpty => IsUnlocked && _entries.Count == 0;
        public bool HasNoMatches => IsUnlocked && !IsEmpty && FilteredEntries.Count == 0;

        public string CountText
        {
            get
            {
                if (!IsUnlocked)
                    return string.Empty;
                if (!string.IsNullOrWhiteSpace(_filter))
                    return _languageService.LoadTextFmt("password_count_filtered", FilteredEntries.Count, _entries.Count);
                return _languageService.LoadTextFmt("password_count", _entries.Count);
            }
        }

        public Guid? EditingEntryId
        {
            get => _editingEntryId;
            private set
            {
                if (SetProperty(ref _editingEntryId, value))
                    OnPropertyChanged(nameof(IsEditing));
            }
        }

        public bool IsEditing => _editingEntryId != null;

        public string DraftName
        {
            get => _draftName;
            set => SetProperty(ref _draftName, value);
        }

        public string DraftUsername
        {
            get => _draftUsername;
            set => SetProperty(ref _draftUsername, value);
        }

        public string DraftPassword
        {
            get => _draftPassword;
            set => SetProperty(ref _draftPassword, value);
        }

        public string DraftUris
        {
            get => _draftUris;
            set => SetProperty(ref _draftUris, value);
        }

        public string DraftNotes
        {
            get => _draftNotes;
            set => SetProperty(ref _draftNotes, value);
        }

        public string DraftTotpSecret
        {
            get => _draftTotpSecret;
            set => SetProperty(ref _draftTotpSecret, value);
        }

        public bool IsEditingOf(PasswordEntryItemViewModel entry)
        {
            return entry != null && _editingEntryId == entry.Id;
        }

        public void RefreshState()
        {
            OnPropertyChanged(
                nameof(VaultExists),
                nameof(IsUnlocked),
                nameof(HasPin),
                nameof(NeedsSetup),
                nameof(NeedsUnlock),
                nameof(IsEmpty),
                nameof(HasNoMatches),
                nameof(CountText),
                nameof(FilteredEntries));
        }

        public void Reload()
        {
            if (!_vaultService.IsUnlocked || _vaultService.Vault == null)
            {
                _entries = new List<PasswordEntryItemViewModel>();
            }
            else
            {
                _entries = _vaultService.Vault.Entries
                    .OrderBy(e => e.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new PasswordEntryItemViewModel(e, () => _totpTick))
                    .ToList();
            }
            NotifyList();
            RefreshState();
        }

        private void NotifyList()
        {
            OnPropertyChanged(nameof(FilteredEntries), nameof(IsEmpty), nameof(HasNoMatches), nameof(CountText));
        }

        private void CreateVault()
        {
            ErrorText = null;
            if (MasterPassword == null || MasterPassword.Length < 5)
            {
                ErrorText = _languageService["password_short_error"];
                return;
            }
            if (!MasterPassword.AreEqual(MasterPasswordConfirmation))
            {
                ErrorText = _languageService["password_confirmation_error"];
                return;
            }
            if (!_vaultService.CreateVault(MasterPassword))
            {
                ErrorText = _languageService["password_vault_create_failed"];
                return;
            }
            ClearUnlockFields();
            Reload();
            _feedbackService.ShowToast(_languageService["password_vault_created"]);
        }

        private void UnlockMaster()
        {
            ErrorText = null;
            if (MasterPassword == null || MasterPassword.Length < 1)
            {
                ErrorText = _languageService["password_required"];
                return;
            }
            if (!_vaultService.UnlockWithMaster(MasterPassword))
            {
                ErrorText = _languageService["password_wrong_error"];
                return;
            }
            ClearUnlockFields();
            Reload();
        }

        private void UnlockPin()
        {
            ErrorText = null;
            if (Pin == null || Pin.Length < 5)
            {
                ErrorText = _languageService["password_pin_short"];
                return;
            }
            if (!_vaultService.UnlockWithPin(Pin))
            {
                ErrorText = _languageService["password_pin_wrong"];
                return;
            }
            ClearUnlockFields();
            Reload();
        }

        private void LockVault()
        {
            CancelEdit();
            _vaultService.Lock();
            ShowPinSetup = false;
            Reload();
        }

        private void SetPin()
        {
            ErrorText = null;
            if (!_vaultService.IsUnlocked)
                return;
            if (Pin == null || Pin.Length < 5)
            {
                ErrorText = _languageService["password_pin_short"];
                return;
            }
            if (!Pin.AreEqual(PinConfirmation))
            {
                ErrorText = _languageService["password_pin_confirm_error"];
                return;
            }
            if (!_vaultService.SetPin(Pin))
            {
                ErrorText = _languageService["password_pin_set_failed"];
                return;
            }
            Pin = null;
            PinConfirmation = null;
            ShowPinSetup = false;
            RefreshState();
            _feedbackService.ShowToast(_languageService["password_pin_set"]);
        }

        private void ClearPin()
        {
            _vaultService.ClearPin();
            ShowPinSetup = false;
            RefreshState();
            _feedbackService.ShowToast(_languageService["password_pin_cleared"]);
        }

        private void Add()
        {
            if (!_vaultService.IsUnlocked)
                return;
            var entry = new PasswordEntryModel { Name = string.Empty };
            _vaultService.Vault.Entries.Add(entry);
            _vaultService.Save();
            Reload();
            StartEdit(_entries.FirstOrDefault(e => e.Id == entry.Id));
        }

        private void Delete(PasswordEntryItemViewModel item)
        {
            if (!_vaultService.IsUnlocked || item == null)
                return;
            var model = _vaultService.Vault.FindById(item.Id);
            if (model == null)
                return;
            _vaultService.Vault.Entries.Remove(model);
            _vaultService.Save();
            if (_editingEntryId == item.Id)
                CancelEdit();
            Reload();
        }

        private void StartEdit(PasswordEntryItemViewModel item)
        {
            if (item == null)
                return;
            EditingEntryId = item.Id;
            DraftName = item.Name;
            DraftUsername = item.Username;
            DraftPassword = item.Password;
            DraftUris = string.Join("\n", item.Uris);
            DraftNotes = item.Notes;
            DraftTotpSecret = item.TotpSecret;
            ShowPassword = false;
        }

        private void SaveEntry()
        {
            if (!_vaultService.IsUnlocked || _editingEntryId == null)
                return;
            var model = _vaultService.Vault.FindById(_editingEntryId.Value);
            if (model == null)
                return;
            model.Name = DraftName?.Trim();
            model.Username = DraftUsername?.Trim();
            model.Password = DraftPassword;
            model.Notes = DraftNotes;
            model.TotpSecret = string.IsNullOrWhiteSpace(DraftTotpSecret) ? null : DraftTotpSecret.Trim();
            model.Uris.Clear();
            if (!string.IsNullOrWhiteSpace(DraftUris))
            {
                foreach (string line in DraftUris.Replace("\r\n", "\n").Split('\n'))
                {
                    string uri = line.Trim();
                    if (!string.IsNullOrEmpty(uri))
                        model.Uris.Add(uri);
                }
            }
            if (!string.IsNullOrEmpty(model.TotpSecret) &&
                TotpService.TryParseSecret(model.TotpSecret, out _, out int period, out int digits))
            {
                model.TotpPeriod = period;
                model.TotpDigits = digits;
            }
            model.RefreshModifiedAt();
            _vaultService.Save();
            CancelEdit();
            Reload();
        }

        private void CancelEdit()
        {
            EditingEntryId = null;
            DraftName = DraftUsername = DraftPassword = DraftUris = DraftNotes = DraftTotpSecret = null;
        }

        private async Task CopyUsername(PasswordEntryItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.Username))
                return;
            await _clipboardService.SetTextAsync(item.Username);
            _feedbackService.ShowToast(_languageService["password_copied_username"]);
        }

        private async Task CopyPassword(PasswordEntryItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.Password))
                return;
            await _clipboardService.SetTextAsync(item.Password);
            _feedbackService.ShowToast(_languageService["password_copied_password"]);
        }

        private async Task CopyTotp(PasswordEntryItemViewModel item)
        {
            if (item == null)
                return;
            string code = item.TotpCode;
            if (string.IsNullOrEmpty(code))
                return;
            await _clipboardService.SetTextAsync(code);
            _feedbackService.ShowToast(_languageService["password_copied_totp"]);
        }

        private async Task ImportAsync()
        {
            if (!_vaultService.IsUnlocked)
                return;
            try
            {
                bool picked = await _filePickerService.PickFile(new[] { ".json", ".csv", ".txt" }, "*/*");
                if (!picked)
                    return;
                byte[] content = await _filePickerService.ReadPickedFile();
                string text = (content == null) ? string.Empty : System.Text.Encoding.UTF8.GetString(content);
                List<PasswordEntryModel> imported = BitwardenImporter.Import(text);
                if (imported == null)
                {
                    _feedbackService.ShowToast(_languageService["password_import_failed"]);
                    return;
                }

                int added = 0;
                foreach (PasswordEntryModel candidate in imported)
                {
                    bool duplicate = _vaultService.Vault.Entries.Any(e => BitwardenImporter.IsDuplicate(e, candidate));
                    if (duplicate)
                        continue;
                    _vaultService.Vault.Entries.Add(candidate);
                    added++;
                }
                _vaultService.Save();
                Reload();
                _feedbackService.ShowToast(_languageService.LoadTextFmt("password_imported", added));
            }
            catch
            {
                _feedbackService.ShowToast(_languageService["password_import_failed"]);
            }
        }

        private void OpenAutofillSettings()
        {
            _browserService.OpenAutofillServiceSettings();
        }

        private void TickTotp()
        {
            _totpTick = DateTime.UtcNow;
            foreach (var entry in _entries)
                entry.RefreshTotp();
        }

        private void ClearUnlockFields()
        {
            MasterPassword = null;
            MasterPasswordConfirmation = null;
            Pin = null;
            PinConfirmation = null;
            ErrorText = null;
        }
    }

    /// <summary>
    /// Presentation wrapper for a vault entry.
    /// </summary>
    public class PasswordEntryItemViewModel : ViewModelBase
    {
        private readonly PasswordEntryModel _model;
        private readonly Func<DateTime> _nowUtc;
        private string _totpCode;
        private int _totpRemaining;

        public PasswordEntryItemViewModel(PasswordEntryModel model, Func<DateTime> nowUtc)
        {
            _model = model;
            _nowUtc = nowUtc;
            RefreshTotp();
        }

        public Guid Id => _model.Id;
        public string Name => _model.Name;
        public string Username => _model.Username;
        public string Password => _model.Password;
        public string Notes => _model.Notes;
        public string TotpSecret => _model.TotpSecret;
        public IReadOnlyList<string> Uris => _model.Uris;
        public bool HasTotp => !string.IsNullOrWhiteSpace(_model.TotpSecret);

        public string PrimaryUri
        {
            get { return _model.Uris.FirstOrDefault(); }
        }

        public string TotpCode
        {
            get => _totpCode;
            private set => SetProperty(ref _totpCode, value);
        }

        public int TotpRemaining
        {
            get => _totpRemaining;
            private set => SetProperty(ref _totpRemaining, value);
        }

        public void RefreshTotp()
        {
            if (!HasTotp)
            {
                TotpCode = null;
                TotpRemaining = 0;
                return;
            }
            DateTime now = _nowUtc();
            TotpCode = TotpService.GenerateCode(_model.TotpSecret, _model.TotpPeriod, _model.TotpDigits, now);
            TotpRemaining = TotpService.SecondsRemaining(_model.TotpPeriod > 0 ? _model.TotpPeriod : 30, now);
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            if ((Name ?? string.Empty).ToLowerInvariant().Contains(query))
                return true;
            if ((Username ?? string.Empty).ToLowerInvariant().Contains(query))
                return true;
            if ((Notes ?? string.Empty).ToLowerInvariant().Contains(query))
                return true;
            return Uris.Any(u => (u ?? string.Empty).ToLowerInvariant().Contains(query));
        }
    }
}
