// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the pinned Voice tab: assistant opt-in and the shared Voice capture note.
    /// </summary>
    public class VoiceViewModel : ViewModelBase
    {
        /// <summary>Stable main-view tab index for Voice.</summary>
        public const int TabIndex = 20;

        private readonly NoteRepositoryModel _model;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly INavigationService _navigationService;
        private readonly IAssistantService _assistantService;
        private readonly IFeedbackService _feedbackService;
        private string _previewHtml;
        private Guid _voiceNoteId;

        /// <summary>
        /// Initializes a new instance of the <see cref="VoiceViewModel"/> class.
        /// </summary>
        public VoiceViewModel(
            NoteRepositoryModel model,
            ILanguageService languageService,
            IRepositoryStorageService repositoryService,
            INavigationService navigationService,
            IAssistantService assistantService,
            IFeedbackService feedbackService)
        {
            Language = languageService;
            _model = model;
            _repositoryService = repositoryService;
            _navigationService = navigationService;
            _assistantService = assistantService;
            _feedbackService = feedbackService;

            OpenVoiceNoteCommand = new RelayCommand(OpenVoiceNote);
            SetDefaultAssistantCommand = new AsyncRelayCommand(SetDefaultAssistantAsync);
            OpenAssistantSettingsCommand = new RelayCommand(() => _assistantService?.OpenAssistantSettings());
            PublishLauncherShortcutsCommand = new RelayCommand(PublishLauncherShortcuts);
            RefreshCommand = new RelayCommand(Refresh);

            Refresh();
            _assistantService?.PublishLauncherShortcuts();
        }

        /// <summary>Gets the language service.</summary>
        public ILanguageService Language { get; }

        /// <summary>Gets a value indicating whether the platform supports the assistant role.</summary>
        public bool CanSetDefaultAssistant => _assistantService?.IsSupported == true;

        /// <summary>Gets a value indicating whether aNote is the default assistant.</summary>
        public bool IsDefaultAssistant => _assistantService?.IsDefaultAssistant == true;

        /// <summary>Gets HTML preview of the Voice note (unlocked content assumed plain for this note).</summary>
        public string PreviewHtml
        {
            get { return _previewHtml; }
            private set { SetProperty(ref _previewHtml, value); }
        }

        /// <summary>Gets the command which opens the Voice note editor.</summary>
        public ICommand OpenVoiceNoteCommand { get; }

        /// <summary>Gets the command which requests the assistant role.</summary>
        public ICommand SetDefaultAssistantCommand { get; }

        /// <summary>Gets the command which opens assistant / home long-press settings.</summary>
        public ICommand OpenAssistantSettingsCommand { get; }

        /// <summary>Gets the command which publishes launcher shortcuts including Voice.</summary>
        public ICommand PublishLauncherShortcutsCommand { get; }

        /// <summary>Gets the command which reloads the Voice note preview.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Ensures the Voice note exists and refreshes the preview.</summary>
        public void Refresh()
        {
            NoteModel note = VoiceNoteHelper.EnsureVoiceNote(_model);
            _voiceNoteId = note.Id;
            _repositoryService.TrySaveRepository(_model);
            PreviewHtml = note.HtmlContent ?? string.Empty;
            OnPropertyChanged(nameof(IsDefaultAssistant));
            OnPropertyChanged(nameof(CanSetDefaultAssistant));
        }

        private void OpenVoiceNote()
        {
            NoteModel note = VoiceNoteHelper.EnsureVoiceNote(_model);
            _voiceNoteId = note.Id;
            _repositoryService.TrySaveRepository(_model);
            _navigationService.NavigateTo(RouteNames.Combine(RouteNames.Note, _voiceNoteId));
        }

        private async System.Threading.Tasks.Task SetDefaultAssistantAsync()
        {
            if (_assistantService == null || !_assistantService.IsSupported)
                return;

            bool ok = await _assistantService.RequestDefaultAssistantAsync();
            OnPropertyChanged(nameof(IsDefaultAssistant));
            if (ok)
                _feedbackService?.ShowToast(Language["assistant_is_default"]);
        }

        private void PublishLauncherShortcuts()
        {
            if (_assistantService == null)
                return;
            bool ok = _assistantService.PublishLauncherShortcuts();
            _feedbackService?.ShowToast(ok
                ? Language["assistant_shortcuts_published"]
                : Language["assistant_shortcuts_failed"]);
        }
    }
}
