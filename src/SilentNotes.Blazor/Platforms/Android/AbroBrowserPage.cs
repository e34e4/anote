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
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Views;
using Android.Webkit;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using AUri = Android.Net.Uri;
using AView = Android.Views.View;
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
        private const int MaxHistoryMenuEntries = 30;
        private const int MaxHistoryLinesRead = 500;
        private const string CustomFontInjectScript =
            "(function(){try{if(document.getElementById('abro-custom-font'))return;"
            + "var s=document.createElement('style');s.id='abro-custom-font';"
            + "s.textContent=\"@font-face{font-family:'AbroCustom';src:url('https://abro.internal/custom-font') format('truetype');font-display:swap;}"
            + "html,body{font-family:'AbroCustom',system-ui,sans-serif !important;}\";"
            + "(document.head||document.documentElement).appendChild(s);}catch(e){}})();";

        private const string BlackModeInjectScript =
            "(function(){try{if(document.getElementById('abro-black-mode'))return;"
            + "var s=document.createElement('style');s.id='abro-black-mode';"
            + "s.textContent='html,body,body *{background-color:#000!important;background-image:none!important;"
            + "color:#fff!important;border-color:#333!important;box-shadow:none!important;}"
            + "a,a *{color:#9ecbff!important;}"
            + "input,textarea,select,button{background-color:#111!important;color:#fff!important;}'"
            + ";(document.head||document.documentElement).appendChild(s);}catch(e){}})();";

        private static readonly object HistoryLock = new object();

        private readonly List<BrowserTab> _tabs = new List<BrowserTab>();
        private readonly string _rootDir;
        private readonly string _historyDir;
        private readonly string _bookmarksDir;
        private readonly string _cookiesDir;
        private readonly string _cacheDir;
        private readonly string _filesDir;
        private readonly string _aiDir;
        private readonly string _adblockDir;
        private readonly string _fontsDir;
        private readonly string _tabsFile;
        private readonly Grid _webHost;
        private readonly HorizontalStackLayout _tabStrip;
        private readonly Entry _addressEntry;
        private readonly Button _backButton;
        private readonly Button _forwardButton;
        private readonly VerticalStackLayout _suggestionsStack;
        private readonly Border _suggestionsPanel;
        private readonly HorizontalStackLayout _passwordStack;
        private readonly Border _passwordPanel;
        private readonly Grid _contentHost;
        private Border _menuOverlay;
        private BrowserTab _current;
        private bool _restored;
        private bool _closing;
        private readonly string _initialUrl;
        private readonly AbroStartAction _startAction;
        private CancellationTokenSource _suggestCts;
        private int _suggestSeq;
        private static WeakReference<AbroBrowserPage> _liveInstance;

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
            _aiDir = Path.Combine(_rootDir, "ai");
            _adblockDir = Path.Combine(_rootDir, "adblock");
            _fontsDir = Path.Combine(_rootDir, "fonts");
            _tabsFile = Path.Combine(_rootDir, "tabs.json");
            EnsureDirectories();
            try
            {
                Ioc.Instance.GetService<IAbroAdBlockService>()?.EnsureFilterLoaded();
            }
            catch
            {
            }

            try
            {
                Ioc.Instance.GetService<IAbroDownloadService>()?.GetDownloadDirectory();
            }
            catch
            {
            }

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
            _addressEntry.TextChanged += (s, e) => OnAddressTextChanged(e.NewTextValue);
            _addressEntry.Unfocused += (s, e) => HideSuggestions();

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

            _suggestionsStack = new VerticalStackLayout { Spacing = 0 };
            _suggestionsPanel = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#2c2c2e"),
                IsVisible = false,
                Padding = new Thickness(0),
                Content = new ScrollView
                {
                    Content = _suggestionsStack,
                    MaximumHeightRequest = 220,
                },
            };

            _passwordStack = new HorizontalStackLayout { Spacing = 6, Padding = new Thickness(8, 4) };
            _passwordPanel = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#243447"),
                IsVisible = false,
                Padding = new Thickness(0),
                Content = new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = _passwordStack,
                },
            };

            _webHost = new Grid
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                BackgroundColor = Colors.Black,
            };

            _contentHost = new Grid();
            _contentHost.Add(_webHost);
            _contentHost.Add(_suggestionsPanel);
            Microsoft.Maui.Controls.Grid.SetRow(_suggestionsPanel, 0);
            _suggestionsPanel.VerticalOptions = LayoutOptions.Start;
            _suggestionsPanel.HorizontalOptions = LayoutOptions.Fill;

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
            };
            root.Add(header, 0, 0);
            root.Add(bar, 0, 1);
            root.Add(_passwordPanel, 0, 2);
            root.Add(_contentHost, 0, 3);
            Content = root;
            _liveInstance = new WeakReference<AbroBrowserPage>(this);
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_restored)
            {
                _restored = true;
                RestoreTabs();
            }

            RefreshPasswordSuggestions();
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
            foreach (string dir in new[] { _rootDir, _historyDir, _bookmarksDir, _cookiesDir, _cacheDir, _filesDir, _aiDir, _adblockDir, _fontsDir })
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

        private bool IsAdHostBlocked(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            try
            {
                IAbroAdBlockService service = Ioc.Instance.GetService<IAbroAdBlockService>();
                return service != null && service.IsBlocked(host);
            }
            catch
            {
                return false;
            }
        }

        private WebResourceResponse TryServeCustomFont()
        {
            try
            {
                IAbroBrowserSettingsService settings = Ioc.Instance.GetService<IAbroBrowserSettingsService>();
                if (settings == null || !settings.UseCustomFont)
                    return null;
                string path = settings.GetCustomFontPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                string mime = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                    ? "font/otf"
                    : "font/ttf";
                Stream stream = File.OpenRead(path);
                return new WebResourceResponse(mime, null, stream);
            }
            catch
            {
                return null;
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
                IAbroHistoryStoreService store = TryGetHistoryStoreService();
                if (store != null)
                {
                    store.AppendVisit(url, title);
                    ScheduleHistorySync();
                    return;
                }

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

            HideSuggestions();
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
            return looksLikeHost ? "https://" + text : BuildSearchUrl(text);
        }

        private static string BuildSearchUrl(string query)
        {
            try
            {
                IAbroBrowserSettingsService settings = Ioc.Instance.GetService<IAbroBrowserSettingsService>();
                if (settings != null)
                    return settings.BuildSearchUrl(query);
            }
            catch
            {
            }

            return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(query ?? string.Empty);
        }

        private void OnAddressTextChanged(string text)
        {
            _ = RefreshSuggestionsAsync(text);
        }

        private async Task RefreshSuggestionsAsync(string text)
        {
            int seq = Interlocked.Increment(ref _suggestSeq);
            _suggestCts?.Cancel();
            var cts = new CancellationTokenSource();
            _suggestCts = cts;

            text = text?.Trim() ?? string.Empty;
            if (text.Length < 2
                || text.Contains("://", StringComparison.Ordinal)
                || LooksLikeHost(text))
            {
                HideSuggestions();
                return;
            }

            IAbroBrowserSettingsService settings;
            try
            {
                settings = Ioc.Instance.GetService<IAbroBrowserSettingsService>();
            }
            catch
            {
                HideSuggestions();
                return;
            }

            if (settings == null || !settings.SearchSuggestionsEnabled)
            {
                HideSuggestions();
                return;
            }

            try
            {
                await Task.Delay(280, cts.Token).ConfigureAwait(true);
                IReadOnlyList<string> suggestions = await settings.GetSuggestionsAsync(text, cts.Token).ConfigureAwait(true);
                if (seq != _suggestSeq || cts.IsCancellationRequested)
                    return;

                ShowSuggestions(suggestions);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (seq == _suggestSeq)
                    HideSuggestions();
            }
        }

        private void ShowSuggestions(IReadOnlyList<string> suggestions)
        {
            _suggestionsStack.Children.Clear();
            if (suggestions == null || suggestions.Count == 0)
            {
                HideSuggestions();
                return;
            }

            foreach (string suggestion in suggestions)
            {
                string captured = suggestion;
                var label = new Label
                {
                    Text = captured,
                    TextColor = Colors.White,
                    FontSize = 14,
                    Padding = new Thickness(14, 12),
                    LineBreakMode = LineBreakMode.TailTruncation,
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => ApplySuggestion(captured);
                label.GestureRecognizers.Add(tap);
                _suggestionsStack.Children.Add(label);
                _suggestionsStack.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#3a3a3c") });
            }

            _suggestionsPanel.IsVisible = true;
        }

        private void HideSuggestions()
        {
            _suggestionsPanel.IsVisible = false;
            _suggestionsStack.Children.Clear();
        }

        private void ApplySuggestion(string suggestion)
        {
            HideSuggestions();
            _addressEntry.Text = suggestion;
            string target = BuildSearchUrl(suggestion);
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

        private static bool LooksLikeHost(string text)
        {
            return !text.Contains(' ')
                && (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
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
                RefreshPasswordSuggestions();
            }

            RebuildTabStrip();
            InjectCustomFontIfNeeded(tab);
            InjectBlackModeIfNeeded(tab);
        }

        private void InjectCustomFontIfNeeded(BrowserTab tab)
        {
            try
            {
                IAbroBrowserSettingsService settings = Ioc.Instance.GetService<IAbroBrowserSettingsService>();
                if (settings == null || !settings.UseCustomFont)
                    return;
                AWebView native = tab?.Native;
                if (native == null)
                    return;
                native.EvaluateJavascript(CustomFontInjectScript, null);
            }
            catch
            {
            }
        }

        private void InjectBlackModeIfNeeded(BrowserTab tab)
        {
            try
            {
                IAbroBrowserSettingsService settings = Ioc.Instance.GetService<IAbroBrowserSettingsService>();
                if (settings == null)
                    return;
                string url = tab?.Url ?? tab?.PendingUrl;
                if (!settings.ShouldApplyBlackMode(url))
                    return;
                AWebView native = tab?.Native;
                if (native == null)
                    return;
                native.EvaluateJavascript(BlackModeInjectScript, null);
            }
            catch
            {
            }
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
            const string ai = "AI";
            const string settings = "Settings";
            const string downloads = "Downloads";
            const string sync = "Sync";
            const string history = "History";
            const string bookmarks = "Bookmarks";
            const string addBookmark = "Add bookmark";
            const string bookyOpen = "Open from Booky";
            const string bookyAdd = "Add to Booky";
            const string passwords = "Passwords";
            const string clearCookies = "Clear cookies";
            const string clearCache = "Clear cache";
            const string openFiles = "Open files folder";

            string choice = await ShowTwoColumnMenuAsync(
                "Abro",
                ai,
                settings,
                downloads,
                sync,
                history,
                bookmarks,
                addBookmark,
                bookyOpen,
                bookyAdd,
                passwords,
                clearCookies,
                clearCache,
                openFiles).ConfigureAwait(true);
            switch (choice)
            {
                case ai:
                    await ShowAiMenuAsync().ConfigureAwait(true);
                    break;
                case settings:
                    await ShowSettingsMenuAsync().ConfigureAwait(true);
                    break;
                case downloads:
                    await OpenSettingsPageAsync(() => new AbroDownloadsPage(), "Downloads").ConfigureAwait(true);
                    break;
                case sync:
                    await ShowSyncMenuAsync().ConfigureAwait(true);
                    break;
                case history:
                    await ShowHistoryAsync().ConfigureAwait(true);
                    break;
                case bookmarks:
                    await ShowBookmarksAsync().ConfigureAwait(true);
                    break;
                case addBookmark:
                    AddCurrentBookmark();
                    break;
                case bookyOpen:
                    await ShowBookyAsync().ConfigureAwait(true);
                    break;
                case bookyAdd:
                    AddCurrentToBooky();
                    break;
                case passwords:
                    await ShowPasswordPickerAsync().ConfigureAwait(true);
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

        private Task<string> ShowTwoColumnMenuAsync(string title, params string[] items)
        {
            var tcs = new TaskCompletionSource<string>();
            try
            {
                DismissMenuOverlay();

                var grid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star),
                    },
                    ColumnSpacing = 8,
                    RowSpacing = 8,
                };

                for (int i = 0; i < items.Length; i++)
                {
                    string label = items[i];
                    int row = i / 2;
                    int col = i % 2;
                    if (row >= grid.RowDefinitions.Count)
                        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                    var button = new Button
                    {
                        Text = label,
                        TextColor = Colors.White,
                        BackgroundColor = Color.FromArgb("#3a3a3c"),
                        CornerRadius = 10,
                        FontSize = 14,
                        HeightRequest = 44,
                        Padding = new Thickness(8, 0),
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    button.Clicked += (s, e) =>
                    {
                        DismissMenuOverlay();
                        tcs.TrySetResult(label);
                    };
                    grid.Add(button, col, row);
                }

                var titleLabel = new Label
                {
                    Text = title,
                    TextColor = Colors.White,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 0, 12),
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    TextColor = Colors.White,
                    BackgroundColor = Color.FromArgb("#2c2c2e"),
                    CornerRadius = 10,
                    HeightRequest = 44,
                    Margin = new Thickness(0, 12, 0, 0),
                };
                cancelButton.Clicked += (s, e) =>
                {
                    DismissMenuOverlay();
                    tcs.TrySetResult(null);
                };

                var panel = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = Color.FromArgb("#1c1c1e"),
                    Padding = new Thickness(16),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    WidthRequest = 340,
                    MaximumWidthRequest = 360,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 0,
                        Children = { titleLabel, grid, cancelButton },
                    },
                };

                var dimTap = new TapGestureRecognizer();
                dimTap.Tapped += (s, e) =>
                {
                    DismissMenuOverlay();
                    tcs.TrySetResult(null);
                };

                var dim = new BoxView
                {
                    Color = Color.FromRgba(0, 0, 0, 160),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                };
                dim.GestureRecognizers.Add(dimTap);

                _menuOverlay = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(16),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    Content = new Grid
                    {
                        Children = { dim, panel },
                    },
                };

                _contentHost.Add(_menuOverlay);
            }
            catch
            {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        private void DismissMenuOverlay()
        {
            try
            {
                if (_menuOverlay != null)
                {
                    _contentHost.Children.Remove(_menuOverlay);
                    _menuOverlay = null;
                }
            }
            catch
            {
            }
        }

        // ----- AI -----

        private async Task ShowAiMenuAsync()
        {
            const string newChat = "New chat";
            const string withSelection = "Chat with selection";
            const string previousChats = "Previous chats";

            string choice = await DisplayActionSheetAsync(
                "AI",
                "Cancel",
                null,
                newChat,
                withSelection,
                previousChats).ConfigureAwait(true);
            if (string.IsNullOrEmpty(choice) || choice == "Cancel")
                return;

            if (choice == newChat)
            {
                await OpenAiChatAsync(null, null).ConfigureAwait(true);
                return;
            }

            if (choice == withSelection)
            {
                await OpenAiWithSelectionAsync(_current?.Native).ConfigureAwait(true);
                return;
            }

            if (choice == previousChats)
                await OpenAiConversationsAsync().ConfigureAwait(true);
        }

        private async Task OpenAiConversationsAsync()
        {
            try
            {
                var page = new AbroAiConversationsPage();
                await Navigation.PushModalAsync(page, false).ConfigureAwait(true);
            }
            catch (Exception)
            {
                ShowToast("Could not open previous chats");
            }
        }

        private async Task OpenAiChatAsync(Guid? conversationId, string initialText)
        {
            try
            {
                var page = new AbroAiChatPage(conversationId, initialText);
                await Navigation.PushModalAsync(page, false).ConfigureAwait(true);
            }
            catch (Exception)
            {
                ShowToast("Could not open AI chat");
            }
        }

        private async Task OpenAiWithSelectionAsync(AWebView webView)
        {
            string selected = await GetSelectedTextAsync(webView).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(selected))
            {
                ShowToast("No text selected");
                return;
            }

            await OpenAiChatAsync(null, selected.Trim()).ConfigureAwait(true);
        }

        private static Task<string> GetSelectedTextAsync(AWebView webView)
        {
            var tcs = new TaskCompletionSource<string>();
            if (webView == null)
            {
                tcs.TrySetResult(string.Empty);
                return tcs.Task;
            }

            try
            {
                webView.EvaluateJavascript(
                    "(function(){try{return window.getSelection().toString();}catch(e){return '';}})()",
                    new AbroJsResultCallback(value =>
                    {
                        string text = value ?? string.Empty;
                        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                        {
                            try
                            {
                                text = JsonSerializer.Deserialize<string>(text) ?? string.Empty;
                            }
                            catch
                            {
                                text = text.Substring(1, text.Length - 2);
                            }
                        }

                        tcs.TrySetResult(text);
                    }));
            }
            catch
            {
                tcs.TrySetResult(string.Empty);
            }

            return tcs.Task;
        }

        private async Task ShowSettingsMenuAsync()
        {
            const string providers = "AI providers";
            const string sync = "AI sync";
            const string adBlocking = "Ad blocking";
            const string search = "Search";
            const string font = "Font";
            const string blackMode = "Black mode";
            string choice = await DisplayActionSheetAsync(
                "Settings",
                "Cancel",
                null,
                providers,
                sync,
                adBlocking,
                search,
                font,
                blackMode).ConfigureAwait(true);
            switch (choice)
            {
                case providers:
                    await ShowAiProvidersMenuAsync().ConfigureAwait(true);
                    break;
                case sync:
                    await ShowAiSyncMenuAsync().ConfigureAwait(true);
                    break;
                case adBlocking:
                    await OpenSettingsPageAsync(() => new AbroAdBlockPage(), "Ad blocking").ConfigureAwait(true);
                    break;
                case search:
                    await OpenSettingsPageAsync(() => new AbroSearchSettingsPage(), "Search").ConfigureAwait(true);
                    break;
                case font:
                    await OpenSettingsPageAsync(() => new AbroFontSettingsPage(), "Font").ConfigureAwait(true);
                    break;
                case blackMode:
                    await OpenSettingsPageAsync(() => new AbroBlackModeSettingsPage(), "Black mode").ConfigureAwait(true);
                    break;
            }
        }

        private async Task OpenSettingsPageAsync(Func<Page> createPage, string label)
        {
            try
            {
                await Navigation.PushModalAsync(createPage(), false).ConfigureAwait(true);
            }
            catch (Exception)
            {
                ShowToast("Could not open " + label);
            }
        }

        private async Task ShowAiProvidersMenuAsync()
        {
            IAbroAiStoreService storeService = TryGetAiStoreService();
            if (storeService == null)
            {
                ShowToast("AI store unavailable");
                return;
            }

            while (true)
            {
                AbroAiStoreModel store = storeService.LoadOrDefault();
                var buttons = new List<string> { "Add provider" };
                foreach (AbroAiProviderModel provider in store.Providers)
                {
                    bool isDefault = store.GetDefaultProvider()?.Id == provider.Id;
                    string label = (isDefault ? "★ " : string.Empty) + (provider.Name ?? "Provider");
                    buttons.Add(label);
                }

                string choice = await DisplayActionSheetAsync("AI providers", "Back", null, buttons.ToArray()).ConfigureAwait(true);
                if (string.IsNullOrEmpty(choice) || choice == "Back")
                    return;

                if (choice == "Add provider")
                {
                    await EditProviderAsync(storeService, null).ConfigureAwait(true);
                    continue;
                }

                int index = buttons.IndexOf(choice) - 1;
                if (index < 0 || index >= store.Providers.Count)
                    continue;

                await ShowProviderActionsAsync(storeService, store.Providers[index].Id).ConfigureAwait(true);
            }
        }

        private async Task ShowProviderActionsAsync(IAbroAiStoreService storeService, Guid providerId)
        {
            const string edit = "Edit";
            const string setDefault = "Set as default";
            const string delete = "Delete";
            string choice = await DisplayActionSheetAsync("Provider", "Back", delete, edit, setDefault).ConfigureAwait(true);
            AbroAiStoreModel store = storeService.LoadOrDefault();
            AbroAiProviderModel provider = store.Providers.Find(p => p.Id == providerId);
            if (provider == null)
                return;

            switch (choice)
            {
                case edit:
                    await EditProviderAsync(storeService, provider).ConfigureAwait(true);
                    break;
                case setDefault:
                    store.DefaultProviderId = provider.Id;
                    provider.ModifiedAt = DateTime.UtcNow;
                    storeService.TrySave(store);
                    ScheduleAiSync();
                    ShowToast("Default provider set");
                    break;
                case delete:
                    store.Providers.RemoveAll(p => p.Id == providerId);
                    if (store.DefaultProviderId == providerId)
                        store.DefaultProviderId = store.Providers.FirstOrDefault()?.Id;
                    storeService.TrySave(store);
                    ScheduleAiSync();
                    ShowToast("Provider deleted");
                    break;
            }
        }

        private async Task EditProviderAsync(IAbroAiStoreService storeService, AbroAiProviderModel existing)
        {
            string name = await DisplayPromptAsync(
                "Provider name",
                "Display name",
                "Next",
                "Cancel",
                existing?.Name ?? "Command Code",
                -1,
                Keyboard.Text,
                existing?.Name ?? string.Empty).ConfigureAwait(true);
            if (name == null)
                return;

            string url = await DisplayPromptAsync(
                "API URL",
                "Base URL ending with /v1",
                "Next",
                "Cancel",
                "https://api.commandcode.ai/provider/v1",
                -1,
                Keyboard.Url,
                existing?.Url ?? string.Empty).ConfigureAwait(true);
            if (url == null)
                return;

            string modelId = await DisplayPromptAsync(
                "Model ID",
                "Model sent to the API",
                "Next",
                "Cancel",
                "deepseek/deepseek-v4.1-flash",
                -1,
                Keyboard.Text,
                existing?.ModelId ?? string.Empty).ConfigureAwait(true);
            if (modelId == null)
                return;

            string apiKey = await DisplayPromptAsync(
                "API key",
                "Bearer token (stored in AI sync file)",
                "Save",
                "Cancel",
                string.Empty,
                -1,
                Keyboard.Text,
                existing?.ApiKey ?? string.Empty).ConfigureAwait(true);
            if (apiKey == null)
                return;

            AbroAiStoreModel store = storeService.LoadOrDefault();
            AbroAiProviderModel provider = existing == null
                ? new AbroAiProviderModel()
                : store.Providers.Find(p => p.Id == existing.Id) ?? existing;
            provider.Name = name.Trim();
            provider.Url = url.Trim();
            provider.ModelId = modelId.Trim();
            provider.ApiKey = apiKey.Trim();
            provider.ModifiedAt = DateTime.UtcNow;
            if (existing == null)
            {
                store.Providers.Add(provider);
                if (!store.DefaultProviderId.HasValue)
                    store.DefaultProviderId = provider.Id;
            }

            storeService.TrySave(store);
            ScheduleAiSync();
            ShowToast(existing == null ? "Provider added" : "Provider saved");
        }

        private async Task ShowAiSyncMenuAsync()
        {
            ISettingsService settingsService = TryGetSettingsService();
            if (settingsService == null)
            {
                ShowToast("Settings unavailable");
                return;
            }

            SettingsModel settings = settingsService.LoadSettingsOrDefault();
            string enableLabel = settings.AbroAiSyncEnabled ? "Disable sync" : "Enable sync";
            const string pathLabel = "Remote path";
            const string syncNow = "Sync now";
            const string overwrite = "Overwrite remote";

            string choice = await DisplayActionSheetAsync(
                "AI sync",
                "Back",
                null,
                enableLabel,
                pathLabel,
                syncNow,
                overwrite).ConfigureAwait(true);

            switch (choice)
            {
                case var _ when choice == enableLabel:
                    settings.AbroAiSyncEnabled = !settings.AbroAiSyncEnabled;
                    if (string.IsNullOrWhiteSpace(settings.AbroAiSyncRemotePath))
                        settings.AbroAiSyncRemotePath = "abro/ai.json";
                    settingsService.TrySaveSettingsToLocalDevice(settings);
                    ShowToast(settings.AbroAiSyncEnabled
                        ? "AI sync enabled (uses Booky WebDAV)"
                        : "AI sync disabled");
                    break;
                case pathLabel:
                    {
                        string path = await DisplayPromptAsync(
                            "Remote path",
                            "Relative to Booky WebDAV URL",
                            "Save",
                            "Cancel",
                            "abro/ai.json",
                            -1,
                            Keyboard.Text,
                            settings.AbroAiSyncRemotePath ?? "abro/ai.json").ConfigureAwait(true);
                        if (path == null)
                            return;
                        settings.AbroAiSyncRemotePath = path.Trim().TrimStart('/');
                        settingsService.TrySaveSettingsToLocalDevice(settings);
                        ShowToast("Remote path saved");
                        break;
                    }
                case syncNow:
                    await RunAiSyncAsync(overwrite: false).ConfigureAwait(true);
                    break;
                case overwrite:
                    await RunAiSyncAsync(overwrite: true).ConfigureAwait(true);
                    break;
            }
        }

        private async Task RunAiSyncAsync(bool overwrite)
        {
            IAbroAiSyncService syncService = TryGetAiSyncService();
            if (syncService == null)
            {
                ShowToast("AI sync unavailable");
                return;
            }

            if (!syncService.IsConfigured)
            {
                ShowToast("Enable AI sync and configure Booky WebDAV first");
                return;
            }

            ShowToast("Syncing…");
            AbroAiSyncResult result = overwrite
                ? await syncService.OverwriteRemoteAsync().ConfigureAwait(true)
                : await syncService.SyncAsync().ConfigureAwait(true);
            switch (result)
            {
                case AbroAiSyncResult.Success:
                    ShowToast("AI sync done");
                    break;
                case AbroAiSyncResult.Disabled:
                    ShowToast("AI sync disabled");
                    break;
                case AbroAiSyncResult.NotConfigured:
                    ShowToast("Booky WebDAV URL missing");
                    break;
                default:
                    ShowToast(string.IsNullOrEmpty(syncService.LastError) ? "AI sync failed" : syncService.LastError);
                    break;
            }
        }

        private void ScheduleAiSync()
        {
            IAbroAiSyncService syncService = TryGetAiSyncService();
            if (syncService?.IsConfigured == true)
                _ = syncService.SyncAsync();
        }

        private async Task ShowSyncMenuAsync()
        {
            ISettingsService settingsService = TryGetSettingsService();
            if (settingsService == null)
            {
                ShowToast("Settings unavailable");
                return;
            }

            SettingsModel settings = settingsService.LoadSettingsOrDefault();
            string bookmarksToggle = settings.AbroBookmarksSyncEnabled ? "Disable bookmarks sync" : "Enable bookmarks sync";
            string historyToggle = settings.AbroHistorySyncEnabled ? "Disable history sync" : "Enable history sync";
            string aiToggle = settings.AbroAiSyncEnabled ? "Disable AI sync" : "Enable AI sync";
            const string syncNow = "Sync now";
            const string overwrite = "Overwrite remote";
            const string bookmarksPath = "Bookmarks path";
            const string historyPath = "History path";
            const string aiPath = "AI path";

            string choice = await DisplayActionSheetAsync(
                "Sync",
                "Back",
                null,
                syncNow,
                overwrite,
                bookmarksToggle,
                historyToggle,
                aiToggle,
                bookmarksPath,
                historyPath,
                aiPath).ConfigureAwait(true);

            switch (choice)
            {
                case syncNow:
                    await RunBrowserSyncAsync(overwrite: false).ConfigureAwait(true);
                    break;
                case overwrite:
                    await RunBrowserSyncAsync(overwrite: true).ConfigureAwait(true);
                    break;
                case var _ when choice == bookmarksToggle:
                    settings.AbroBookmarksSyncEnabled = !settings.AbroBookmarksSyncEnabled;
                    if (string.IsNullOrWhiteSpace(settings.AbroBookmarksSyncRemotePath))
                        settings.AbroBookmarksSyncRemotePath = "abro/bookmarks.json";
                    settingsService.TrySaveSettingsToLocalDevice(settings);
                    ShowToast(settings.AbroBookmarksSyncEnabled
                        ? "Bookmarks sync enabled (Booky WebDAV)"
                        : "Bookmarks sync disabled");
                    break;
                case var _ when choice == historyToggle:
                    settings.AbroHistorySyncEnabled = !settings.AbroHistorySyncEnabled;
                    if (string.IsNullOrWhiteSpace(settings.AbroHistorySyncRemotePath))
                        settings.AbroHistorySyncRemotePath = "abro/history.json";
                    settingsService.TrySaveSettingsToLocalDevice(settings);
                    ShowToast(settings.AbroHistorySyncEnabled
                        ? "History sync enabled (Booky WebDAV)"
                        : "History sync disabled");
                    break;
                case var _ when choice == aiToggle:
                    settings.AbroAiSyncEnabled = !settings.AbroAiSyncEnabled;
                    if (string.IsNullOrWhiteSpace(settings.AbroAiSyncRemotePath))
                        settings.AbroAiSyncRemotePath = "abro/ai.json";
                    settingsService.TrySaveSettingsToLocalDevice(settings);
                    ShowToast(settings.AbroAiSyncEnabled
                        ? "AI sync enabled (Booky WebDAV)"
                        : "AI sync disabled");
                    break;
                case bookmarksPath:
                    {
                        string path = await DisplayPromptAsync(
                            "Bookmarks path",
                            "Relative to Booky WebDAV URL",
                            "Save",
                            "Cancel",
                            "abro/bookmarks.json",
                            -1,
                            Keyboard.Text,
                            settings.AbroBookmarksSyncRemotePath ?? "abro/bookmarks.json").ConfigureAwait(true);
                        if (path == null)
                            return;
                        settings.AbroBookmarksSyncRemotePath = path.Trim().TrimStart('/');
                        settingsService.TrySaveSettingsToLocalDevice(settings);
                        ShowToast("Bookmarks path saved");
                        break;
                    }
                case historyPath:
                    {
                        string path = await DisplayPromptAsync(
                            "History path",
                            "Relative to Booky WebDAV URL",
                            "Save",
                            "Cancel",
                            "abro/history.json",
                            -1,
                            Keyboard.Text,
                            settings.AbroHistorySyncRemotePath ?? "abro/history.json").ConfigureAwait(true);
                        if (path == null)
                            return;
                        settings.AbroHistorySyncRemotePath = path.Trim().TrimStart('/');
                        settingsService.TrySaveSettingsToLocalDevice(settings);
                        ShowToast("History path saved");
                        break;
                    }
                case aiPath:
                    {
                        string path = await DisplayPromptAsync(
                            "AI path",
                            "Relative to Booky WebDAV URL",
                            "Save",
                            "Cancel",
                            "abro/ai.json",
                            -1,
                            Keyboard.Text,
                            settings.AbroAiSyncRemotePath ?? "abro/ai.json").ConfigureAwait(true);
                        if (path == null)
                            return;
                        settings.AbroAiSyncRemotePath = path.Trim().TrimStart('/');
                        settingsService.TrySaveSettingsToLocalDevice(settings);
                        ShowToast("AI path saved");
                        break;
                    }
            }
        }

        private async Task RunBrowserSyncAsync(bool overwrite)
        {
            bool anyConfigured = false;
            var parts = new List<string>();

            IAbroBookmarkSyncService bookmarkSync = TryGetBookmarkSyncService();
            if (bookmarkSync?.IsConfigured == true)
            {
                anyConfigured = true;
                AbroBrowserSyncResult result = overwrite
                    ? await bookmarkSync.OverwriteRemoteAsync().ConfigureAwait(true)
                    : await bookmarkSync.SyncAsync().ConfigureAwait(true);
                parts.Add(result == AbroBrowserSyncResult.Success
                    ? "bookmarks ok"
                    : "bookmarks: " + (string.IsNullOrEmpty(bookmarkSync.LastError) ? result.ToString() : bookmarkSync.LastError));
            }

            IAbroHistorySyncService historySync = TryGetHistorySyncService();
            if (historySync?.IsConfigured == true)
            {
                anyConfigured = true;
                AbroBrowserSyncResult result = overwrite
                    ? await historySync.OverwriteRemoteAsync().ConfigureAwait(true)
                    : await historySync.SyncAsync().ConfigureAwait(true);
                parts.Add(result == AbroBrowserSyncResult.Success
                    ? "history ok"
                    : "history: " + (string.IsNullOrEmpty(historySync.LastError) ? result.ToString() : historySync.LastError));
            }

            IAbroAiSyncService aiSync = TryGetAiSyncService();
            if (aiSync?.IsConfigured == true)
            {
                anyConfigured = true;
                AbroAiSyncResult result = overwrite
                    ? await aiSync.OverwriteRemoteAsync().ConfigureAwait(true)
                    : await aiSync.SyncAsync().ConfigureAwait(true);
                parts.Add(result == AbroAiSyncResult.Success
                    ? "AI ok"
                    : "AI: " + (string.IsNullOrEmpty(aiSync.LastError) ? result.ToString() : aiSync.LastError));
            }

            if (!anyConfigured)
            {
                ShowToast("Enable sync items and configure Booky WebDAV first");
                return;
            }

            ShowToast(string.Join(" · ", parts));
        }

        private void ScheduleBookmarkSync()
        {
            IAbroBookmarkSyncService syncService = TryGetBookmarkSyncService();
            if (syncService?.IsConfigured == true)
                _ = syncService.SyncAsync();
        }

        private void ScheduleHistorySync()
        {
            IAbroHistorySyncService syncService = TryGetHistorySyncService();
            if (syncService?.IsConfigured == true)
                _ = syncService.SyncAsync();
        }

        private static IAbroAiStoreService TryGetAiStoreService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroAiStoreService>();
            }
            catch
            {
                return null;
            }
        }

        private static IAbroAiSyncService TryGetAiSyncService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroAiSyncService>();
            }
            catch
            {
                return null;
            }
        }

        private static IAbroBookmarkStoreService TryGetBookmarkStoreService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroBookmarkStoreService>();
            }
            catch
            {
                return null;
            }
        }

        private static IAbroBookmarkSyncService TryGetBookmarkSyncService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroBookmarkSyncService>();
            }
            catch
            {
                return null;
            }
        }

        private static IAbroHistoryStoreService TryGetHistoryStoreService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroHistoryStoreService>();
            }
            catch
            {
                return null;
            }
        }

        private static IAbroHistorySyncService TryGetHistorySyncService()
        {
            try
            {
                return Ioc.Instance.GetService<IAbroHistorySyncService>();
            }
            catch
            {
                return null;
            }
        }

        private static ISettingsService TryGetSettingsService()
        {
            try
            {
                return Ioc.Instance.GetService<ISettingsService>();
            }
            catch
            {
                return null;
            }
        }

        private async Task ShowHistoryAsync()
        {
            try
            {
                IAbroHistoryStoreService store = TryGetHistoryStoreService();
                if (store == null)
                {
                    ShowToast("History store unavailable");
                    return;
                }

                var page = new AbroHistoryPage(store, OpenOrNavigate, ScheduleHistorySync);
                await Navigation.PushModalAsync(page, false).ConfigureAwait(true);
            }
            catch
            {
                ShowToast("Could not open history");
            }
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
                IAbroBookmarkStoreService storeService = TryGetBookmarkStoreService();
                if (storeService != null)
                {
                    AbroBookmarkStoreModel store = storeService.LoadOrDefault();
                    foreach (AbroBookmarkItemModel item in store.Items
                        .OrderByDescending(i => i.ModifiedAt)
                        .Take(MaxHistoryMenuEntries))
                    {
                        if (string.IsNullOrWhiteSpace(item.Url))
                            continue;
                        string title = item.Title ?? string.Empty;
                        string label = string.IsNullOrWhiteSpace(title) ? item.Url : title + " — " + item.Url;
                        if (label.Length > 90)
                            label = label.Substring(0, 89) + "…";
                        entries.Add((item.Url, label));
                    }
                    return entries;
                }

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
                string title = _current.Title ?? string.Empty;
                IAbroBookmarkStoreService store = TryGetBookmarkStoreService();
                if (store != null)
                {
                    if (!store.AddOrUpdate(url, title))
                    {
                        ShowToast("Could not save bookmark");
                        return;
                    }
                    ScheduleBookmarkSync();
                    ShowToast("Bookmark added");
                    return;
                }

                Directory.CreateDirectory(_bookmarksDir);
                string line = url + "\t" + title.Replace('\t', ' ') + Environment.NewLine;
                File.AppendAllText(Path.Combine(_bookmarksDir, "bookmarks.txt"), line, Encoding.UTF8);
                ShowToast("Bookmark added");
            }
            catch
            {
                ShowToast("Could not save bookmark");
            }
        }

        private void AddCurrentToBooky()
        {
            string url = _current?.Url;
            if (string.IsNullOrWhiteSpace(url) || !BookyUrlUtils.LooksLikeUrl(url))
            {
                ShowToast("Nothing to add to Booky");
                return;
            }

            try
            {
                IRepositoryStorageService repo = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repo == null)
                {
                    ShowToast("Booky storage unavailable");
                    return;
                }

                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                string title = string.IsNullOrWhiteSpace(_current?.Title)
                    ? BookyUrlUtils.HostOf(url)
                    : _current.Title.Trim();
                var bookmark = new BookmarkModel
                {
                    Url = url.Trim(),
                    Title = title,
                    Quote = string.Empty,
                    Note = string.Empty,
                };
                model.Bookmarks.Insert(0, bookmark);
                repo.TrySaveRepository(model);
                ShowToast("Added to Booky");

                IBookySyncService sync = Ioc.Instance.GetService<IBookySyncService>();
                if (sync?.IsConfigured == true)
                    _ = sync.SyncAsync();
            }
            catch
            {
                ShowToast("Could not add to Booky");
            }
        }

        private async Task ShowBookyAsync()
        {
            try
            {
                IRepositoryStorageService repo = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repo == null)
                {
                    ShowToast("Booky storage unavailable");
                    return;
                }

                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                var entries = new List<(string Url, string Label)>();
                foreach (BookmarkModel bookmark in model.Bookmarks)
                {
                    if (string.IsNullOrWhiteSpace(bookmark.Url))
                        continue;
                    string label = string.IsNullOrWhiteSpace(bookmark.Title)
                        ? bookmark.Url
                        : bookmark.Title + " — " + bookmark.Url;
                    if (label.Length > 90)
                        label = label.Substring(0, 89) + "…";
                    entries.Add((TextFragmentBuilder.WithTextFragment(bookmark.Url, bookmark.Quote), label));
                    if (entries.Count >= MaxHistoryMenuEntries)
                        break;
                }

                if (entries.Count == 0)
                {
                    ShowToast("No Booky records yet");
                    return;
                }

                await PickAndOpenUrlAsync("Booky", entries).ConfigureAwait(true);
            }
            catch
            {
                ShowToast("Could not open Booky");
            }
        }

        private const string HasLoginFieldsScript =
            "(function(){try{"
            + "if(document.querySelector('input[type=password],input[autocomplete=current-password],"
            + "input[autocomplete=new-password]'))return true;"
            + "var forms=document.querySelectorAll('form');"
            + "for(var i=0;i<forms.length;i++){"
            + "var f=forms[i];"
            + "if(f.querySelector('input[type=password]'))return true;}"
            + "return false;}catch(e){return false;}})();";

        private int _passwordSuggestSeq;

        private void RefreshPasswordSuggestions()
        {
            try
            {
                _passwordStack.Children.Clear();
                _passwordPanel.IsVisible = false;

                IPasswordVaultService vault = Ioc.Instance.GetService<IPasswordVaultService>();
                string url = _current?.Url;
                AWebView native = _current?.Native;
                if (vault == null || string.IsNullOrWhiteSpace(url) || native == null)
                    return;

                if (!vault.IsUnlocked && !vault.VaultExists)
                    return;

                // Only show the strip when the page actually has a login/password field.
                int seq = Interlocked.Increment(ref _passwordSuggestSeq);
                native.EvaluateJavascript(HasLoginFieldsScript, new JsFillCallback(hasLogin =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (seq != _passwordSuggestSeq)
                            return;
                        if (!hasLogin)
                        {
                            _passwordStack.Children.Clear();
                            _passwordPanel.IsVisible = false;
                            return;
                        }

                        PopulatePasswordSuggestionButtons(vault, url);
                    });
                }));
            }
            catch
            {
                _passwordPanel.IsVisible = false;
            }
        }

        private void PopulatePasswordSuggestionButtons(IPasswordVaultService vault, string url)
        {
            try
            {
                _passwordStack.Children.Clear();
                if (vault == null || string.IsNullOrWhiteSpace(url))
                {
                    _passwordPanel.IsVisible = false;
                    return;
                }

                // Page may have navigated away while the JS probe was in flight.
                if (!string.Equals(_current?.Url, url, StringComparison.Ordinal))
                {
                    _passwordPanel.IsVisible = false;
                    return;
                }

                if (!vault.IsUnlocked)
                {
                    if (!vault.VaultExists)
                    {
                        _passwordPanel.IsVisible = false;
                        return;
                    }

                    var unlock = new Button
                    {
                        Text = "Unlock vault…",
                        FontSize = 12,
                        TextColor = Colors.White,
                        BackgroundColor = Color.FromArgb("#3a5f8a"),
                        Padding = new Thickness(10, 4),
                        HeightRequest = 32,
                        CornerRadius = 8,
                    };
                    unlock.Clicked += (s, e) => PromptVaultUnlock(url);
                    _passwordStack.Add(unlock);
                    _passwordPanel.IsVisible = true;
                    return;
                }

                IReadOnlyList<PasswordEntryModel> matches = vault.FindMatches(url);
                if (matches == null || matches.Count == 0)
                {
                    _passwordPanel.IsVisible = false;
                    return;
                }

                foreach (PasswordEntryModel entry in matches.Take(8))
                {
                    PasswordEntryModel local = entry;
                    string label = string.IsNullOrWhiteSpace(local.Name)
                        ? (local.Username ?? "Login")
                        : local.Name;
                    if (!string.IsNullOrWhiteSpace(local.Username) &&
                        !string.Equals(label, local.Username, StringComparison.Ordinal))
                        label += " · " + local.Username;
                    if (label.Length > 42)
                        label = label.Substring(0, 41) + "…";

                    var button = new Button
                    {
                        Text = "🔑 " + label,
                        FontSize = 12,
                        TextColor = Colors.White,
                        BackgroundColor = Color.FromArgb("#2f6fed"),
                        Padding = new Thickness(10, 4),
                        HeightRequest = 32,
                        CornerRadius = 8,
                    };
                    button.Clicked += (s, e) => FillPasswordEntry(local);
                    _passwordStack.Add(button);
                }

                _passwordPanel.IsVisible = true;
            }
            catch
            {
                _passwordPanel.IsVisible = false;
            }
        }

        private async Task ShowPasswordPickerAsync()
        {
            try
            {
                IPasswordVaultService vault = Ioc.Instance.GetService<IPasswordVaultService>();
                if (vault == null)
                {
                    ShowToast("Password vault unavailable");
                    return;
                }

                if (!vault.IsUnlocked)
                {
                    PromptVaultUnlock(_current?.Url);
                    ShowToast(vault.VaultExists ? "Unlock the vault first" : "No vault yet");
                    return;
                }

                string url = _current?.Url;
                IReadOnlyList<PasswordEntryModel> matches = string.IsNullOrWhiteSpace(url)
                    ? Array.Empty<PasswordEntryModel>()
                    : vault.FindMatches(url);
                if (matches.Count == 0 && vault.Vault?.Entries != null)
                    matches = vault.Vault.Entries.Take(MaxHistoryMenuEntries).ToList();

                if (matches.Count == 0)
                {
                    ShowToast("No matching passwords");
                    return;
                }

                string[] buttons = matches.Select((e, i) =>
                {
                    string name = string.IsNullOrWhiteSpace(e.Name) ? (e.Username ?? "Login") : e.Name;
                    string user = string.IsNullOrWhiteSpace(e.Username) ? string.Empty : " — " + e.Username;
                    string label = (i + 1) + ". " + name + user;
                    return label.Length > 90 ? label.Substring(0, 89) + "…" : label;
                }).ToArray();

                string choice = await DisplayActionSheetAsync("Passwords", "Cancel", null, buttons)
                    .ConfigureAwait(true);
                int index = Array.IndexOf(buttons, choice);
                if (index < 0)
                    return;

                FillPasswordEntry(matches[index]);
            }
            catch
            {
                ShowToast("Could not open passwords");
            }
        }

        private void PromptVaultUnlock(string url)
        {
            try
            {
                global::Android.App.Activity activity = Platform.CurrentActivity;
                if (activity == null)
                    return;

                string host = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(url))
                        host = AUri.Parse(url)?.Host;
                }
                catch
                {
                }

                var intent = new Intent(activity, typeof(VaultUnlockActivity));
                if (!string.IsNullOrEmpty(host))
                    intent.PutExtra(VaultUnlockActivity.ExtraWebDomain, host);
                activity.StartActivity(intent);
            }
            catch
            {
                ShowToast("Could not open vault unlock");
            }
        }

        private void FillPasswordEntry(PasswordEntryModel entry)
        {
            if (entry == null)
                return;

            AWebView native = _current?.Native;
            if (native == null)
            {
                ShowToast("No page to fill");
                return;
            }

            string user = JsonSerializer.Serialize(entry.Username ?? string.Empty);
            string pass = JsonSerializer.Serialize(entry.Password ?? string.Empty);
            string script =
                "(function(u,p){function setVal(el,v){if(!el)return;el.focus();el.value=v;"
                + "el.dispatchEvent(new Event('input',{bubbles:true}));"
                + "el.dispatchEvent(new Event('change',{bubbles:true}));}"
                + "var user=document.querySelector('input[type=email],input[autocomplete=username],"
                + "input[name*=user i],input[name*=email i],input[id*=user i],input[id*=email i],"
                + "input[type=text]:not([type=search]):not([type=hidden])');"
                + "var passEl=document.querySelector('input[type=password]');"
                + "setVal(user,u);setVal(passEl,p);return !!(user||passEl);})(" + user + "," + pass + ");";

            try
            {
                native.EvaluateJavascript(script, new JsFillCallback(filled =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        ShowToast(filled ? "Filled login" : "No login fields found"));
                }));
            }
            catch
            {
                ShowToast("Could not fill login");
            }
        }

        private sealed class JsFillCallback : Java.Lang.Object, IValueCallback
        {
            private readonly Action<bool> _done;

            public JsFillCallback(Action<bool> done)
            {
                _done = done;
            }

            public void OnReceiveValue(Java.Lang.Object value)
            {
                string raw = value?.ToString() ?? string.Empty;
                _done?.Invoke(raw.Contains("true", StringComparison.OrdinalIgnoreCase));
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
            awv.SetDownloadListener(new AbroDownloadListener(this));
            awv.LongClickable = true;
            awv.SetOnLongClickListener(new AbroLinkLongClickListener(this, awv));

            if (ReferenceEquals(tab, _current))
            {
                LoadPendingUrl(tab);
                UpdateNavigationButtons();
            }
        }

        private async Task ShowLinkMenuAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            const string share = "Share link";
            const string copy = "Copy link";
            const string newTab = "Open in new tab";
            string choice = await DisplayActionSheetAsync(TruncateForMenu(url, 80), "Cancel", null, share, copy, newTab)
                .ConfigureAwait(true);
            switch (choice)
            {
                case share:
                    ShareLink(url);
                    break;
                case copy:
                    CopyLink(url);
                    break;
                case newTab:
                    AddTab(url, select: true);
                    ShowToast("Opened in new tab");
                    break;
            }
        }

        private static void ShareLink(string url)
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
                ShowToast("Could not share link");
            }
        }

        private static void CopyLink(string url)
        {
            try
            {
                var clipboard = Platform.AppContext.GetSystemService(Context.ClipboardService)
                    as global::Android.Content.ClipboardManager;
                if (clipboard != null)
                    clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText("link", url);
                ShowToast("Link copied");
            }
            catch
            {
                ShowToast("Could not copy link");
            }
        }

        private static string TruncateForMenu(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max - 1) + "…";
        }

        private static string ResolveHitTestLink(AWebView webView)
        {
            try
            {
                AWebView.HitTestResult hit = webView?.GetHitTestResult();
                if (hit == null)
                    return null;

                // SrcAnchor = link, SrcImageAnchor = image that is also a link.
                // Constants live on Android.Webkit.HitTestResult; Type returns the same enum-like type.
                HitTestResult type = hit.Type;
                if (type == HitTestResult.SrcAnchorType
                    || type == HitTestResult.SrcImageAnchorType
                    || type == HitTestResult.EmailType
                    || type == HitTestResult.PhoneType)
                {
                    return hit.Extra;
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task CloseAsync()
        {
            if (_closing)
                return;

            _closing = true;
            // Prevent AbroTab from auto-Presenting again when the main task resumes.
            AbroLaunchGate.SuppressAutoPresent = true;
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

            _liveInstance = null;

            // Abro lives in AbroBrowserActivity. Closing only the MAUI Window leaves the activity
            // alive and MAUI recreates the window via App.CreateWindow — looks like an instant reopen.
            // Always Finish the host activity; CloseWindow is optional cleanup only.
            try
            {
                global::Android.App.Activity activity = Platform.CurrentActivity;
                if (activity is AbroBrowserActivity abroActivity)
                {
                    abroActivity.Finish();
                    await Task.CompletedTask.ConfigureAwait(true);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                if (Window != null && MauiApplication.Current != null)
                    MauiApplication.Current.CloseWindow(Window);
            }
            catch
            {
            }

            try
            {
                Platform.CurrentActivity?.Finish();
            }
            catch
            {
            }

            await Task.CompletedTask.ConfigureAwait(true);
        }

        /// <summary>
        /// Shows the browser in its own Android task/window. Must run on the UI thread.
        /// </summary>
        public static Task Present() => Present(null, AbroStartAction.None);

        /// <summary>
        /// Shows the browser, optionally opening a URL and/or a history/bookmarks sheet.
        /// </summary>
        public static Task Present(string url, AbroStartAction startAction)
        {
            AbroLaunchGate.SuppressAutoPresent = false;
            try
            {
                Context context = Platform.CurrentActivity
                    ?? (Context)global::Android.App.Application.Context;
                if (context == null)
                    return Task.CompletedTask;

                var intent = new Intent(context, typeof(AbroBrowserActivity));
                if (!string.IsNullOrWhiteSpace(url))
                    intent.PutExtra(AbroBrowserActivity.ExtraUrl, url);
                intent.PutExtra(AbroBrowserActivity.ExtraStartAction, (int)startAction);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront | ActivityFlags.SingleTop);
                context.StartActivity(intent);
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Builds a page for a freshly started <see cref="AbroBrowserActivity"/>.
        /// </summary>
        public static AbroBrowserPage CreateFromIntent(Intent intent)
        {
            string url = intent?.GetStringExtra(AbroBrowserActivity.ExtraUrl);
            AbroStartAction action = AbroStartAction.None;
            if (intent != null && intent.HasExtra(AbroBrowserActivity.ExtraStartAction))
            {
                int raw = intent.GetIntExtra(AbroBrowserActivity.ExtraStartAction, 0);
                if (Enum.IsDefined(typeof(AbroStartAction), raw))
                    action = (AbroStartAction)raw;
            }

            var page = new AbroBrowserPage(url, action);
            bool started = false;
            page.Appearing += async (s, e) =>
            {
                if (started)
                    return;
                started = true;
                try
                {
                    await page.ApplyStartActionAsync().ConfigureAwait(true);
                }
                catch
                {
                }
            };
            return page;
        }

        /// <summary>
        /// Applies a new intent while Abro is already running (SingleTask).
        /// </summary>
        public static void HandleIncomingIntent(Intent intent)
        {
            if (intent == null)
                return;
            if (_liveInstance == null || !_liveInstance.TryGetTarget(out AbroBrowserPage page) || page == null)
                return;

            string url = intent.GetStringExtra(AbroBrowserActivity.ExtraUrl);
            AbroStartAction action = AbroStartAction.None;
            if (intent.HasExtra(AbroBrowserActivity.ExtraStartAction))
            {
                int raw = intent.GetIntExtra(AbroBrowserActivity.ExtraStartAction, 0);
                if (Enum.IsDefined(typeof(AbroStartAction), raw))
                    action = (AbroStartAction)raw;
            }

            MainThread.BeginInvokeOnMainThread(() => ApplyIncoming(page, url, action));
        }

        private static void ApplyIncoming(AbroBrowserPage page, string url, AbroStartAction startAction)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url))
                    page.OpenOrNavigate(NormalizeAddress(url) ?? url);

                if (startAction == AbroStartAction.History)
                    _ = page.ShowHistoryAsync();
                else if (startAction == AbroStartAction.Bookmarks)
                    _ = page.ShowBookmarksAsync();

                page.RefreshPasswordSuggestions();
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

                RefreshPasswordSuggestions();
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
            private static readonly byte[] EmptyBody = Array.Empty<byte>();

            public AbroWebViewClient(AbroBrowserPage page, BrowserTab tab)
            {
                _page = page;
                _tab = tab;
            }

            public override WebResourceResponse ShouldInterceptRequest(AWebView view, IWebResourceRequest request)
            {
                try
                {
                    string url = request?.Url?.ToString();
                    if (!string.IsNullOrEmpty(url)
                        && url.StartsWith(AbroBrowserSettingsModel.CustomFontInterceptUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        WebResourceResponse fontResponse = _page.TryServeCustomFont();
                        if (fontResponse != null)
                            return fontResponse;
                    }

                    string host = request?.Url?.Host;
                    if (_page.IsAdHostBlocked(host))
                    {
                        return new WebResourceResponse(
                            "text/plain",
                            "utf-8",
                            new System.IO.MemoryStream(EmptyBody));
                    }
                }
                catch
                {
                }

                return base.ShouldInterceptRequest(view, request);
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
            public AbroDownloadListener(AbroBrowserPage page)
            {
            }

            public void OnDownloadStart(string url, string userAgent, string contentDisposition, string mimetype, long contentLength)
            {
                try
                {
                    IAbroDownloadService downloads = Ioc.Instance.GetService<IAbroDownloadService>();
                    if (downloads == null)
                    {
                        ShowToast("Downloads unavailable");
                        return;
                    }

                    string name = URLUtil.GuessFileName(url, contentDisposition, mimetype);
                    string cookies = null;
                    try
                    {
                        cookies = CookieManager.Instance?.GetCookie(url);
                    }
                    catch
                    {
                    }

                    long? lengthHint = contentLength > 0 ? contentLength : null;
                    AbroDownloadHistoryItemModel item = downloads.StartDownload(
                        url,
                        name,
                        userAgent,
                        cookies,
                        lengthHint);
                    ShowToast("Downloading " + (item?.FileName ?? name));
                }
                catch
                {
                    ShowToast("Download failed");
                }
            }
        }

        private sealed class AbroJsResultCallback : Java.Lang.Object, IValueCallback
        {
            private readonly Action<string> _onResult;

            public AbroJsResultCallback(Action<string> onResult)
            {
                _onResult = onResult;
            }

            public void OnReceiveValue(Java.Lang.Object value)
            {
                _onResult?.Invoke(value?.ToString());
            }
        }

        private sealed class AbroLinkLongClickListener : Java.Lang.Object, AView.IOnLongClickListener
        {
            private readonly AbroBrowserPage _page;
            private readonly AWebView _webView;

            public AbroLinkLongClickListener(AbroBrowserPage page, AWebView webView)
            {
                _page = page;
                _webView = webView;
            }

            public bool OnLongClick(AView v)
            {
                string url = ResolveHitTestLink(_webView);
                if (string.IsNullOrWhiteSpace(url))
                    return false;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await _page.ShowLinkMenuAsync(url).ConfigureAwait(true);
                });
                return true;
            }
        }
    }
}
