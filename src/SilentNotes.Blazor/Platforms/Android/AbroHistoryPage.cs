// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Abro browsing history with open, share, remove, and clear-all.
    /// </summary>
    internal sealed class AbroHistoryPage : ContentPage
    {
        private readonly string _historyFile;
        private readonly Action<string> _openUrl;
        private readonly object _historyLock;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;

        public AbroHistoryPage(string historyFile, object historyLock, Action<string> openUrl)
        {
            BackgroundColor = Colors.Black;
            _historyFile = historyFile;
            _historyLock = historyLock ?? new object();
            _openUrl = openUrl;

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "History",
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

            var clearButton = new Button
            {
                Text = "Clear all history",
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#ff453a"),
                CornerRadius = 8,
                HeightRequest = 44,
                FontSize = 15,
                Margin = new Thickness(16, 12, 16, 8),
            };
            clearButton.Clicked += async (s, e) => await ClearAllAsync().ConfigureAwait(true);

            _emptyLabel = new Label
            {
                Text = "History is empty",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 14,
                Margin = new Thickness(16, 24, 16, 0),
                HorizontalOptions = LayoutOptions.Center,
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };

            var body = new VerticalStackLayout
            {
                Children = { clearButton, _emptyLabel, _listStack },
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
            List<(string Url, string Title)> entries = ReadEntries();
            _listStack.Children.Clear();
            _emptyLabel.IsVisible = entries.Count == 0;
            foreach ((string url, string title) in entries)
                _listStack.Children.Add(CreateRow(url, title));
        }

        private View CreateRow(string url, string title)
        {
            string capturedUrl = url;
            var titleLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(title) ? url : title,
                TextColor = Colors.White,
                FontSize = 15,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
            };
            var urlLabel = new Label
            {
                Text = url,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 11,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var text = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { titleLabel, urlLabel },
            };
            var openTap = new TapGestureRecognizer();
            openTap.Tapped += async (s, e) =>
            {
                _openUrl?.Invoke(capturedUrl);
                await CloseAsync().ConfigureAwait(true);
            };
            text.GestureRecognizers.Add(openTap);

            Button shareButton = CreateIconButton("⤴", 16);
            shareButton.Clicked += (s, e) => ShareUrl(capturedUrl);

            Button deleteButton = CreateIconButton("⌫", 18);
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) => await RemoveAsync(capturedUrl).ConfigureAwait(true);

            var actions = new HorizontalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { shareButton, deleteButton },
            };

            var row = new Grid
            {
                Padding = new Thickness(16, 12),
                ColumnSpacing = 4,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(text, 0, 0);
            row.Add(actions, 1, 0);

            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private List<(string Url, string Title)> ReadEntries()
        {
            var entries = new List<(string Url, string Title)>();
            try
            {
                if (!File.Exists(_historyFile))
                    return entries;

                string[] lines;
                lock (_historyLock)
                    lines = File.ReadAllLines(_historyFile, Encoding.UTF8);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in lines.Reverse())
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]) || !seen.Add(parts[1]))
                        continue;
                    string title = parts.Length > 2 ? parts[2] : string.Empty;
                    entries.Add((parts[1], title));
                    if (entries.Count >= 200)
                        break;
                }
            }
            catch
            {
            }

            return entries;
        }

        private async Task RemoveAsync(string url)
        {
            bool confirm = await DisplayAlertAsync(
                "Remove",
                "Remove this page from history?",
                "Remove",
                "Cancel").ConfigureAwait(true);
            if (!confirm)
                return;

            try
            {
                lock (_historyLock)
                {
                    if (!File.Exists(_historyFile))
                        return;
                    string[] lines = File.ReadAllLines(_historyFile, Encoding.UTF8);
                    IEnumerable<string> kept = lines.Where(line =>
                    {
                        string[] parts = line.Split('\t');
                        return parts.Length < 2 || !string.Equals(parts[1], url, StringComparison.Ordinal);
                    });
                    File.WriteAllLines(_historyFile, kept, Encoding.UTF8);
                }
            }
            catch
            {
            }

            Rebuild();
        }

        private async Task ClearAllAsync()
        {
            bool confirm = await DisplayAlertAsync(
                "Clear history",
                "Delete all browsing history?",
                "Clear",
                "Cancel").ConfigureAwait(true);
            if (!confirm)
                return;

            try
            {
                lock (_historyLock)
                {
                    if (File.Exists(_historyFile))
                        File.WriteAllText(_historyFile, string.Empty, Encoding.UTF8);
                }
            }
            catch
            {
            }

            Rebuild();
        }

        private static void ShareUrl(string url)
        {
            try
            {
                var intent = new Intent(Intent.ActionSend);
                intent.SetType("text/plain");
                intent.PutExtra(Intent.ExtraText, url);
                Intent chooser = Intent.CreateChooser(intent, "Share link");
                chooser.AddFlags(ActivityFlags.NewTask);
                Platform.AppContext.StartActivity(chooser);
            }
            catch
            {
            }
        }

        private async Task CloseAsync()
        {
            try { await Navigation.PopModalAsync(false).ConfigureAwait(true); }
            catch { }
        }

        private static Button CreateIconButton(string text, double fontSize)
        {
            return new Button
            {
                Text = text,
                FontSize = fontSize,
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                WidthRequest = 40,
                HeightRequest = 40,
                Padding = 0,
                VerticalOptions = LayoutOptions.Center,
            };
        }
    }
}
