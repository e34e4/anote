// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Abro Downloads: location, threads, and download history.
    /// </summary>
    internal sealed class AbroDownloadsPage : ContentPage
    {
        private readonly IAbroDownloadService _downloads;
        private readonly Label _locationLabel;
        private readonly Label _threadsLabel;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;

        public AbroDownloadsPage()
        {
            BackgroundColor = Colors.Black;
            _downloads = Ioc.Instance.GetService<IAbroDownloadService>();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Downloads",
                TextColor = Colors.White,
                FontSize = 16,
                VerticalOptions = LayoutOptions.Center,
            };

            var header = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                HeightRequest = 56,
                Padding = new Thickness(4, 0, 12, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            header.Add(backButton, 0, 0);
            header.Add(titleLabel, 1, 0);

            _locationLabel = new Label
            {
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                LineBreakMode = LineBreakMode.MiddleTruncation,
                Margin = new Thickness(16, 4, 16, 0),
            };
            _threadsLabel = new Label
            {
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                Margin = new Thickness(16, 2, 16, 8),
            };

            var locationButton = CreateActionButton("Change download location");
            locationButton.Clicked += async (s, e) => await ChangeLocationAsync().ConfigureAwait(true);

            var threadsButton = CreateActionButton("Change download threads");
            threadsButton.Clicked += async (s, e) => await ChangeThreadsAsync().ConfigureAwait(true);

            var historyHeader = new Label
            {
                Text = "Previous downloads",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                Margin = new Thickness(16, 16, 16, 4),
            };

            _emptyLabel = new Label
            {
                Text = "No downloads yet",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 14,
                Margin = new Thickness(16, 8, 16, 0),
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };

            var body = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _locationLabel,
                    _threadsLabel,
                    new VerticalStackLayout
                    {
                        Spacing = 8,
                        Padding = new Thickness(16, 0),
                        Children = { locationButton, threadsButton },
                    },
                    historyHeader,
                    _emptyLabel,
                    _listStack,
                },
            };

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
            };
            root.Add(header, 0, 0);
            root.Add(new ScrollView { Content = body }, 0, 1);
            Content = root;
            Rebuild();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Rebuild();
        }

        private void Rebuild()
        {
            AbroDownloadStoreModel store = _downloads.LoadOrDefault();
            _locationLabel.Text = "Location: " + _downloads.GetDownloadDirectory();
            _threadsLabel.Text = "Threads: " + store.GetClampedThreadCount();

            _listStack.Children.Clear();
            _emptyLabel.IsVisible = store.History.Count == 0;
            foreach (AbroDownloadHistoryItemModel item in store.History)
                _listStack.Children.Add(CreateRow(item));
        }

        private View CreateRow(AbroDownloadHistoryItemModel item)
        {
            Guid id = item.Id;
            var name = new Label
            {
                Text = string.IsNullOrWhiteSpace(item.FileName) ? "download" : item.FileName,
                TextColor = Colors.White,
                FontSize = 15,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
            };
            var url = new Label
            {
                Text = item.Url ?? string.Empty,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 11,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
                Margin = new Thickness(0, 2, 0, 0),
            };
            string meta = item.Status + " · " + FormatWhen(item.StartedAt);
            if (item.Bytes > 0)
                meta += " · " + FormatBytes(item.Bytes);
            if (item.ThreadCount > 1)
                meta += " · " + item.ThreadCount + " threads";
            if (!string.IsNullOrWhiteSpace(item.Error))
                meta = "failed · " + Truncate(item.Error, 48);

            var metaLabel = new Label
            {
                Text = meta,
                TextColor = string.Equals(item.Status, AbroDownloadHistoryItemModel.StatusFailed, StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb("#ff9f0a")
                    : Color.FromArgb("#8e8e93"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var text = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { name, url, metaLabel },
            };

            Button deleteButton = CreateIconButton("⌫", 18);
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) => await RemoveAsync(id, item.FileName).ConfigureAwait(true);

            var row = new Grid
            {
                Padding = new Thickness(16, 12),
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(text, 0, 0);
            row.Add(deleteButton, 1, 0);

            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private async Task ChangeLocationAsync()
        {
            string current = _downloads.GetDownloadDirectory();
            string path = await DisplayPromptAsync(
                "Download location",
                "Absolute folder path (empty = default /sdcard/abro/down)",
                "Save",
                "Cancel",
                current,
                -1,
                Keyboard.Text,
                current).ConfigureAwait(true);
            if (path == null)
                return;

            if (!_downloads.SetDownloadDirectory(path.Trim()))
            {
                await DisplayAlertAsync("Downloads", "Could not use that folder.", "OK").ConfigureAwait(true);
                return;
            }

            Rebuild();
        }

        private async Task ChangeThreadsAsync()
        {
            string[] choices = { "1", "2", "3", "4", "5" };
            string choice = await DisplayActionSheetAsync(
                "Download threads",
                "Cancel",
                null,
                choices).ConfigureAwait(true);
            if (string.IsNullOrEmpty(choice) || choice == "Cancel")
                return;
            if (!int.TryParse(choice, out int n))
                return;

            _downloads.SetThreadCount(n);
            Rebuild();
        }

        private async Task RemoveAsync(Guid id, string fileName)
        {
            bool confirm = await DisplayAlertAsync(
                "Remove from history",
                $"Remove “{Truncate(fileName, 40)}” from history?\n(The file on disk is kept.)",
                "Remove",
                "Cancel").ConfigureAwait(true);
            if (!confirm)
                return;

            _downloads.RemoveHistoryItem(id);
            Rebuild();
        }

        private async Task CloseAsync()
        {
            try { await Navigation.PopModalAsync(false).ConfigureAwait(true); }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
        }

        private static string FormatWhen(DateTime utc)
        {
            DateTime local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            DateTime now = DateTime.Now;
            if (local.Date == now.Date)
                return local.ToString("HH:mm");
            if (local.Year == now.Year)
                return local.ToString("dd MMM");
            return local.ToString("dd MMM yyyy");
        }

        private static Button CreateActionButton(string text)
        {
            return new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#3a3a3c"),
                CornerRadius = 8,
                HeightRequest = 44,
                FontSize = 15,
            };
        }

        private static Button CreateIconButton(string text, double fontSize)
        {
            return new Button
            {
                Text = text,
                FontSize = fontSize,
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                WidthRequest = 44,
                HeightRequest = 44,
                Padding = 0,
                VerticalOptions = LayoutOptions.Center,
            };
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (oneLine.Length <= max)
                return oneLine;
            return oneLine.Substring(0, max - 1) + "…";
        }
    }
}
