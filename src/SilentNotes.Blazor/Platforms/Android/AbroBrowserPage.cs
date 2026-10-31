// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content;
using Android.Webkit;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using AUri = Android.Net.Uri;
using AWebView = Android.Webkit.WebView;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Full-screen light multi-tab web browser ("Abro"). History, a cookie dump, the cache and
    /// downloaded files are stored in a visible folder (<c>/sdcard/abro</c>), the open tabs are
    /// persisted in <c>tabs.json</c> and restored the next time the browser is opened.
    /// </summary>
    internal sealed class AbroBrowserPage : ContentPage
    {
        private const string HomeUrl = "https://duckduckgo.com";
        private const string SearchUrl = "https://duckduckgo.com/?q=";
        private const int MaxHistoryMenuEntries = 30;
        private const int MaxHistoryLinesRead = 500;

        private static readonly object HistoryLock = new object();

        private readonly List<BrowserTab> _tabs = new List<BrowserTab>();
        private readonly string _rootDir;
        private readonly string _historyDir;
        private readonly string _bookmarksDir;
        private readonly string _cookiesDir;
        private readonly string _cacheDir;
        private readonly string _filesDir;
        private readonly string _tabsFile;
        private readonly Grid _webHost;
        private readonly HorizontalStackLayout _tabStrip;
        private readonly Entry _addressEntry;
        private readonly Button _backButton;
        private readonly Button _forwardButton;
        private BrowserTab _current;
        private bool _restored;
        private bool _closing;
        private readonly string _initialUrl;
        private readonly AbroStartAction _startAction;

        public enum AbroStartAction
        {
            None,
            History,
            Bookmarks,
        }

        public AbroBrowserPage(string initialUrl = null, AbroStartAction startAction = AbroStartAction.None)
        {
            BackgroundColor = Colors.Black;
            _initialUrl = initialUrl;
            _startAction = startAction;

            _rootDir = ResolveRootDirectory();
            _historyDir = Path.Combine(_rootDir, "history");
            _bookmarksDir = Path.Combine(_rootDir, "bookmarks");
            _cookiesDir = Path.Combine(_rootDir, "cookies");
            _cacheDir = Path.Combine(_rootDir, "cache");
            _filesDir = Path.Combine(_rootDir, "files");
            _tabsFile = Path.Combine(_rootDir, "tabs.json");
            EnsureDirectories();

            _tabStrip = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
            var tabScroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = _tabStrip,
            };

            Button addButton = CreateIconButton("+", 24);
            addButton.Clicked += (s, e) => AddTab(HomeUrl, select: true);
            Button menuButton = CreateIconButton("⋮", 22);
            menuButton.Clicked += async (s, e) => await ShowMenuAsync().ConfigureAwait(true);
            Button closeButton = CreateIconButton("✕", 20);
            closeButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var header = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                HeightRequest = 52,
                Padding = new Thickness(8, 0, 4, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            header.Add(tabScroll, 0, 0);
            header.Add(addButton, 1, 0);
            header.Add(menuButton, 2, 0);
            header.Add(closeButton, 3, 0);

            _backButton = CreateIconButton("◀", 18);
            _backButton.Clicked += (s, e) => GoBack();
            _forwardButton = CreateIconButton("▶", 18);
            _forwardButton.Clicked += (s, e) => GoForward();
            Button reloadButton = CreateIconButton("⟳", 20);
            reloadButton.Clicked += (s, e) => Reload();
            Button goButton = new Button
            {
                Text = "Go",
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2481cc"),
                HeightRequest = 40,
                Padding = new Thickness(12, 0),
                CornerRadius = 8,
                VerticalOptions = LayoutOptions.Center,
            };
            goButton.Clicked += (s, e) => NavigateFromAddressBar();

            _addressEntry = new Entry
            {
                Placeholder = "Search or enter address",
                PlaceholderColor = Color.FromArgb("#8e8e93"),
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2c2c2e"),
                Keyboard = Keyboard.Url,
                ReturnType = ReturnType.Go,
                ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
                VerticalOptions = LayoutOptions.Center,
                FontSize = 14,
            };
            _addressEntry.Completed += (s, e) => NavigateFromAddressBar();

            var bar = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                Padding = new Thickness(4, 2),
                ColumnSpacing = 2,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            bar.Add(_backButton, 0, 0);
            bar.Add(_forwardButton, 1, 0);
            bar.Add(reloadButton, 2, 0);
            bar.Add(_addressEntry, 3, 0);
            bar.Add(goButton, 4, 0);

            _webHost = new Grid
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                BackgroundColor = Colors.Black,
            };

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
            };
            root.Add(header, 0, 0);
            root.Add(bar, 0, 1);
            root.Add(_webHost, 0, 2);
            Content = root;
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_restored)
                return;

            _restored = true;
            RestoreTabs();
        }

        /// <inheritdoc/>
        protected override void OnDisappearing()
        {
            SaveTabs();
            DumpCookies(_current?.Url);
            base.OnDisappearing();
        }

        /// <inheritdoc/>
        protected override bool OnBackButtonPressed()
        {
            if (TryGoBackInWebView())
                return true;

            _ = CloseAsync();
            return true;
        }

        private static Button CreateIconButton(string text, double fontSize)
        {
            return new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                FontSize = fontSize,
                WidthRequest = 44,
                HeightRequest = 44,
                Padding = new Thickness(0),
                VerticalOptions = LayoutOptions.Center,
            };
        }

        // ----- storage -----

        private static string ResolveRootDirectory()
        {
            string external = null;
            try
            {
                external = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
            }
            catch
            {
            }

            string preferred = Path.Combine(string.IsNullOrEmpty(external) ? "/sdcard" : external, "abro");
            if (IsWritableDirectory(preferred))
                return preferred;

            // No (all-files) storage access: fall back to the private app directory.
            string fallback = Path.Combine(FileSystem.AppDataDirectory, "abro");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static bool IsWritableDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureDirectories()
        {
            foreach (string dir in new[] { _rootDir, _historyDir, _bookmarksDir, _cookiesDir, _cacheDir, _filesDir })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                }
            }
        }

        private void RestoreTabs()
        {
            TabsState state = null;
            try
            {
                if (File.Exists(_tabsFile))
                    state = JsonSerializer.Deserialize<TabsState>(File.ReadAllText(_tabsFile));
            }
            catch
            {
            }

            List<string> urls = state?.Tabs?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls == null || urls.Count == 0)
            {
                AddTab(HomeUrl, select: true);
                return;
            }

            int selected = Math.Clamp(state.Selected, 0, urls.Count - 1);
            for (int i = 0; i < urls.Count; i++)
                AddTab(urls[i], select: i == selected);
        }

        private void SaveTabs()
        {
            try
            {
                var state = new TabsState
                {
                    Selected = Math.Max(0, _tabs.IndexOf(_current)),
                    Tabs = _tabs.Select(t => t.Url ?? t.PendingUrl ?? HomeUrl).ToList(),
                };
                File.WriteAllText(_tabsFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        private void AppendHistory(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + url + "\t"
                    + (title ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                lock (HistoryLock)
                {
                    Directory.CreateDirectory(_historyDir);
                    File.AppendAllText(Path.Combine(_historyDir, "history.txt"), line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private void DumpCookies(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                CookieManager manager = CookieManager.Instance;
                manager?.Flush();
                string cookies = manager?.GetCookie(url);
                if (string.IsNullOrEmpty(cookies))
                    return;

                string host = new Uri(url).Host;
                foreach (char c in Path.GetInvalidFileNameChars())
                    host = host.Replace(c, '_');
                if (string.IsNullOrEmpty(host))
                    return;

                Directory.CreateDirectory(_cookiesDir);
                File.WriteAllText(Path.Combine(_cookiesDir, host + ".txt"), cookies.Replace("; ", "\n"), Encoding.UTF8);
            }
            catch
            {
            }
        }

        // ----- tabs -----

        private BrowserTab AddTab(string url, bool select)
        {
            var tab = new BrowserTab
            {
                PendingUrl = string.IsNullOrWhiteSpace(url) ? HomeUrl : url,
                Url = string.IsNullOrWhiteSpace(url) ? HomeUrl : url,
                View = new MauiWebView
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    BackgroundColor = Colors.Black,
                    IsVisible = false,
                },
            };
            tab.View.HandlerChanged += (s, e) => ConfigureAndroidWebView(tab);
            _tabs.Add(tab);
            _webHost.Add(tab.View);

            if (select || _current == null)
                SelectTab(tab);
            else
                RebuildTabStrip();

            SaveTabs();
            return tab;
        }

        private void SelectTab(BrowserTab tab)
        {
            if (tab == null)
                return;

            _current = tab;
            foreach (BrowserTab t in _tabs)
                t.View.IsVisible = ReferenceEquals(t, tab);

            LoadPendingUrl(tab);
            _addressEntry.Text = tab.Url;
            RebuildTabStrip();
            UpdateNavigationButtons();
            SaveTabs();
        }

        private void CloseTab(BrowserTab tab)
        {
            if (tab == null)
                return;

            int index = _tabs.IndexOf(tab);
            if (index < 0)
                return;

            if (_tabs.Count == 1)
            {
                // Always keep one tab open: reset it to the home page.
                tab.PendingUrl = HomeUrl;
                tab.Url = HomeUrl;
                tab.Title = null;
                tab.Loaded = false;
                LoadPendingUrl(tab);
                _addressEntry.Text = tab.Url;
                RebuildTabStrip();
                SaveTabs();
                return;
            }

            _tabs.RemoveAt(index);
            try
            {
                tab.Native?.StopLoading();
                _webHost.Remove(tab.View);
            }
            catch
            {
            }

            if (ReferenceEquals(_current, tab))
                SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
            else
            {
                RebuildTabStrip();
                SaveTabs();
            }
        }

        private void RebuildTabStrip()
        {
            _tabStrip.Children.Clear();
            foreach (BrowserTab tab in _tabs)
            {
                BrowserTab captured = tab;
                bool active = ReferenceEquals(tab, _current);

                var select = new Button
                {
                    Text = GetTabTitle(tab),
                    TextColor = active ? Colors.White : Color.FromArgb("#8e8e93"),
                    BackgroundColor = active ? Color.FromArgb("#2c2c2e") : Colors.Transparent,
                    FontSize = 13,
                    HeightRequest = 38,
                    MinimumWidthRequest = 60,
                    Padding = new Thickness(10, 0, 2, 0),
                    CornerRadius = 8,
                };
                select.Clicked += (s, e) => SelectTab(captured);

                var close = new Button
                {
                    Text = "✕",
                    TextColor = Color.FromArgb("#8e8e93"),
                    BackgroundColor = active ? Color.FromArgb("#2c2c2e") : Colors.Transparent,
                    FontSize = 12,
                    HeightRequest = 38,
                    WidthRequest = 32,
                    Padding = new Thickness(0),
                    CornerRadius = 8,
                };
                close.Clicked += (s, e) => CloseTab(captured);

                var item = new HorizontalStackLayout { Spacing = 0 };
                item.Children.Add(select);
                item.Children.Add(close);
                _tabStrip.Children.Add(item);
            }
        }

        private static string GetTabTitle(BrowserTab tab)
        {
            string text = tab.Title;
            if (string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    text = new Uri(tab.Url ?? string.Empty).Host;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(text))
                text = "New tab";
            return text.Length > 14 ? text.Substring(0, 13) + "…" : text;
        }

        // ----- navigation -----

        private void NavigateFromAddressBar()
        {
            string target = NormalizeAddress(_addressEntry.Text);
            if (target == null)
                return;

            _addressEntry.Unfocus();
            if (_current == null)
            {
                AddTab(target, select: true);
                return;
            }

            _current.PendingUrl = target;
            _current.Url = target;
            _current.Loaded = false;
            LoadPendingUrl(_current);
            SaveTabs();
        }

        private static string NormalizeAddress(string input)
        {
            string text = input?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;

            if (text.Contains("://") || text.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            bool looksLikeHost = !text.Contains(' ') && (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
            return looksLikeHost ? "https://" + text : SearchUrl + Uri.EscapeDataString(text);
        }

        private void LoadPendingUrl(BrowserTab tab)
        {
            if (tab?.Native == null || tab.Loaded || string.IsNullOrEmpty(tab.PendingUrl))
                return;

            tab.Loaded = true;
            string url = tab.PendingUrl;
            tab.PendingUrl = null;
            tab.Native.LoadUrl(url);
        }

        private void GoBack()
        {
            TryGoBackInWebView();
            UpdateNavigationButtons();
        }

        private void GoForward()
        {
            try
            {
                if (_current?.Native != null && _current.Native.CanGoForward())
                    _current.Native.GoForward();
            }
            catch
            {
            }

            UpdateNavigationButtons();
        }

        private void Reload()
        {
            try
            {
                if (_current?.Native == null)
                    return;
                if (!_current.Loaded)
                    LoadPendingUrl(_current);
                else
                    _current.Native.Reload();
            }
            catch
            {
            }
        }

        private bool TryGoBackInWebView()
        {
            try
            {
                if (_current?.Native != null && _current.Native.CanGoBack())
                {
                    _current.Native.GoBack();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void UpdateNavigationButtons()
        {
            try
            {
                bool canBack = _current?.Native?.CanGoBack() == true;
                bool canForward = _current?.Native?.CanGoForward() == true;
                _backButton.Opacity = canBack ? 1.0 : 0.4;
                _forwardButton.Opacity = canForward ? 1.0 : 0.4;
            }
            catch
            {
            }
        }

        private void OnPageStarted(BrowserTab tab, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            tab.Url = url;
            if (ReferenceEquals(tab, _current))
            {
                _addressEntry.Text = url;
                UpdateNavigationButtons();
            }
        }

        private void OnPageFinished(BrowserTab tab, string url, string title)
        {
            if (!string.IsNullOrEmpty(url))
                tab.Url = url;
            if (!string.IsNullOrWhiteSpace(title))
                tab.Title = title;

            AppendHistory(url, title);
            DumpCookies(url);
            SaveTabs();
            if (ReferenceEquals(tab, _current))
            {
                _addressEntry.Text = tab.Url;
                UpdateNavigationButtons();
            }

            RebuildTabStrip();
        }

        private void OnTitleChanged(BrowserTab tab, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title == tab.Title)
                return;

            tab.Title = title;
            RebuildTabStrip();
        }

        private void OnVisitedUrl(BrowserTab tab, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            tab.Url = url;
            if (ReferenceEquals(tab, _current))
            {
                _addressEntry.Text = url;
                UpdateNavigationButtons();
            }

            SaveTabs();
        }

        // ----- menu -----

        private async Task ShowMenuAsync()
        {
            const string history = "History";
            const string bookmarks = "Bookmarks";
            const string addBookmark = "Add bookmark";
            const string clearCookies = "Clear cookies";
            const string clearCache = "Clear cache";
            const string openFiles = "Open files folder";

            string choice = await DisplayActionSheetAsync(
                "Abro", "Cancel", null, history, bookmarks, addBookmark, clearCookies, clearCache, openFiles).ConfigureAwait(true);
            switch (choice)
            {
                case history:
                    await ShowHistoryAsync().ConfigureAwait(true);
                    break;
                case bookmarks:
                    await ShowBookmarksAsync().ConfigureAwait(true);
                    break;
                case addBookmark:
                    AddCurrentBookmark();
                    break;
                case clearCookies:
                    ClearCookies();
                    break;
                case clearCache:
                    ClearCache();
                    break;
                case openFiles:
                    await OpenFilesFolderAsync().ConfigureAwait(true);
                    break;
            }
        }

        private async Task ShowHistoryAsync()
        {
            var entries = ReadHistoryEntries();
            if (entries.Count == 0)
            {
                ShowToast("History is empty");
                return;
            }

            await PickAndOpenUrlAsync("History", entries).ConfigureAwait(true);
        }

        private async Task ShowBookmarksAsync()
        {
            var entries = ReadBookmarkEntries();
            if (entries.Count == 0)
            {
                ShowToast("No bookmarks yet");
                return;
            }

            await PickAndOpenUrlAsync("Bookmarks", entries).ConfigureAwait(true);
        }

        private List<(string Url, string Label)> ReadHistoryEntries()
        {
            var entries = new List<(string Url, string Label)>();
            try
            {
                string file = Path.Combine(_historyDir, "history.txt");
                if (!File.Exists(file))
                    return entries;

                string[] lines;
                lock (HistoryLock)
                {
                    lines = File.ReadAllLines(file, Encoding.UTF8);
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in lines.Skip(Math.Max(0, lines.Length - MaxHistoryLinesRead)).Reverse())
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 2 || !seen.Add(parts[1]))
                        continue;

                    string title = parts.Length > 2 ? parts[2] : string.Empty;
                    string label = string.IsNullOrWhiteSpace(title) ? parts[1] : title + " — " + parts[1];
                    if (label.Length > 90)
                        label = label.Substring(0, 89) + "…";
                    entries.Add((parts[1], label));
                    if (entries.Count >= MaxHistoryMenuEntries)
                        break;
                }
            }
            catch
            {
            }

            return entries;
        }

        private List<(string Url, string Label)> ReadBookmarkEntries()
        {
            var entries = new List<(string Url, string Label)>();
            try
            {
                string file = Path.Combine(_bookmarksDir, "bookmarks.txt");
                if (!File.Exists(file))
                    return entries;

                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in lines.Reverse())
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]) || !seen.Add(parts[0]))
                        continue;
                    string title = parts.Length > 1 ? parts[1] : string.Empty;
                    string label = string.IsNullOrWhiteSpace(title) ? parts[0] : title + " — " + parts[0];
                    if (label.Length > 90)
                        label = label.Substring(0, 89) + "…";
                    entries.Add((parts[0], label));
                    if (entries.Count >= MaxHistoryMenuEntries)
                        break;
                }
            }
            catch
            {
            }

            return entries;
        }

        private void AddCurrentBookmark()
        {
            string url = _current?.Url;
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("Nothing to bookmark");
                return;
            }

            try
            {
                Directory.CreateDirectory(_bookmarksDir);
                string title = _current.Title ?? string.Empty;
                string line = url + "\t" + title.Replace('\t', ' ') + Environment.NewLine;
                File.AppendAllText(Path.Combine(_bookmarksDir, "bookmarks.txt"), line, Encoding.UTF8);
                ShowToast("Bookmark added");
            }
            catch
            {
                ShowToast("Could not save bookmark");
            }
        }

        private async Task PickAndOpenUrlAsync(string title, List<(string Url, string Label)> entries)
        {
            string[] buttons = entries.Select((e, i) => (i + 1) + ". " + e.Label).ToArray();
            string choice = await DisplayActionSheetAsync(title, "Cancel", null, buttons).ConfigureAwait(true);
            int index = Array.IndexOf(buttons, choice);
            if (index < 0)
                return;

            OpenOrNavigate(entries[index].Url);
        }

        private void OpenOrNavigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (_current == null)
            {
                AddTab(url, select: true);
                return;
            }

            _current.PendingUrl = url;
            _current.Url = url;
            _current.Loaded = false;
            LoadPendingUrl(_current);
            _addressEntry.Text = _current.Url;
            SaveTabs();
        }

        private void ClearCookies()
        {
            try
            {
                CookieManager.Instance?.RemoveAllCookies(null);
                CookieManager.Instance?.Flush();
            }
            catch
            {
            }

            DeleteDirectoryContent(_cookiesDir);
            ShowToast("Cookies cleared");
        }

        private void ClearCache()
        {
            foreach (BrowserTab tab in _tabs)
            {
                try
                {
                    tab.Native?.ClearCache(true);
                }
                catch
                {
                }
            }

            DeleteDirectoryContent(_cacheDir);
            ShowToast("Cache cleared");
        }

        private async Task OpenFilesFolderAsync()
        {
            try
            {
                Directory.CreateDirectory(_filesDir);
                string relative = Path.GetRelativePath(
                    global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/sdcard", _filesDir);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                {
                    AUri uri = AUri.Parse("content://com.android.externalstorage.documents/document/primary%3A"
                        + AUri.Encode(relative.Replace('\\', '/')));
                    var intent = new Intent(Intent.ActionView);
                    intent.SetDataAndType(uri, "vnd.android.document/directory");
                    intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
                    Platform.AppContext.StartActivity(intent);
                    return;
                }
            }
            catch
            {
            }

            await DisplayAlertAsync("Abro files", _filesDir, "OK").ConfigureAwait(true);
        }

        private static void DeleteDirectoryContent(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return;

                foreach (string file in Directory.GetFiles(dir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }

                foreach (string sub in Directory.GetDirectories(dir))
                {
                    try
                    {
                        Directory.Delete(sub, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void ShowToast(string message)
        {
            try
            {
                global::Android.Widget.Toast.MakeText(Platform.AppContext, message, global::Android.Widget.ToastLength.Short)?.Show();
            }
            catch
            {
            }
        }

        // ----- android webview -----

        private void ConfigureAndroidWebView(BrowserTab tab)
        {
            if (tab.Native != null)
                return;
            if (tab.View.Handler?.PlatformView is not AWebView awv)
                return;

            tab.Native = awv;
            awv.Settings.JavaScriptEnabled = true;
            awv.Settings.DomStorageEnabled = true;
            awv.Settings.DatabaseEnabled = true;
            awv.Settings.LoadWithOverviewMode = true;
            awv.Settings.UseWideViewPort = true;
            awv.Settings.BuiltInZoomControls = true;
            awv.Settings.DisplayZoomControls = false;
            awv.Settings.SetSupportZoom(true);
            awv.Settings.MixedContentMode = MixedContentHandling.CompatibilityMode;
            awv.Settings.CacheMode = CacheModes.Default;
            awv.Settings.AllowFileAccess = false;
            awv.Settings.AllowContentAccess = true;
            awv.Settings.MediaPlaybackRequiresUserGesture = true;
            try
            {
                // The WebView keeps its HTTP cache in the app cache directory. This setting is
                // deprecated and ignored on newer Android versions, so it is best effort only.
#pragma warning disable CA1422, CS0618
                Directory.CreateDirectory(_cacheDir);
                awv.Settings.SetAppCacheEnabled(true);
                awv.Settings.SetAppCachePath(_cacheDir);
#pragma warning restore CA1422, CS0618
            }
            catch
            {
            }

            try
            {
                CookieManager.Instance?.SetAcceptCookie(true);
                CookieManager.Instance?.SetAcceptThirdPartyCookies(awv, true);
            }
            catch
            {
            }

            awv.SetWebViewClient(new AbroWebViewClient(this, tab));
            awv.SetWebChromeClient(new AbroWebChromeClient(this, tab));
            awv.SetDownloadListener(new AbroDownloadListener(_filesDir));

            if (ReferenceEquals(tab, _current))
            {
                LoadPendingUrl(tab);
                UpdateNavigationButtons();
            }
        }

        private async Task CloseAsync()
        {
            if (_closing)
                return;

            _closing = true;
            SaveTabs();
            DumpCookies(_current?.Url);
            foreach (BrowserTab tab in _tabs)
            {
                try
                {
                    tab.Native?.StopLoading();
                }
                catch
                {
                }
            }

            try
            {
                await Navigation.PopModalAsync(false).ConfigureAwait(true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Shows the browser as a modal page on top of the current page. Must run on the UI thread.
        /// </summary>
        public static Task Present() => Present(null, AbroStartAction.None);

        /// <summary>
        /// Shows the browser, optionally opening a URL and/or a history/bookmarks sheet.
        /// </summary>
        public static async Task Present(string url, AbroStartAction startAction)
        {
            if (MauiApplication.Current?.Windows == null || MauiApplication.Current.Windows.Count == 0)
                return;

            try
            {
                Page host = MauiApplication.Current.Windows[0].Page;
                if (host == null)
                    return;

                AbroBrowserPage existing = host.Navigation.ModalStack.OfType<AbroBrowserPage>().LastOrDefault();
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                        existing.OpenOrNavigate(NormalizeAddress(url) ?? url);
                    if (startAction == AbroStartAction.History)
                        await existing.ShowHistoryAsync().ConfigureAwait(true);
                    else if (startAction == AbroStartAction.Bookmarks)
                        await existing.ShowBookmarksAsync().ConfigureAwait(true);
                    return;
                }

                var page = new AbroBrowserPage(url, startAction);
                await host.Navigation.PushModalAsync(page, false).ConfigureAwait(true);
                await page.ApplyStartActionAsync().ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private async Task ApplyStartActionAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_initialUrl))
                    OpenOrNavigate(NormalizeAddress(_initialUrl) ?? _initialUrl);

                if (_startAction == AbroStartAction.History)
                    await ShowHistoryAsync().ConfigureAwait(true);
                else if (_startAction == AbroStartAction.Bookmarks)
                    await ShowBookmarksAsync().ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private sealed class BrowserTab
        {
            public MauiWebView View { get; set; }

            public AWebView Native { get; set; }

            public string Url { get; set; }

            public string PendingUrl { get; set; }

            public string Title { get; set; }

            public bool Loaded { get; set; }
        }

        private sealed class TabsState
        {
            public int Selected { get; set; }

            public List<string> Tabs { get; set; } = new List<string>();
        }

        private sealed class AbroWebViewClient : WebViewClient
        {
            private readonly AbroBrowserPage _page;
            private readonly BrowserTab _tab;

            public AbroWebViewClient(AbroBrowserPage page, BrowserTab tab)
            {
                _page = page;
                _tab = tab;
            }

            public override bool ShouldOverrideUrlLoading(AWebView view, IWebResourceRequest request)
            {
                return TryOpenExternally(request?.Url?.ToString());
            }

            public override void OnPageStarted(AWebView view, string url, global::Android.Graphics.Bitmap favicon)
            {
                base.OnPageStarted(view, url, favicon);
                _page.OnPageStarted(_tab, url);
            }

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                _page.OnPageFinished(_tab, url, view?.Title);
            }

            public override void DoUpdateVisitedHistory(AWebView view, string url, bool isReload)
            {
                base.DoUpdateVisitedHistory(view, url, isReload);
                _page.OnVisitedUrl(_tab, url);
            }

            private static bool TryOpenExternally(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return false;

                if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                try
                {
                    Intent intent = url.StartsWith("intent:", StringComparison.OrdinalIgnoreCase)
                        ? Intent.ParseUri(url, IntentUriType.Scheme)
                        : new Intent(Intent.ActionView, AUri.Parse(url));
                    intent.AddFlags(ActivityFlags.NewTask);
                    Platform.AppContext.StartActivity(intent);
                }
                catch
                {
                }

                return true;
            }
        }

        private sealed class AbroWebChromeClient : WebChromeClient
        {
            private readonly AbroBrowserPage _page;
            private readonly BrowserTab _tab;

            public AbroWebChromeClient(AbroBrowserPage page, BrowserTab tab)
            {
                _page = page;
                _tab = tab;
            }

            public override void OnReceivedTitle(AWebView view, string title)
            {
                base.OnReceivedTitle(view, title);
                _page.OnTitleChanged(_tab, title);
            }
        }

        private sealed class AbroDownloadListener : Java.Lang.Object, IDownloadListener
        {
            private readonly string _targetDir;

            public AbroDownloadListener(string targetDir)
            {
                _targetDir = targetDir;
            }

            public void OnDownloadStart(string url, string userAgent, string contentDisposition, string mimetype, long contentLength)
            {
                try
                {
                    Directory.CreateDirectory(_targetDir);
                    string name = URLUtil.GuessFileName(url, contentDisposition, mimetype);
                    var request = new global::Android.App.DownloadManager.Request(AUri.Parse(url));
                    request.SetMimeType(mimetype);
                    string cookies = CookieManager.Instance?.GetCookie(url);
                    if (!string.IsNullOrEmpty(cookies))
                        request.AddRequestHeader("Cookie", cookies);
                    if (!string.IsNullOrEmpty(userAgent))
                        request.AddRequestHeader("User-Agent", userAgent);
                    request.SetNotificationVisibility(global::Android.App.DownloadVisibility.VisibleNotifyCompleted);
                    request.SetDestinationUri(AUri.FromFile(new Java.IO.File(Path.Combine(_targetDir, name))));

                    var manager = (global::Android.App.DownloadManager)Platform.AppContext.GetSystemService(Context.DownloadService);
                    manager?.Enqueue(request);
                    ShowToast("Downloading " + name);
                }
                catch
                {
                    ShowToast("Download failed");
                }
            }
        }
    }
}
