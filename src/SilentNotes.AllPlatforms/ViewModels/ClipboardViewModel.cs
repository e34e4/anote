// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model which presents the clipboard history in the clipboard tab.
    /// </summary>
    public class ClipboardViewModel : ViewModelBase
    {
        private readonly IClipboardHistoryService _historyService;
        private readonly IClipboardService _clipboardService;
        private List<ClipboardItemViewModel> _items = new List<ClipboardItemViewModel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardViewModel"/> class.
        /// </summary>
        public ClipboardViewModel(IClipboardHistoryService historyService, IClipboardService clipboardService)
        {
            _historyService = historyService;
            _clipboardService = clipboardService;

            CopyCommand = new AsyncRelayCommand<ClipboardItemViewModel>(CopyAsync);
            ToggleFavoriteCommand = new RelayCommand<ClipboardItemViewModel>(ToggleFavorite);
            DeleteCommand = new RelayCommand<ClipboardItemViewModel>(Delete);
            ClearAllCommand = new RelayCommand(ClearAll);
            ClearNonFavoritesCommand = new RelayCommand(ClearNonFavorites);

            Reload();
        }

        /// <summary>
        /// Gets all captured items, newest first.
        /// </summary>
        public IReadOnlyList<ClipboardItemViewModel> AllItems
        {
            get { return _items; }
        }

        /// <summary>
        /// Gets the favorite items, newest first.
        /// </summary>
        public IReadOnlyList<ClipboardItemViewModel> FavoriteItems
        {
            get { return _items.Where(i => i.IsFavorite).ToList(); }
        }

        /// <summary>
        /// Gets a value indicating whether the history is empty.
        /// </summary>
        public bool IsEmpty
        {
            get { return _items.Count == 0; }
        }

        /// <summary>
        /// Gets a value indicating whether there is at least one non-favorite item.
        /// </summary>
        public bool HasNonFavorites
        {
            get { return _items.Any(i => !i.IsFavorite); }
        }

        /// <summary>
        /// Reloads the history from the local device.
        /// </summary>
        public void Reload()
        {
            ClipboardHistoryModel model = _historyService.LoadHistoryOrDefault();
            _items = model.Items.Select(item => new ClipboardItemViewModel(item, _historyService)).ToList();

            OnPropertyChanged(nameof(AllItems));
            OnPropertyChanged(nameof(FavoriteItems));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasNonFavorites));
        }

        /// <summary>
        /// Gets the command to copy an item back to the system clipboard.
        /// </summary>
        public ICommand CopyCommand { get; }

        private async Task CopyAsync(ClipboardItemViewModel item)
        {
            if (item == null)
                return;

            if (item.IsImage)
            {
                await _clipboardService.SetImageAsync(item.LoadFullImage());
            }
            else
            {
                await _clipboardService.SetTextAsync(item.Model.Text);
            }
        }

        /// <summary>
        /// Gets the command to toggle the favorite state of an item.
        /// </summary>
        public ICommand ToggleFavoriteCommand { get; }

        private void ToggleFavorite(ClipboardItemViewModel item)
        {
            if (item == null)
                return;
            _historyService.ToggleFavorite(item.Id);
            Reload();
        }

        /// <summary>
        /// Gets the command to delete a single item.
        /// </summary>
        public ICommand DeleteCommand { get; }

        private void Delete(ClipboardItemViewModel item)
        {
            if (item == null)
                return;
            _historyService.Delete(item.Id);
            Reload();
        }

        /// <summary>
        /// Gets the command to clear the whole history.
        /// </summary>
        public ICommand ClearAllCommand { get; }

        private void ClearAll()
        {
            _historyService.ClearAll();
            Reload();
        }

        /// <summary>
        /// Gets the command to clear all non-favorite items.
        /// </summary>
        public ICommand ClearNonFavoritesCommand { get; }

        private void ClearNonFavorites()
        {
            _historyService.ClearNonFavorites();
            Reload();
        }
    }

    /// <summary>
    /// View model for a single item of the clipboard history.
    /// </summary>
    public class ClipboardItemViewModel : ViewModelBase
    {
        private readonly IClipboardHistoryService _historyService;
        private string _thumbnailDataUri;
        private bool _thumbnailLoaded;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardItemViewModel"/> class.
        /// </summary>
        public ClipboardItemViewModel(ClipboardItemModel model, IClipboardHistoryService historyService)
        {
            Model = model;
            _historyService = historyService;
        }

        /// <summary>
        /// Gets the wrapped model.
        /// </summary>
        public ClipboardItemModel Model { get; }

        /// <summary>
        /// Gets the id of the item.
        /// </summary>
        public Guid Id
        {
            get { return Model.Id; }
        }

        /// <summary>
        /// Gets a value indicating whether the item is a favorite.
        /// </summary>
        public bool IsFavorite
        {
            get { return Model.IsFavorite; }
        }

        /// <summary>
        /// Gets a value indicating whether the item holds an image.
        /// </summary>
        public bool IsImage
        {
            get { return Model.Kind == ClipboardItemKind.Image; }
        }

        /// <summary>
        /// Gets the time in UTC when the item was captured.
        /// </summary>
        public DateTime CreatedUtc
        {
            get { return Model.CreatedUtc; }
        }

        /// <summary>
        /// Gets a shortened preview of the copied text.
        /// </summary>
        public string PreviewText
        {
            get
            {
                string text = Model.Text ?? string.Empty;
                return (text.Length <= 500) ? text : text.Substring(0, 500) + "\u2026";
            }
        }

        /// <summary>
        /// Gets the thumbnail of the copied image as a data URI, or null.
        /// </summary>
        public string ThumbnailDataUri
        {
            get
            {
                if (!_thumbnailLoaded)
                {
                    _thumbnailLoaded = true;
                    try
                    {
                        string path = _historyService.GetThumbImagePath(Model.ImageFileName);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            _thumbnailDataUri = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
                    }
                    catch (Exception)
                    {
                    }
                }
                return _thumbnailDataUri;
            }
        }

        /// <summary>
        /// Loads the bytes of the full size image.
        /// </summary>
        /// <returns>The encoded image, or null.</returns>
        public byte[] LoadFullImage()
        {
            try
            {
                string path = _historyService.GetFullImagePath(Model.ImageFileName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return File.ReadAllBytes(path);
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
