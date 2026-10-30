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
using SilentNotes;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model which presents the phone contacts in the phone tab.
    /// </summary>
    /// <remarks>
    /// The contacts are part of the note repository, so this view model shares the same
    /// <see cref="NoteRepositoryModel"/> instance as the <see cref="NoteRepositoryViewModel"/>,
    /// and the contacts are synchronized together with the notes.
    /// </remarks>
    public class ContactsViewModel : ViewModelBase
    {
        private readonly NoteRepositoryModel _model;
        private readonly ILanguageService _languageService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly IClipboardService _clipboardService;
        private readonly INativeBrowserService _browserService;
        private readonly IFilePickerService _filePickerService;
        private readonly IFeedbackService _feedbackService;
        private readonly IMessengerService _messengerService;

        private string _filter;
        private Guid? _editingContactId;
        private string _draftNumbers;
        private List<ContactItemViewModel> _contacts = new List<ContactItemViewModel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ContactsViewModel"/> class.
        /// </summary>
        /// <param name="model">The note repository which owns the contacts.</param>
        /// <param name="languageService">A language service.</param>
        /// <param name="repositoryService">The storage service used to persist changes.</param>
        /// <param name="clipboardService">The service used to copy a phone number.</param>
        /// <param name="browserService">The service used to launch call/telegram links.</param>
        /// <param name="filePickerService">The service used to import a contacts file.</param>
        /// <param name="feedbackService">The service used to show toast messages.</param>
        /// <param name="messengerService">Used to open the SMS tab for in-app compose.</param>
        public ContactsViewModel(
            NoteRepositoryModel model,
            ILanguageService languageService,
            IRepositoryStorageService repositoryService,
            IClipboardService clipboardService,
            INativeBrowserService browserService,
            IFilePickerService filePickerService,
            IFeedbackService feedbackService,
            IMessengerService messengerService = null)
        {
            _model = model;
            _languageService = languageService;
            _repositoryService = repositoryService;
            _clipboardService = clipboardService;
            _browserService = browserService;
            _filePickerService = filePickerService;
            _feedbackService = feedbackService;
            _messengerService = messengerService;

            AddCommand = new RelayCommand(Add);
            DeleteCommand = new RelayCommand<ContactItemViewModel>(Delete);
            CopyCommand = new AsyncRelayCommand<string>(CopyPhone);
            CallCommand = new RelayCommand<string>(phone => OpenExternal("tel:" + EncodePhone(phone)));
            SmsCommand = new RelayCommand<string>(StartSms);
            TelegramCommand = new RelayCommand<string>(StartTelegram);
            StartEditNumbersCommand = new RelayCommand<ContactItemViewModel>(StartEditNumbers);
            SaveNumbersCommand = new RelayCommand(SaveNumbers);
            CancelEditNumbersCommand = new RelayCommand(CancelEditNumbers);
            ImportCommand = new AsyncRelayCommand(ImportAsync);

            Reload();
        }

        /// <summary>Gets the command to add a new contact.</summary>
        public ICommand AddCommand { get; }

        /// <summary>Gets the command to delete a contact.</summary>
        public ICommand DeleteCommand { get; }

        /// <summary>Gets the command to copy a phone number to the clipboard.</summary>
        public ICommand CopyCommand { get; }

        /// <summary>Gets the command to start a call.</summary>
        public ICommand CallCommand { get; }

        /// <summary>Gets the command to send a message.</summary>
        public ICommand SmsCommand { get; }

        /// <summary>Gets the command to open the number in Telegram.</summary>
        public ICommand TelegramCommand { get; }

        /// <summary>Gets the command to start editing the numbers of a contact.</summary>
        public ICommand StartEditNumbersCommand { get; }

        /// <summary>Gets the command to save the edited numbers.</summary>
        public ICommand SaveNumbersCommand { get; }

        /// <summary>Gets the command to cancel editing the numbers.</summary>
        public ICommand CancelEditNumbersCommand { get; }

        /// <summary>Gets the command to import contacts from a file.</summary>
        public ICommand ImportCommand { get; }

        /// <summary>
        /// Gets or sets the search filter, matching the name or one of the phone numbers.
        /// </summary>
        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilteredContacts));
                    OnPropertyChanged(nameof(IsFiltered));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(CountText));
                }
            }
        }

        /// <summary>
        /// Gets the filtered contacts.
        /// </summary>
        public IReadOnlyList<ContactItemViewModel> FilteredContacts
        {
            get
            {
                string query = _filter?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(query))
                    return _contacts;
                return _contacts.Where(c => c.Matches(query)).ToList();
            }
        }

        /// <summary>
        /// Gets a value indicating whether a search filter is active.
        /// </summary>
        public bool IsFiltered
        {
            get { return !string.IsNullOrWhiteSpace(_filter); }
        }

        /// <summary>
        /// Gets a value indicating whether there are no contacts at all.
        /// </summary>
        public bool IsEmpty
        {
            get { return _contacts.Count == 0; }
        }

        /// <summary>
        /// Gets a value indicating whether the current filter has no matches.
        /// </summary>
        public bool HasNoMatches
        {
            get { return !IsEmpty && (FilteredContacts.Count == 0); }
        }

        /// <summary>
        /// Gets the text showing the number of contacts (or matches when filtering).
        /// </summary>
        public string CountText
        {
            get
            {
                if (IsFiltered)
                    return _languageService.LoadTextFmt("contact_count_filtered", FilteredContacts.Count, _contacts.Count);
                return _languageService.LoadTextFmt("contact_count", _contacts.Count);
            }
        }

        /// <summary>
        /// Gets or sets the id of the contact whose numbers are currently being edited.
        /// </summary>
        public Guid? EditingContactId
        {
            get { return _editingContactId; }
            private set
            {
                if (SetProperty(ref _editingContactId, value))
                    OnPropertyChanged(nameof(IsEditingNumbers));
            }
        }

        /// <summary>
        /// Gets a value indicating whether the numbers of any contact are being edited.
        /// </summary>
        public bool IsEditingNumbers
        {
            get { return _editingContactId != null; }
        }

        /// <summary>
        /// Gets or sets the comma separated numbers of the contact being edited.
        /// </summary>
        public string DraftNumbers
        {
            get { return _draftNumbers; }
            set { SetProperty(ref _draftNumbers, value); }
        }

        /// <summary>
        /// Determines whether the numbers of a given contact are currently being edited.
        /// </summary>
        /// <param name="contact">The contact to check.</param>
        /// <returns>True if the contact's numbers are being edited.</returns>
        public bool IsEditingNumbersOf(ContactItemViewModel contact)
        {
            return (contact != null) && (_editingContactId == contact.Id);
        }

        /// <summary>
        /// Reloads the contacts from the shared repository model.
        /// </summary>
        public void Reload()
        {
            _contacts = _model.Contacts
                .Select((contact, index) => new ContactItemViewModel(contact, Persist) { Index = index + 1 })
                .ToList();

            OnPropertyChanged(nameof(FilteredContacts));
            OnPropertyChanged(nameof(IsFiltered));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasNoMatches));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEditingNumbers));
        }

        private void Persist()
        {
            _repositoryService.TrySaveRepository(_model);
        }

        private void Add()
        {
            var contact = new ContactModel { Name = string.Empty };
            _model.Contacts.Add(contact);
            Persist();
            Reload();

            EditingContactId = contact.Id;
            DraftNumbers = string.Empty;
        }

        private void Delete(ContactItemViewModel contact)
        {
            if (contact == null)
                return;
            ContactModel model = _model.Contacts.FindById(contact.Id);
            if (model == null)
                return;

            _model.Contacts.Remove(model);
            Persist();
            if (_editingContactId == contact.Id)
                CancelEditNumbers();
            Reload();
        }

        private void StartEditNumbers(ContactItemViewModel contact)
        {
            if (contact == null)
                return;
            EditingContactId = contact.Id;
            DraftNumbers = string.Join(", ", contact.Phones);
        }

        private void CancelEditNumbers()
        {
            EditingContactId = null;
            DraftNumbers = null;
        }

        private void SaveNumbers()
        {
            if (_editingContactId == null)
                return;
            ContactModel model = _model.Contacts.FindById(_editingContactId.Value);
            if (model == null)
            {
                CancelEditNumbers();
                return;
            }

            model.Phones = ParsePhones(DraftNumbers);
            model.RefreshModifiedAt();
            Persist();
            CancelEditNumbers();
            Reload();
        }

        private async Task CopyPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone))
                return;
            await _clipboardService.SetTextAsync(phone);
            _feedbackService.ShowToast(_languageService.LoadTextFmt("contact_copied", phone));
        }

        private void OpenExternal(string url)
        {
            _browserService.OpenWebsite(url);
        }

        private void StartSms(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return;

            SmsIntentRequest.Pending = new SmsIntentRequest { PhoneNumber = phone.Trim() };
            if (_messengerService != null)
            {
                _messengerService.Send(new SmsIntentMessage());
                return;
            }

            // Fallback when no messenger is available (e.g. unit tests): open externally.
            OpenExternal("smsto:" + EncodePhone(phone));
        }

        private void StartTelegram(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return;

            string link = "https://t.me/" + EncodePhone(phone);
            TelegramLinkRequest.Pending = new TelegramLinkRequest { Link = link };
            if (_messengerService != null)
            {
                _messengerService.Send(new TelegramLinkMessage());
                return;
            }

            OpenExternal(link);
        }

        private async Task ImportAsync()
        {
            bool picked = await _filePickerService.PickFile(new[] { ".html", ".json", ".txt" }, "text/html");
            if (!picked)
                return;

            byte[] bytes = await _filePickerService.ReadPickedFile();
            string text = (bytes == null) ? string.Empty : System.Text.Encoding.UTF8.GetString(bytes);

            ContactListModel imported = ContactsImporter.Import(text);
            if (imported == null)
            {
                _feedbackService.ShowToast(_languageService["contact_import_failed"], FeedbackSeverity.Warning);
                return;
            }

            _model.Contacts = imported;
            Persist();
            CancelEditNumbers();
            Reload();
            _feedbackService.ShowToast(
                _languageService.LoadTextFmt("contact_imported", imported.Count), FeedbackSeverity.Info);
        }

        private static List<string> ParsePhones(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
            return text.Split(',')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
        }

        private static string EncodePhone(string phone)
        {
            return Uri.EscapeDataString(phone ?? string.Empty).Replace("%2B", "+");
        }
    }

    /// <summary>
    /// View model for a single contact.
    /// </summary>
    public class ContactItemViewModel : ViewModelBase
    {
        private readonly ContactModel _model;
        private readonly Action _persist;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContactItemViewModel"/> class.
        /// </summary>
        /// <param name="model">The wrapped contact.</param>
        /// <param name="persist">Called after the name was changed, to save the repository.</param>
        public ContactItemViewModel(ContactModel model, Action persist)
        {
            _model = model;
            _persist = persist;
        }

        /// <summary>Gets the id of the contact.</summary>
        public Guid Id
        {
            get { return _model.Id; }
        }

        /// <summary>Gets or sets the one based position of the contact in the full list.</summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the name of the contact. Setting the name persists the repository.
        /// </summary>
        public string Name
        {
            get { return _model.Name; }
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    _model.RefreshModifiedAt();
                    _persist();
                }
            }
        }

        /// <summary>Gets the phone numbers of the contact.</summary>
        public IReadOnlyList<string> Phones
        {
            get { return _model.Phones; }
        }

        /// <summary>
        /// Determines whether the contact matches a search query.
        /// </summary>
        /// <param name="query">Lower case search query.</param>
        /// <returns>True if the contact matches, otherwise false.</returns>
        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            if (!string.IsNullOrEmpty(_model.Name) && _model.Name.ToLowerInvariant().Contains(query))
                return true;

            string compact = query.Replace(" ", "");
            return _model.Phones.Any(phone =>
            {
                string value = phone.ToLowerInvariant();
                return value.Contains(query) || value.Replace(" ", "").Contains(compact);
            });
        }
    }
}
