// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Models;
using System.IO;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Abro Settings → Font: system font or a copied .ttf/.otf from storage.
    /// </summary>
    internal sealed class AbroFontSettingsPage : ContentPage
    {
        private readonly IAbroBrowserSettingsService _settingsService;
        private readonly Label _statusLabel;

        public AbroFontSettingsPage()
        {
            BackgroundColor = Colors.Black;
            _settingsService = Ioc.Instance.GetService<IAbroBrowserSettingsService>();

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = "Font",
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
                FontSize = 14,
                Margin = new Thickness(16, 16, 16, 8),
                LineBreakMode = LineBreakMode.WordWrap,
            };

            var systemButton = CreateActionButton("Use system font");
            systemButton.Clicked += (s, e) => UseSystemFont();

            var pickButton = CreateActionButton("Choose .ttf / .otf from storage");
            pickButton.BackgroundColor = Color.FromArgb("#2481cc");
            pickButton.Clicked += async (s, e) => await PickFontAsync().ConfigureAwait(true);

            var note = new Label
            {
                Text = "The font file is copied into " + Path.Combine(AnotePaths.Abro, "fonts") + " and applied to pages while browsing.",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                Margin = new Thickness(16, 12, 16, 0),
            };

            var body = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(0, 0, 0, 24),
                Children = { _statusLabel, systemButton, pickButton, note },
            };
            systemButton.Margin = new Thickness(16, 0);
            pickButton.Margin = new Thickness(16, 0);

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
            AbroBrowserSettingsModel settings = _settingsService.LoadOrDefault();
            if (settings.UseCustomFont && !string.IsNullOrWhiteSpace(settings.CustomFontFileName))
            {
                string path = _settingsService.GetCustomFontPath();
                bool exists = !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
                _statusLabel.Text = exists
                    ? "Custom font: " + settings.CustomFontFileName
                    : "Custom font missing — pick a file again or use system font.";
            }
            else
            {
                _statusLabel.Text = "Using default system font.";
            }
        }

        private void UseSystemFont()
        {
            _settingsService.ClearCustomFont();
            Rebuild();
        }

        private async Task PickFontAsync()
        {
            IFilePickerService picker = null;
            try
            {
                picker = Ioc.Instance.GetService<IFilePickerService>();
            }
            catch
            {
            }

            if (picker == null)
            {
                await DisplayAlertAsync("Font", "File picker unavailable.", "OK").ConfigureAwait(true);
                return;
            }

            bool picked = await picker.PickFile(new[] { ".ttf", ".otf" }, "font/*").ConfigureAwait(true);
            if (!picked)
                return;

            try
            {
                byte[] bytes = await picker.ReadPickedFile().ConfigureAwait(true);
                string name = picker.GetPickedFileName() ?? "custom.ttf";
                if (!_settingsService.TryInstallCustomFont(bytes, name))
                {
                    await DisplayAlertAsync("Font", "Could not install that font file.", "OK").ConfigureAwait(true);
                    return;
                }

                Rebuild();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Font", "Failed: " + Truncate(ex.Message, 80), "OK").ConfigureAwait(true);
            }
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

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max - 1) + "…";
        }
    }
}
