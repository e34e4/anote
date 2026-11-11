// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Lists PDFs saved under <see cref="AnotePaths.Abro"/>/pages with share / WebDAV actions.
    /// </summary>
    internal sealed class AbroSavedPagesPage : ContentPage
    {
        private readonly string _pagesDir;
        private readonly Func<Task> _saveCurrentPageAsync;
        private readonly VerticalStackLayout _listStack;
        private readonly Label _emptyLabel;

        public AbroSavedPagesPage(string pagesDir, Func<Task> saveCurrentPageAsync)
        {
            BackgroundColor = Colors.Black;
            _pagesDir = pagesDir ?? Path.Combine(AnotePaths.Abro, "pages");
            _saveCurrentPageAsync = saveCurrentPageAsync;

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) =>
            {
                try { await Navigation.PopModalAsync(false).ConfigureAwait(true); }
                catch { }
            };

            var titleLabel = new Label
            {
                Text = "Saved pages",
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

            var saveButton = CreateActionButton("Save current page as PDF");
            saveButton.Margin = new Thickness(16, 12, 16, 8);
            saveButton.Clicked += async (s, e) =>
            {
                if (_saveCurrentPageAsync != null)
                    await _saveCurrentPageAsync().ConfigureAwait(true);
                Rebuild();
            };

            _emptyLabel = new Label
            {
                Text = "No saved PDFs yet. Files go to " + Path.Combine(AnotePaths.Abro, "pages") + ".",
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 13,
                Margin = new Thickness(16, 8, 16, 0),
                IsVisible = false,
            };

            _listStack = new VerticalStackLayout { Spacing = 0 };
            var scroll = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Children = { saveButton, _emptyLabel, _listStack },
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
            _listStack.Children.Clear();
            try
            {
                Directory.CreateDirectory(_pagesDir);
                string[] files = Directory.GetFiles(_pagesDir, "*.pdf")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
                _emptyLabel.IsVisible = files.Length == 0;
                foreach (string path in files)
                    _listStack.Children.Add(CreateRow(path));
            }
            catch
            {
                _emptyLabel.IsVisible = true;
            }
        }

        private View CreateRow(string path)
        {
            string name = Path.GetFileName(path);
            var title = new Label
            {
                Text = name,
                TextColor = Colors.White,
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };

            Button shareButton = CreateSmallButton("Share");
            shareButton.Clicked += (s, e) => SharePdf(path);

            Button webdavButton = CreateSmallButton("WebDAV");
            webdavButton.Clicked += async (s, e) => await CopyToWebdavAsync(path).ConfigureAwait(true);

            Button deleteButton = CreateSmallButton("⌫");
            deleteButton.TextColor = Color.FromArgb("#ff453a");
            deleteButton.Clicked += async (s, e) =>
            {
                bool ok = await DisplayAlertAsync("Delete PDF", $"Delete “{name}”?", "Delete", "Cancel")
                    .ConfigureAwait(true);
                if (!ok)
                    return;
                try { File.Delete(path); } catch { }
                Rebuild();
            };

            var actions = new HorizontalStackLayout
            {
                Spacing = 6,
                Children = { shareButton, webdavButton, deleteButton },
            };

            var row = new Grid
            {
                Padding = new Thickness(16, 10),
                RowSpacing = 8,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
            };
            row.Add(title, 0, 0);
            row.Add(actions, 0, 1);

            return new VerticalStackLayout
            {
                Children =
                {
                    row,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2c2c2e") },
                },
            };
        }

        private async Task CopyToWebdavAsync(string path)
        {
            try
            {
                IWebdavQuickSaveService quick = Ioc.Instance.GetService<IWebdavQuickSaveService>();
                if (quick == null || !quick.IsConfigured)
                {
                    await DisplayAlertAsync("WebDAV", "Configure File/Booky WebDAV first.", "OK").ConfigureAwait(true);
                    return;
                }

                bool ok = await quick.SaveLocalFileAsync("abro_page", path).ConfigureAwait(true);
                await DisplayAlertAsync(
                    "WebDAV",
                    ok ? ("Uploaded as " + (quick.LastFileName ?? Path.GetFileName(path))) : (quick.LastError ?? "Upload failed"),
                    "OK").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("WebDAV", ex.Message, "OK").ConfigureAwait(true);
            }
        }

        private static void SharePdf(string path)
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var shareDir = new Java.IO.File(context.CacheDir, "reader_share");
                shareDir.Mkdirs();
                var shareFile = new Java.IO.File(shareDir, Path.GetFileName(path));
                File.Copy(path, shareFile.AbsolutePath, overwrite: true);

                global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                    context, context.PackageName + ".fileprovider", shareFile);

                var intent = new Intent(Intent.ActionSend);
                intent.SetType("application/pdf");
                intent.PutExtra(Intent.ExtraStream, uri);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                Intent chooser = Intent.CreateChooser(intent, Path.GetFileName(path));
                chooser.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(chooser);
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
                BackgroundColor = Color.FromArgb("#2481cc"),
                CornerRadius = 8,
                HeightRequest = 44,
                FontSize = 15,
            };
        }

        private static Button CreateSmallButton(string text)
        {
            return new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2c2c2e"),
                CornerRadius = 8,
                HeightRequest = 36,
                Padding = new Thickness(10, 0),
                FontSize = 13,
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
