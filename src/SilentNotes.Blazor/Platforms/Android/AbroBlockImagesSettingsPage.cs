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
    /// Abro ⋮ → Block images: enable / disable / selectively block network images.
    /// </summary>
    internal sealed class AbroBlockImagesSettingsPage : ContentPage
    {
        private readonly IAbroBrowserSettingsService _settingsService;
        private readonly CheckBox _enableCheck;
        private readonly CheckBox _disableCheck;
        private readonly CheckBox _selectiveCheck;
        private readonly VerticalStackLayout _domainsSection;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;
        private bool _updatingChecks;

        public AbroBlockImagesSettingsPage()
        {
            BackgroundColor = Colors.Black;
            _settingsService = Ioc.Instance.GetService<IAbroBrowserSettingsService>();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Block images",
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

            var hint = new Label
            {
                Text = "Do not load images; show placeholders with the image URL.",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 13,
                Margin = new Thickness(16, 12, 16, 8),
            };

            _enableCheck = CreateModeCheck("Enable");
            _disableCheck = CreateModeCheck("Disable");
            _selectiveCheck = CreateModeCheck("Selectively");
            _enableCheck.CheckedChanged += (s, e) => OnModeChecked(AbroBlockImagesMode.Enable, e.Value);
            _disableCheck.CheckedChanged += (s, e) => OnModeChecked(AbroBlockImagesMode.Disable, e.Value);
            _selectiveCheck.CheckedChanged += (s, e) => OnModeChecked(AbroBlockImagesMode.Selectively, e.Value);

            var modes = new VerticalStackLayout
            {
                Spacing = 0,
                Padding = new Thickness(8, 0, 8, 8),
                Children =
                {
                    CreateCheckRow(_enableCheck, "Enable"),
                    CreateCheckRow(_disableCheck, "Disable"),
                    CreateCheckRow(_selectiveCheck, "Selectively"),
                },
            };

            var addButton = CreateActionButton("Add domain");
            addButton.Clicked += async (s, e) => await EditDomainAsync(-1).ConfigureAwait(true);

            _emptyLabel = new Label
            {
                Text = "No domains yet. Add a domain to block images only there.",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 13,
                Margin = new Thickness(16, 8, 16, 0),
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };
            _domainsSection = new VerticalStackLayout
            {
                Spacing = 8,
                IsVisible = false,
                Children =
                {
                    new Label
                    {
                        Text = "Domains",
                        TextColor = Colors.White,
                        FontSize = 14,
                        Margin = new Thickness(16, 8, 16, 0),
                    },
                    addButton,
                    _emptyLabel,
                    _listStack,
                },
            };
            addButton.Margin = new Thickness(16, 0);

            var scroll = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children = { hint, modes, _domainsSection },
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
            _updatingChecks = true;
            try
            {
                _enableCheck.IsChecked = settings.BlockImagesMode == AbroBlockImagesMode.Enable;
                _disableCheck.IsChecked = settings.BlockImagesMode == AbroBlockImagesMode.Disable;
                _selectiveCheck.IsChecked = settings.BlockImagesMode == AbroBlockImagesMode.Selectively;
            }
            finally
            {
                _updatingChecks = false;
            }

            bool selective = settings.BlockImagesMode == AbroBlockImagesMode.Selectively;
            _domainsSection.IsVisible = selective;
            if (!selective)
                return;

            _listStack.Children.Clear();
            _emptyLabel.IsVisible = settings.BlockImagesDomains == null || settings.BlockImagesDomains.Count == 0;
            if (settings.BlockImagesDomains == null)
                return;

            for (int i = 0; i < settings.BlockImagesDomains.Count; i++)
                _listStack.Children.Add(CreateDomainRow(i, settings.BlockImagesDomains[i]));
        }

        private View CreateDomainRow(int index, string domain)
        {
            int captured = index;
            var title = new Label
            {
                Text = domain,
                TextColor = Colors.White,
                FontSize = 15,
                VerticalOptions = LayoutOptions.Center,
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => await EditDomainAsync(captured).ConfigureAwait(true);
            title.GestureRecognizers.Add(tap);

            Button editButton = CreateIconButton("✎", 16);
            editButton.Clicked += async (s, e) => await EditDomainAsync(captured).ConfigureAwait(true);

            Button deleteButton = CreateIconButton("⌫", 18);
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) => await RemoveDomainAsync(captured, domain).ConfigureAwait(true);

            var row = new Grid
            {
                Padding = new Thickness(16, 10),
                ColumnSpacing = 4,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(title, 0, 0);
            row.Add(editButton, 1, 0);
            row.Add(deleteButton, 2, 0);

            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private void OnModeChecked(AbroBlockImagesMode mode, bool isChecked)
        {
            if (_updatingChecks)
                return;

            if (!isChecked)
            {
                AbroBrowserSettingsModel current = _settingsService.LoadOrDefault();
                if (current.BlockImagesMode != mode)
                    return;

                // Keep exclusive selection — one mode must stay selected.
                _updatingChecks = true;
                try
                {
                    switch (mode)
                    {
                        case AbroBlockImagesMode.Enable:
                            _enableCheck.IsChecked = true;
                            break;
                        case AbroBlockImagesMode.Selectively:
                            _selectiveCheck.IsChecked = true;
                            break;
                        default:
                            _disableCheck.IsChecked = true;
                            break;
                    }
                }
                finally
                {
                    _updatingChecks = false;
                }

                return;
            }

            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            settings.BlockImagesMode = mode;
            _settingsService.TrySave(settings);
            Rebuild();
        }

        private async Task EditDomainAsync(int index)
        {
            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            settings.BlockImagesDomains ??= new System.Collections.Generic.List<string>();
            bool isNew = index < 0 || index >= settings.BlockImagesDomains.Count;
            string current = isNew ? string.Empty : settings.BlockImagesDomains[index];

            string input = await DisplayPromptAsync(
                isNew ? "Add domain" : "Edit domain",
                "Hostname (e.g. example.com)",
                "Save",
                "Cancel",
                "example.com",
                -1,
                Keyboard.Url,
                current).ConfigureAwait(true);
            if (input == null)
                return;

            string domain = AbroBrowserSettingsModel.NormalizeDomain(input);
            if (string.IsNullOrEmpty(domain))
            {
                await DisplayAlertAsync("Block images", "Enter a valid domain.", "OK").ConfigureAwait(true);
                return;
            }

            int duplicate = settings.BlockImagesDomains.FindIndex(d =>
                string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
            if (duplicate >= 0 && (isNew || duplicate != index))
            {
                await DisplayAlertAsync("Block images", "That domain is already in the list.", "OK").ConfigureAwait(true);
                return;
            }

            if (isNew)
                settings.BlockImagesDomains.Add(domain);
            else
                settings.BlockImagesDomains[index] = domain;

            settings.BlockImagesMode = AbroBlockImagesMode.Selectively;
            _settingsService.TrySave(settings);
            Rebuild();
        }

        private async Task RemoveDomainAsync(int index, string domain)
        {
            bool confirm = await DisplayAlertAsync("Remove domain", $"Remove “{domain}”?", "Remove", "Cancel")
                .ConfigureAwait(true);
            if (!confirm)
                return;

            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            if (settings.BlockImagesDomains == null || index < 0 || index >= settings.BlockImagesDomains.Count)
                return;

            settings.BlockImagesDomains.RemoveAt(index);
            _settingsService.TrySave(settings);
            Rebuild();
        }

        private async Task CloseAsync()
        {
            try { await Navigation.PopModalAsync(false).ConfigureAwait(true); }
            catch { }
        }

        private static View CreateCheckRow(CheckBox checkBox, string label)
        {
            var text = new Label
            {
                Text = label,
                TextColor = Colors.White,
                FontSize = 16,
                VerticalOptions = LayoutOptions.Center,
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => { checkBox.IsChecked = true; };
            text.GestureRecognizers.Add(tap);

            var row = new Grid
            {
                Padding = new Thickness(8, 10),
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(checkBox, 0, 0);
            row.Add(text, 1, 0);
            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private static CheckBox CreateModeCheck(string automationId)
        {
            return new CheckBox
            {
                Color = Color.FromArgb("#2481cc"),
                VerticalOptions = LayoutOptions.Center,
                AutomationId = automationId,
            };
        }

        private static Button CreateActionButton(string text)
        {
            return new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2481cc"),
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
