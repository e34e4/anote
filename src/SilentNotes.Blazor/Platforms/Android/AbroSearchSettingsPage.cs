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
    /// Abro Settings → Search: engines, default, suggestions toggle.
    /// </summary>
    internal sealed class AbroSearchSettingsPage : ContentPage
    {
        private readonly IAbroBrowserSettingsService _settingsService;
        private readonly Label _statusLabel;
        private readonly Button _suggestionsButton;
        private readonly VerticalStackLayout _listStack;

        public AbroSearchSettingsPage()
        {
            BackgroundColor = Colors.Black;
            _settingsService = Ioc.Instance.GetService<IAbroBrowserSettingsService>();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Search",
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
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 13,
                Margin = new Thickness(16, 12, 16, 4),
            };

            _suggestionsButton = CreateActionButton("Suggestions");
            _suggestionsButton.Clicked += (s, e) => ToggleSuggestions();

            var addButton = CreateActionButton("Add search engine");
            addButton.Clicked += async (s, e) => await EditEngineAsync(null).ConfigureAwait(true);

            var actions = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(16, 4, 16, 8),
                Children = { _suggestionsButton, addButton },
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };

            var scroll = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children = { _statusLabel, actions, _listStack },
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
            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            AbroSearchEngineModel def = settings.GetDefaultEngine();
            _statusLabel.Text = "Default: " + (def?.Name ?? "—");
            _suggestionsButton.Text = settings.SearchSuggestionsEnabled
                ? "Disable search suggestions"
                : "Enable search suggestions";
            _suggestionsButton.BackgroundColor = settings.SearchSuggestionsEnabled
                ? Color.FromArgb("#3a3a3c")
                : Color.FromArgb("#2481cc");

            _listStack.Children.Clear();
            foreach (AbroSearchEngineModel engine in settings.Engines)
                _listStack.Children.Add(CreateRow(settings, engine));
        }

        private View CreateRow(AbroBrowserSettingsModel settings, AbroSearchEngineModel engine)
        {
            Guid id = engine.Id;
            bool isDefault = settings.DefaultEngineId == engine.Id
                || (settings.GetDefaultEngine()?.Id == engine.Id && settings.DefaultEngineId == null);

            var title = new Label
            {
                Text = (isDefault ? "★ " : string.Empty) + (engine.Name ?? "Engine"),
                TextColor = Colors.White,
                FontSize = 15,
            };
            var url = new Label
            {
                Text = engine.SearchUrl ?? string.Empty,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 11,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                Margin = new Thickness(0, 2, 0, 0),
            };

            var text = new VerticalStackLayout { Children = { title, url }, VerticalOptions = LayoutOptions.Center };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => await ShowEngineActionsAsync(id).ConfigureAwait(true);
            text.GestureRecognizers.Add(tap);

            var row = new Grid
            {
                Padding = new Thickness(16, 12),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(text, 0, 0);

            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private async Task ShowEngineActionsAsync(Guid engineId)
        {
            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            AbroSearchEngineModel engine = settings.Engines.Find(e => e.Id == engineId);
            if (engine == null)
                return;

            const string setDefault = "Set as default";
            const string edit = "Edit";
            const string remove = "Remove";
            string choice = await DisplayActionSheetAsync(engine.Name ?? "Engine", "Cancel", null, setDefault, edit, remove)
                .ConfigureAwait(true);

            switch (choice)
            {
                case setDefault:
                    settings.DefaultEngineId = engine.Id;
                    _settingsService.TrySave(settings);
                    Rebuild();
                    break;
                case edit:
                    await EditEngineAsync(engine).ConfigureAwait(true);
                    break;
                case remove:
                    if (settings.Engines.Count <= 1)
                    {
                        await DisplayAlertAsync("Search", "Keep at least one search engine.", "OK").ConfigureAwait(true);
                        return;
                    }

                    bool confirm = await DisplayAlertAsync("Remove engine", $"Remove “{engine.Name}”?", "Remove", "Cancel")
                        .ConfigureAwait(true);
                    if (!confirm)
                        return;
                    settings.Engines.RemoveAll(e => e.Id == engineId);
                    if (settings.DefaultEngineId == engineId)
                        settings.DefaultEngineId = settings.Engines[0].Id;
                    _settingsService.TrySave(settings);
                    Rebuild();
                    break;
            }
        }

        private async Task EditEngineAsync(AbroSearchEngineModel existing)
        {
            string name = await DisplayPromptAsync(
                existing == null ? "Add engine" : "Edit name",
                "Name",
                "Next",
                "Cancel",
                "DuckDuckGo",
                -1,
                Keyboard.Text,
                existing?.Name ?? string.Empty).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name))
                return;

            string searchUrl = await DisplayPromptAsync(
                existing == null ? "Add engine" : "Edit search URL",
                "Search URL with {query}",
                "Next",
                "Cancel",
                "https://example.com/search?q={query}",
                -1,
                Keyboard.Url,
                existing?.SearchUrl ?? string.Empty).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(searchUrl) || !searchUrl.Contains("{query}", StringComparison.OrdinalIgnoreCase))
            {
                await DisplayAlertAsync("Search", "Search URL must include {query}.", "OK").ConfigureAwait(true);
                return;
            }

            string suggestUrl = await DisplayPromptAsync(
                existing == null ? "Add engine" : "Edit suggest URL",
                "Suggestions URL with {query} (optional)",
                "Save",
                "Cancel",
                "https://… or leave empty",
                -1,
                Keyboard.Url,
                existing?.SuggestUrl ?? string.Empty).ConfigureAwait(true);
            if (suggestUrl == null)
                return;

            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            AbroSearchEngineModel engine = existing ?? new AbroSearchEngineModel();
            engine.Name = name.Trim();
            engine.SearchUrl = searchUrl.Trim();
            engine.SuggestUrl = suggestUrl.Trim();
            if (existing == null)
            {
                settings.Engines.Add(engine);
                if (!settings.DefaultEngineId.HasValue)
                    settings.DefaultEngineId = engine.Id;
            }

            _settingsService.TrySave(settings);
            Rebuild();
        }

        private void ToggleSuggestions()
        {
            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            settings.SearchSuggestionsEnabled = !settings.SearchSuggestionsEnabled;
            _settingsService.TrySave(settings);
            Rebuild();
        }

        private async Task CloseAsync()
        {
            try { await Navigation.PopModalAsync(false).ConfigureAwait(true); }
            catch { }
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
    }
}
