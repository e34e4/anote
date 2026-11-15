// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>Kind of the source which should be transformed.</summary>
    public enum TextTransformSourceKind
    {
        Note,
        Clipboard,
    }

    /// <summary>A note or clipboard item which can be processed by the text pipeline.</summary>
    public class TextTransformSourceItem : ViewModelBase
    {
        private bool _isSelected;

        /// <summary>Gets or sets the id of the note or clipboard item.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the display title.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the secondary description.</summary>
        public string SubTitle { get; set; }

        /// <summary>Gets or sets the kind of the source.</summary>
        public TextTransformSourceKind Kind { get; set; }

        /// <summary>Gets or sets a value indicating whether this item is selected for processing.</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    /// <summary>A transformation which can be enabled for the pipeline.</summary>
    public class TextTransformStepItem : ViewModelBase
    {
        private bool _isSelected;
        private string _parameter;

        /// <summary>Gets the transformation kind.</summary>
        public TextTransformKind Kind { get; set; }

        /// <summary>Gets the display name.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets a value indicating whether the step needs a parameter.</summary>
        public bool RequiresParameter { get; set; }

        /// <summary>Gets or sets a value indicating whether the step is part of the pipeline.</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>Gets or sets the parameter of the step (the regular expression).</summary>
        public string Parameter
        {
            get { return _parameter; }
            set { SetProperty(ref _parameter, value); }
        }
    }

    /// <summary>The result of the pipeline for a single source item.</summary>
    public class TextTransformResultItem
    {
        /// <summary>Gets or sets the processed source.</summary>
        public TextTransformSourceItem Source { get; set; }

        /// <summary>Gets or sets the transformed text.</summary>
        public string Output { get; set; }

        /// <summary>Gets or sets the error message, or null if the pipeline succeeded.</summary>
        public string Error { get; set; }

        /// <summary>Gets a value indicating whether the pipeline succeeded.</summary>
        public bool Success
        {
            get { return string.IsNullOrEmpty(Error); }
        }

        /// <summary>Gets the title shown in the result list.</summary>
        public string Title
        {
            get { return Source?.Title ?? string.Empty; }
        }
    }

    /// <summary>
    /// View model of the batch text-processing sheet. It applies a pipeline of text
    /// transformations to selected notes and clipboard items.
    /// </summary>
    public class TextTransformViewModel : ViewModelBase
    {
        private readonly IRepositoryStorageService _repositoryStorageService;
        private readonly IClipboardHistoryService _clipboardHistoryService;
        private readonly IClipboardService _clipboardService;
        private readonly IFeedbackService _feedbackService;
        private bool _isBusy;
        private string _statusText = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextTransformViewModel"/> class.
        /// </summary>
        public TextTransformViewModel(
            IRepositoryStorageService repositoryStorageService,
            IClipboardHistoryService clipboardHistoryService,
            IClipboardService clipboardService,
            IFeedbackService feedbackService)
        {
            _repositoryStorageService = repositoryStorageService;
            _clipboardHistoryService = clipboardHistoryService;
            _clipboardService = clipboardService;
            _feedbackService = feedbackService;

            foreach (TextTransformKind kind in TextTransformUtils.AllKinds)
            {
                Steps.Add(new TextTransformStepItem
                {
                    Kind = kind,
                    DisplayName = TextTransformUtils.GetDisplayName(kind),
                    RequiresParameter = TextTransformUtils.RequiresParameter(kind),
                });
            }

            RunCommand = new RelayCommand(Run);
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            ClearResultsCommand = new RelayCommand(() => Results.Clear());
            CopyResultCommand = new AsyncRelayCommand<TextTransformResultItem>(CopyResultAsync);
            CopyAllCommand = new AsyncRelayCommand(CopyAllAsync);
            SaveToNoteCommand = new RelayCommand<TextTransformResultItem>(SaveToNote);
            AddToClipboardCommand = new RelayCommand<TextTransformResultItem>(AddToClipboard);
            Reload();
        }

        /// <summary>Gets the notes which can be processed.</summary>
        public ObservableCollection<TextTransformSourceItem> Notes { get; } = new ObservableCollection<TextTransformSourceItem>();

        /// <summary>Gets the clipboard items which can be processed.</summary>
        public ObservableCollection<TextTransformSourceItem> Clips { get; } = new ObservableCollection<TextTransformSourceItem>();

        /// <summary>Gets all available transformations.</summary>
        public ObservableCollection<TextTransformStepItem> Steps { get; } = new ObservableCollection<TextTransformStepItem>();

        /// <summary>Gets the results of the last run.</summary>
        public ObservableCollection<TextTransformResultItem> Results { get; } = new ObservableCollection<TextTransformResultItem>();

        /// <summary>Gets a value indicating whether the pipeline is running.</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        /// <summary>Gets the status text of the last run.</summary>
        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        /// <summary>Gets the command which executes the pipeline.</summary>
        public IRelayCommand RunCommand { get; }

        /// <summary>Gets the command which selects all source items.</summary>
        public IRelayCommand SelectAllCommand { get; }

        /// <summary>Gets the command which clears the source selection.</summary>
        public IRelayCommand ClearSelectionCommand { get; }

        /// <summary>Gets the command which clears the results.</summary>
        public IRelayCommand ClearResultsCommand { get; }

        /// <summary>Gets the command which copies a single result to the system clipboard.</summary>
        public IAsyncRelayCommand<TextTransformResultItem> CopyResultCommand { get; }

        /// <summary>Gets the command which copies all results to the system clipboard.</summary>
        public IAsyncRelayCommand CopyAllCommand { get; }

        /// <summary>Gets the command which stores a result as the new content of a note.</summary>
        public IRelayCommand<TextTransformResultItem> SaveToNoteCommand { get; }

        /// <summary>Gets the command which adds a result as a new clipboard item.</summary>
        public IRelayCommand<TextTransformResultItem> AddToClipboardCommand { get; }

        /// <summary>
        /// Reloads the notes and clipboard items from the local storage.
        /// </summary>
        public void Reload()
        {
            Notes.Clear();
            Clips.Clear();

            _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            foreach (NoteModel note in repository.Notes.Where(note => !note.InRecyclingBin).OrderByDescending(note => note.ModifiedAt))
            {
                bool locked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty;
                string title = locked ? "Encrypted note" : NoteHtmlUtils.GetTitle(note.HtmlContent);
                Notes.Add(new TextTransformSourceItem
                {
                    Id = note.Id,
                    Title = string.IsNullOrWhiteSpace(title) ? "Untitled note" : title,
                    SubTitle = note.ModifiedAt.ToString("yyyy-MM-dd HH:mm"),
                    Kind = TextTransformSourceKind.Note,
                });
            }

            ClipboardHistoryModel history = _clipboardHistoryService.LoadHistoryOrDefault();
            foreach (ClipboardItemModel item in history.Items
                .Where(item => !item.IsDeleted && item.Kind == ClipboardItemKind.Text && !string.IsNullOrEmpty(item.Text))
                .OrderByDescending(item => item.CreatedUtc)
                .Take(200))
            {
                string preview = item.Text.Length > 80 ? item.Text.Substring(0, 80) + "…" : item.Text;
                Clips.Add(new TextTransformSourceItem
                {
                    Id = item.Id,
                    Title = preview,
                    SubTitle = item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    Kind = TextTransformSourceKind.Clipboard,
                });
            }
        }

        /// <summary>
        /// Preselects a source item, e.g. the note which was open when the page was opened.
        /// </summary>
        /// <param name="noteId">Id of the note to select, or null.</param>
        /// <param name="clipboardId">Id of the clipboard item to select, or null.</param>
        public void SelectSource(Guid? noteId, Guid? clipboardId)
        {
            foreach (TextTransformSourceItem item in Notes.Concat(Clips))
                item.IsSelected = false;

            if (noteId.HasValue)
            {
                TextTransformSourceItem note = Notes.FirstOrDefault(item => item.Id == noteId.Value);
                if (note != null)
                    note.IsSelected = true;
            }
            if (clipboardId.HasValue)
            {
                TextTransformSourceItem clip = Clips.FirstOrDefault(item => item.Id == clipboardId.Value);
                if (clip != null)
                    clip.IsSelected = true;
            }
        }

        /// <summary>
        /// Runs the configured pipeline on all selected items.
        /// </summary>
        public void Run()
        {
            Results.Clear();
            List<TextTransformSourceItem> selected = Notes.Concat(Clips).Where(item => item.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText = "Select at least one note or clipboard item.";
                return;
            }

            List<TextTransformStep> pipeline = Steps
                .Where(step => step.IsSelected)
                .Select(step => new TextTransformStep { Kind = step.Kind, Parameter = step.Parameter })
                .ToList();
            if (pipeline.Count == 0)
            {
                StatusText = "Select at least one transformation.";
                return;
            }

            int successCount = 0;
            foreach (TextTransformSourceItem source in selected)
            {
                string input = GetInput(source);
                if (input == null)
                {
                    Results.Add(new TextTransformResultItem { Source = source, Error = "The note is protected by a safe and cannot be read." });
                    continue;
                }

                if (TextTransformUtils.TryApply(input, pipeline, out string output, out string error))
                {
                    Results.Add(new TextTransformResultItem { Source = source, Output = output });
                    successCount++;
                }
                else
                {
                    Results.Add(new TextTransformResultItem { Source = source, Error = error });
                }
            }

            StatusText = string.Format("{0} of {1} items transformed.", successCount, selected.Count);
        }

        private string GetInput(TextTransformSourceItem source)
        {
            if (source.Kind == TextTransformSourceKind.Note)
            {
                _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                NoteModel note = repository.Notes.FindById(source.Id);
                if (note == null)
                    return null;
                if (note.SafeId.HasValue && note.SafeId.Value != Guid.Empty)
                    return null;
                return NoteHtmlUtils.GetPlainText(note.HtmlContent);
            }
            else
            {
                ClipboardHistoryModel history = _clipboardHistoryService.LoadHistoryOrDefault();
                return history.Items.FirstOrDefault(item => item.Id == source.Id)?.Text;
            }
        }

        private void SelectAll()
        {
            foreach (TextTransformSourceItem item in Notes.Concat(Clips))
                item.IsSelected = true;
        }

        private void ClearSelection()
        {
            foreach (TextTransformSourceItem item in Notes.Concat(Clips))
                item.IsSelected = false;
        }

        private async Task CopyResultAsync(TextTransformResultItem result)
        {
            if (result == null || !result.Success)
                return;
            await _clipboardService.SetTextAsync(result.Output);
            _feedbackService.ShowToast("Copied to the clipboard", FeedbackSeverity.Success);
        }

        private async Task CopyAllAsync()
        {
            List<TextTransformResultItem> results = Results.Where(result => result.Success).ToList();
            if (results.Count == 0)
                return;
            string text = string.Join("\n\n", results.Select(result => result.Output));
            await _clipboardService.SetTextAsync(text);
            _feedbackService.ShowToast(string.Format("{0} results copied", results.Count), FeedbackSeverity.Success);
        }

        private void SaveToNote(TextTransformResultItem result)
        {
            if (result == null || !result.Success || result.Source.Kind != TextTransformSourceKind.Note)
                return;

            _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            NoteModel note = repository.Notes.FindById(result.Source.Id);
            if (note == null)
                return;
            if (note.SafeId.HasValue && note.SafeId.Value != Guid.Empty)
            {
                _feedbackService.ShowToast("Protected notes must be opened before they can be changed.", FeedbackSeverity.Warning);
                return;
            }

            note.HtmlContent = PlainTextToHtml(result.Output);
            note.RefreshModifiedAt();
            _repositoryStorageService.TrySaveRepository(repository);
            _feedbackService.ShowToast("Note replaced with the transformed text", FeedbackSeverity.Success);
        }

        private void AddToClipboard(TextTransformResultItem result)
        {
            if (result == null || !result.Success)
                return;
            _clipboardHistoryService.AddText(result.Output, 500);
            _feedbackService.ShowToast("Added to the clipboard history", FeedbackSeverity.Success);
        }

        private static string PlainTextToHtml(string text)
        {
            var builder = new StringBuilder();
            foreach (string line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                builder.Append("<p>");
                builder.Append(WebUtility.HtmlEncode(line));
                builder.Append("</p>");
            }
            return builder.ToString();
        }
    }
}
