// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model of the manual backup page. It creates a full local backup (repository file,
    /// clipboard history, SQLite caches and Markdown/JSON bundles) in a folder chosen by the user.
    /// </summary>
    public class BackupViewModel : ViewModelBase
    {
        private readonly IManualBackupService _backupService;
        private readonly ISettingsService _settingsService;
        private readonly IFeedbackService _feedbackService;
        private readonly ILanguageService _languageService;
        private string _folder;
        private bool _isBusy;
        private string _statusText = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupViewModel"/> class.
        /// </summary>
        public BackupViewModel(
            IManualBackupService backupService,
            ISettingsService settingsService,
            IFeedbackService feedbackService,
            ILanguageService languageService)
        {
            _backupService = backupService;
            _settingsService = settingsService;
            _feedbackService = feedbackService;
            _languageService = languageService;

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _folder = string.IsNullOrWhiteSpace(settings.ManualBackupFolder)
                ? _backupService.DefaultFolder
                : settings.ManualBackupFolder;

            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
            ResetFolderCommand = new RelayCommand(ResetFolder);
        }

        /// <summary>Gets or sets the folder which receives the backup.</summary>
        public string Folder
        {
            get { return _folder; }
            set { SetProperty(ref _folder, value); }
        }

        /// <summary>Gets a value indicating whether a backup is currently running.</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        /// <summary>Gets the status text of the last backup.</summary>
        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        /// <summary>Gets the files written by the last backup.</summary>
        public ObservableCollection<string> LastFiles { get; } = new ObservableCollection<string>();

        /// <summary>Gets the command which creates the backup.</summary>
        public IAsyncRelayCommand CreateBackupCommand { get; }

        /// <summary>Gets the command which resets the folder to the default.</summary>
        public IRelayCommand ResetFolderCommand { get; }

        /// <summary>
        /// Starts a backup with the currently configured folder.
        /// </summary>
        public async Task CreateBackupAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                string folder = string.IsNullOrWhiteSpace(Folder) ? _backupService.DefaultFolder : Folder.Trim();

                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                settings.ManualBackupFolder = string.IsNullOrWhiteSpace(Folder) ? string.Empty : Folder.Trim();
                _settingsService.TrySaveSettingsToLocalDevice(settings);

                ManualBackupResult result = await _backupService.CreateBackupAsync(folder);
                LastFiles.Clear();
                if (result.Success)
                {
                    foreach (string file in result.Files)
                        LastFiles.Add(file);
                    StatusText = _languageService.LoadTextFmt("backup_done_fmt", result.Folder, result.Files.Count);
                    Folder = result.Folder;
                    _feedbackService.ShowToast(StatusText, FeedbackSeverity.Success);
                }
                else
                {
                    StatusText = _languageService.LoadTextFmt("backup_failed_fmt", result.Error);
                    _feedbackService.ShowToast(StatusText, FeedbackSeverity.Error);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ResetFolder()
        {
            Folder = _backupService.DefaultFolder;
        }
    }
}
