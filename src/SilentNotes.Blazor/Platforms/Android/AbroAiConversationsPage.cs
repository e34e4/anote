// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Lists saved Abro AI conversations with open and delete actions.
    /// </summary>
    internal sealed class AbroAiConversationsPage : ContentPage
    {
        private readonly IAbroAiStoreService _storeService;
        private readonly IAbroAiSyncService _syncService;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;
        private AbroAiStoreModel _store;

        public AbroAiConversationsPage()
        {
            BackgroundColor = Colors.Black;
            _storeService = Ioc.Instance.GetService<IAbroAiStoreService>();
            _syncService = Ioc.Instance.GetService<IAbroAiSyncService>();
            _store = _storeService.LoadOrDefault();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Previous chats",
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

            _emptyLabel = new Label
            {
                Text = "No previous chats",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 15,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(24, 48, 24, 0),
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout
            {
                Spacing = 0,
                Padding = new Thickness(0, 0, 0, 24),
            };

            var scroll = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children = { _emptyLabel, _listStack },
                },
                VerticalOptions = LayoutOptions.Fill,
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
            root.Add(scroll, 0, 1);
            Content = root;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            RebuildList();
        }

        private void RebuildList()
        {
            _store = _storeService.LoadOrDefault();
            _listStack.Children.Clear();

            List<AbroAiConversationModel> conversations = (_store.Conversations ?? new List<AbroAiConversationModel>())
                .OrderByDescending(c => c.ModifiedAt)
                .ToList();

            _emptyLabel.IsVisible = conversations.Count == 0;
            foreach (AbroAiConversationModel conversation in conversations)
                _listStack.Children.Add(CreateRow(conversation));
        }

        private View CreateRow(AbroAiConversationModel conversation)
        {
            Guid id = conversation.Id;
            string title = string.IsNullOrWhiteSpace(conversation.Title) ? "Chat" : conversation.Title.Trim();
            int messageCount = conversation.Messages?.Count ?? 0;
            string subtitle = $"{FormatWhen(conversation.ModifiedAt)} · {messageCount} message{(messageCount == 1 ? string.Empty : "s")}";

            var titleLabel = new Label
            {
                Text = title,
                TextColor = Colors.White,
                FontSize = 15,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
            };
            var subtitleLabel = new Label
            {
                Text = subtitle,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var textStack = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { titleLabel, subtitleLabel },
            };

            Button deleteButton = CreateIconButton("⌫", 18);
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) => await DeleteAsync(id, title).ConfigureAwait(true);

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
            row.Add(textStack, 0, 0);
            row.Add(deleteButton, 1, 0);

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => await OpenChatAsync(id).ConfigureAwait(true);
            textStack.GestureRecognizers.Add(tap);

            var border = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Content = row,
            };

            // Hairline under each row.
            return new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    border,
                    new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#2c2c2e"),
                    },
                },
            };
        }

        private async Task OpenChatAsync(Guid conversationId)
        {
            try
            {
                var page = new AbroAiChatPage(conversationId, null);
                await Navigation.PushModalAsync(page, false).ConfigureAwait(true);
            }
            catch
            {
                await DisplayAlertAsync("AI", "Could not open chat.", "OK").ConfigureAwait(true);
            }
        }

        private async Task DeleteAsync(Guid conversationId, string title)
        {
            bool confirm = await DisplayAlertAsync(
                "Delete chat",
                $"Delete “{Truncate(title, 48)}”?",
                "Delete",
                "Cancel").ConfigureAwait(true);
            if (!confirm)
                return;

            _store = _storeService.LoadOrDefault();
            int removed = _store.Conversations.RemoveAll(c => c.Id == conversationId);
            if (removed <= 0)
            {
                RebuildList();
                return;
            }

            _storeService.TrySave(_store);
            if (_syncService?.IsConfigured == true)
                _ = _syncService.SyncAsync();

            RebuildList();
        }

        private async Task CloseAsync()
        {
            try
            {
                await Navigation.PopModalAsync(false).ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private static string FormatWhen(DateTime utc)
        {
            DateTime local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            DateTime now = DateTime.Now;
            if (local.Date == now.Date)
                return local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1))
                return "Yesterday";
            if (local.Year == now.Year)
                return local.ToString("dd MMM");
            return local.ToString("dd MMM yyyy");
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
