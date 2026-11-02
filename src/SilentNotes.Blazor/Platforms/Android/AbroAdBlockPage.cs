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
    /// Abro Settings → Ad blocking: enable, subscriptions, update.
    /// </summary>
    internal sealed class AbroAdBlockPage : ContentPage
    {
        private readonly IAbroAdBlockService _adBlock;
        private readonly Label _statusLabel;
        private readonly Button _enableButton;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;
        private bool _busy;

        public AbroAdBlockPage()
        {
            BackgroundColor = Colors.Black;
            _adBlock = Ioc.Instance.GetService<IAbroAdBlockService>();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Ad blocking",
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

            _statusLabel = new Label
            {
                Text = string.Empty,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 13,
                Margin = new Thickness(16, 12, 16, 4),
            };

            _enableButton = CreateActionButton("Enable");
            _enableButton.Clicked += (s, e) => ToggleEnabled();

            var addButton = CreateActionButton("Add subscription");
            addButton.Clicked += async (s, e) => await AddSubscriptionAsync().ConfigureAwait(true);

            var updateButton = CreateActionButton("Update subscriptions");
            updateButton.BackgroundColor = Color.FromArgb("#2481cc");
            updateButton.Clicked += async (s, e) => await UpdateAsync().ConfigureAwait(true);

            var actions = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(16, 4, 16, 8),
                Children = { _enableButton, addButton, updateButton },
            };

            _emptyLabel = new Label
            {
                Text = "No subscriptions yet.\nAdd a hosts / DNS blocklist URL, then Update.",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 14,
                Margin = new Thickness(16, 16, 16, 0),
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };

            var scroll = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children = { _statusLabel, actions, _emptyLabel, _listStack },
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

            Rebuild();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Rebuild();
        }

        private void Rebuild()
        {
            AbroAdBlockStoreModel store = _adBlock.LoadOrDefault();
            _adBlock.EnsureFilterLoaded();

            _enableButton.Text = store.Enabled ? "Disable ad blocking" : "Enable ad blocking";
            _enableButton.BackgroundColor = store.Enabled
                ? Color.FromArgb("#3a3a3c")
                : Color.FromArgb("#2481cc");

            string compiled = store.CompiledDomainCount > 0
                ? $"{store.CompiledDomainCount} domains"
                : $"{_adBlock.CompiledDomainCount} domains";
            string when = store.CompiledAt.HasValue
                ? " · updated " + FormatWhen(store.CompiledAt.Value)
                : string.Empty;
            _statusLabel.Text = (store.Enabled ? "On" : "Off") + " · " + compiled + when;

            _listStack.Children.Clear();
            _emptyLabel.IsVisible = store.Subscriptions.Count == 0;
            foreach (AbroAdBlockSubscriptionModel sub in store.Subscriptions)
                _listStack.Children.Add(CreateRow(sub));
        }

        private View CreateRow(AbroAdBlockSubscriptionModel sub)
        {
            Guid id = sub.Id;
            string title = string.IsNullOrWhiteSpace(sub.Title) ? sub.Url : sub.Title;
            string meta = sub.UpdatedAt.HasValue
                ? $"{sub.DomainCount} domains · {FormatWhen(sub.UpdatedAt.Value)}"
                : "Not downloaded yet";
            if (!string.IsNullOrWhiteSpace(sub.LastError))
                meta = "Error: " + Truncate(sub.LastError, 60);

            var titleLabel = new Label
            {
                Text = title,
                TextColor = Colors.White,
                FontSize = 15,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
            };
            var urlLabel = new Label
            {
                Text = sub.Url,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 11,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                Margin = new Thickness(0, 2, 0, 0),
            };
            var metaLabel = new Label
            {
                Text = meta,
                TextColor = string.IsNullOrWhiteSpace(sub.LastError)
                    ? Color.FromArgb("#8e8e93")
                    : Color.FromArgb("#ff9f0a"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var text = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { titleLabel, urlLabel, metaLabel },
            };

            Button deleteButton = CreateIconButton("⌫", 18);
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) => await RemoveAsync(id, title).ConfigureAwait(true);

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
                Spacing = 0,
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private void ToggleEnabled()
        {
            if (_busy)
                return;
            bool next = !_adBlock.IsEnabled;
            if (!_adBlock.SetEnabled(next))
            {
                _statusLabel.Text = "Could not save setting";
                return;
            }

            if (next && _adBlock.CompiledDomainCount == 0)
                _statusLabel.Text = "Enabled — add a list and tap Update";
            Rebuild();
        }

        private async Task AddSubscriptionAsync()
        {
            if (_busy)
                return;

            string url = await DisplayPromptAsync(
                "Add subscription",
                "Hosts or DNS blocklist URL",
                "Add",
                "Cancel",
                "https://…",
                -1,
                Keyboard.Url).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(url))
                return;

            AbroAdBlockSubscriptionModel sub = _adBlock.AddSubscription(url.Trim());
            if (sub == null)
            {
                await DisplayAlertAsync("Ad blocking", "Enter a valid http(s) URL.", "OK").ConfigureAwait(true);
                return;
            }

            Rebuild();
            bool updateNow = await DisplayAlertAsync(
                "Ad blocking",
                "Download this list now?",
                "Update",
                "Later").ConfigureAwait(true);
            if (updateNow)
                await UpdateAsync().ConfigureAwait(true);
        }

        private async Task RemoveAsync(Guid id, string title)
        {
            if (_busy)
                return;

            bool confirm = await DisplayAlertAsync(
                "Remove subscription",
                $"Remove “{Truncate(title, 48)}”?",
                "Remove",
                "Cancel").ConfigureAwait(true);
            if (!confirm)
                return;

            _adBlock.RemoveSubscription(id);
            Rebuild();
        }

        private async Task UpdateAsync()
        {
            if (_busy)
                return;

            _busy = true;
            _statusLabel.Text = "Updating…";
            try
            {
                AbroAdBlockUpdateResult result = await _adBlock.UpdateSubscriptionsAsync().ConfigureAwait(true);
                _statusLabel.Text = result.Message ?? "Updated";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Update failed: " + Truncate(ex.Message, 80);
            }
            finally
            {
                _busy = false;
                Rebuild();
            }
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
