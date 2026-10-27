// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.Util;
using Android.Webkit;
using Java.Interop;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using AWebView = Android.Webkit.WebView;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Full-screen in-app host for Telegram Mini Apps. Injects <c>TelegramWebviewProxy</c>,
    /// replies to viewport/theme/custom-method events (CloudStorage), and prolongs the session.
    /// </summary>
    internal sealed class TelegramWebAppPage : ContentPage
    {
        private static readonly TimeSpan ProlongInterval = TimeSpan.FromSeconds(50);

        private static readonly Dictionary<string, string> ThemeParams = new Dictionary<string, string>
        {
            ["bg_color"] = "#1c1c1e",
            ["secondary_bg_color"] = "#2c2c2e",
            ["text_color"] = "#ffffff",
            ["hint_color"] = "#8e8e93",
            ["link_color"] = "#64b5ef",
            ["button_color"] = "#2481cc",
            ["button_text_color"] = "#ffffff",
            ["header_bg_color"] = "#1c1c1e",
            ["accent_text_color"] = "#64b5ef",
            ["section_bg_color"] = "#1c1c1e",
            ["section_header_text_color"] = "#8e8e93",
            ["subtitle_text_color"] = "#8e8e93",
            ["destructive_text_color"] = "#ff453a",
        };

        private readonly BotWebAppHostRequest _request;
        private readonly TaskCompletionSource _closedSource;
        private readonly MauiWebView _webView;
        private readonly Label _titleLabel;
        private CancellationTokenSource _prolongCts;
        private TelegramJsBridge _bridge;
        private AWebView _androidWebView;
        private bool _closed;
        private bool _androidConfigured;
        private bool _expanded = true;

        public TelegramWebAppPage(
            BotWebAppHostRequest request,
            TaskCompletionSource closedSource)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _closedSource = closedSource ?? throw new ArgumentNullException(nameof(closedSource));
            BackgroundColor = Colors.Black;

            _titleLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(request.Title) ? "Bot" : request.Title,
                TextColor = Colors.White,
                FontSize = 16,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                HorizontalOptions = LayoutOptions.Fill,
            };

            Button closeButton = new Button
            {
                Text = "✕",
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                FontSize = 20,
                WidthRequest = 48,
                HeightRequest = 48,
                Padding = new Thickness(0),
            };
            closeButton.Clicked += (s, e) => Close();

            var header = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                HeightRequest = 52,
                Padding = new Thickness(8, 0, 4, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            header.Add(_titleLabel, 0, 0);
            header.Add(closeButton, 1, 0);

            _webView = new MauiWebView
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                BackgroundColor = Colors.Black,
            };
            _webView.HandlerChanged += OnWebViewHandlerChanged;

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
            };
            root.Add(header, 0, 0);
            root.Add(_webView, 0, 1);
            Content = root;
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartProlongLoop();
            if (_webView.Source == null && !string.IsNullOrWhiteSpace(_request.Url))
                _webView.Source = new UrlWebViewSource { Url = _request.Url };
        }

        /// <inheritdoc/>
        protected override void OnDisappearing()
        {
            StopProlongLoop();
            base.OnDisappearing();
        }

        /// <inheritdoc/>
        protected override bool OnBackButtonPressed()
        {
            if (TryGoBackInWebView())
                return true;
            Close();
            return true;
        }

        private void OnWebViewHandlerChanged(object sender, EventArgs e)
        {
            ConfigureAndroidWebView();
        }

        private void ConfigureAndroidWebView()
        {
            if (_androidConfigured)
                return;
            if (_webView.Handler?.PlatformView is not AWebView awv)
                return;

            _androidConfigured = true;
            _androidWebView = awv;
            awv.Settings.JavaScriptEnabled = true;
            awv.Settings.DomStorageEnabled = true;
            awv.Settings.DatabaseEnabled = true;
            awv.Settings.MediaPlaybackRequiresUserGesture = false;
            awv.Settings.LoadWithOverviewMode = false;
            awv.Settings.UseWideViewPort = true;
            awv.Settings.BuiltInZoomControls = false;
            awv.Settings.DisplayZoomControls = false;
            awv.Settings.SetSupportZoom(false);
            awv.Settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
            awv.Settings.CacheMode = CacheModes.Default;
            awv.Settings.AllowFileAccess = true;
            awv.Settings.AllowContentAccess = true;
            try
            {
                CookieManager.Instance?.SetAcceptCookie(true);
                CookieManager.Instance?.SetAcceptThirdPartyCookies(awv, true);
            }
            catch
            {
            }

            string baseUa = awv.Settings.UserAgentString ?? string.Empty;
            if (baseUa.IndexOf("Telegram-Android", StringComparison.OrdinalIgnoreCase) < 0)
                awv.Settings.UserAgentString = baseUa + " Telegram-Android/11.5.3 (Android 13; SDK 33; SAFE)";

            _bridge = new TelegramJsBridge(OnWebAppEvent);
            awv.AddJavascriptInterface(_bridge, "TelegramWebviewProxy");
            awv.SetWebViewClient(new MiniAppWebViewClient(OnExternalUrl, OnPageFinished));
            awv.SetWebChromeClient(new WebChromeClient());
        }

        private void OnPageFinished(string url)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Mini Apps request these on load; reply even if they raced ahead of the bridge.
                PushTheme();
                PushViewport(isStateStable: true);
            });
        }

        private bool TryGoBackInWebView()
        {
            try
            {
                if (_androidWebView != null && _androidWebView.CanGoBack())
                {
                    _androidWebView.GoBack();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void OnWebAppEvent(string eventType, string eventData)
        {
            if (string.IsNullOrEmpty(eventType))
                return;

            try
            {
                Log.Info("TgWebApp", "postEvent " + eventType + " " + Truncate(eventData, 300));
            }
            catch
            {
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    switch (eventType)
                    {
                        case "web_app_close":
                            Close();
                            break;

                        case "web_app_data_send":
                            await HandleDataSendAsync(eventData).ConfigureAwait(true);
                            Close();
                            break;

                        case "web_app_open_link":
                            OpenLinkFromMiniApp(ExtractJsonString(eventData, "url"));
                            break;

                        case "web_app_open_tg_link":
                            OpenTelegramPath(ExtractJsonString(eventData, "path_full"));
                            break;

                        case "web_app_expand":
                            _expanded = true;
                            PushViewport(isStateStable: true);
                            break;

                        case "web_app_ready":
                            PushTheme();
                            PushViewport(isStateStable: true);
                            PushSafeArea();
                            break;

                        case "web_app_request_theme":
                            PushTheme();
                            break;

                        case "web_app_request_viewport":
                            PushViewport(isStateStable: true);
                            break;

                        case "web_app_request_safe_area":
                        case "web_app_request_content_safe_area":
                            PushSafeArea();
                            break;

                        case "web_app_invoke_custom_method":
                            await HandleCustomMethodAsync(eventData).ConfigureAwait(true);
                            break;

                        case "web_app_setup_main_button":
                        case "web_app_setup_back_button":
                        case "web_app_trigger_haptic_feedback":
                        case "web_app_set_header_color":
                        case "web_app_set_background_color":
                        case "web_app_set_bottom_bar_color":
                        case "web_app_setup_settings_button":
                        case "web_app_setup_swipe_behavior":
                        case "web_app_disable_vertical_swipes":
                        case "web_app_enable_vertical_swipes":
                            break;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        Log.Warn("TgWebApp", "event handler failed: " + eventType + " " + ex.Message);
                    }
                    catch
                    {
                    }
                }
            });
        }

        private async Task HandleCustomMethodAsync(string eventData)
        {
            string reqId = ExtractJsonString(eventData, "req_id");
            string method = ExtractJsonString(eventData, "method");
            if (string.IsNullOrEmpty(reqId))
                return;

            try
            {
                string paramsJson = ExtractJsonObjectString(eventData, "params") ?? "{}";
                Log.Info("TgWebApp", "custom_method " + method + " req=" + reqId + " params=" + Truncate(paramsJson, 200));
                if (_request.InvokeCustomMethodAsync == null)
                {
                    ReceiveEvent("custom_method_invoked", new Dictionary<string, object>
                    {
                        ["req_id"] = reqId,
                        ["error"] = "UNSUPPORTED",
                    });
                    return;
                }

                string resultJson = await _request.InvokeCustomMethodAsync(
                    method ?? string.Empty,
                    paramsJson,
                    CancellationToken.None).ConfigureAwait(true);

                Log.Info("TgWebApp", "custom_method ok " + method + " result=" + Truncate(resultJson, 300));
                object resultPayload = ParseJsonPayload(resultJson);
                ReceiveEvent("custom_method_invoked", new Dictionary<string, object>
                {
                    ["req_id"] = reqId,
                    ["result"] = resultPayload,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("TgWebApp", "custom_method fail " + method + ": " + ex.Message);
                ReceiveEvent("custom_method_invoked", new Dictionary<string, object>
                {
                    ["req_id"] = reqId,
                    ["error"] = ex.Message ?? "INTERNAL_ERROR",
                });
            }
        }

        private async Task HandleDataSendAsync(string eventData)
        {
            if (_request.SendWebAppDataAsync == null)
                return;

            string buttonText = ExtractJsonString(eventData, "button_text") ?? string.Empty;
            string data = ExtractJsonString(eventData, "data") ?? eventData ?? string.Empty;
            await _request.SendWebAppDataAsync(buttonText, data, CancellationToken.None).ConfigureAwait(false);
        }

        private void PushTheme()
        {
            ReceiveEvent("theme_changed", new Dictionary<string, object>
            {
                ["theme_params"] = ThemeParams,
            });
        }

        private void PushViewport(bool isStateStable)
        {
            GetViewportCssSize(out int width, out int height);
            if (height <= 0)
                height = 1;
            if (width <= 0)
                width = 1;

            ReceiveEvent("viewport_changed", new Dictionary<string, object>
            {
                ["height"] = height,
                ["width"] = width,
                ["is_expanded"] = _expanded,
                ["is_state_stable"] = isStateStable,
            });
        }

        private void PushSafeArea()
        {
            var zero = new Dictionary<string, object>
            {
                ["top"] = 0,
                ["bottom"] = 0,
                ["left"] = 0,
                ["right"] = 0,
            };
            ReceiveEvent("safe_area_changed", zero);
            ReceiveEvent("content_safe_area_changed", zero);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max) + "…";
        }

        private void GetViewportCssSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                AWebView awv = _androidWebView;
                if (awv == null)
                    return;

                float density = awv.Resources?.DisplayMetrics?.Density ?? 1f;
                if (density <= 0f)
                    density = 1f;

                int pxW = awv.Width > 0 ? awv.Width : awv.MeasuredWidth;
                int pxH = awv.Height > 0 ? awv.Height : awv.MeasuredHeight;
                width = Math.Max(1, (int)Math.Round(pxW / density));
                height = Math.Max(1, (int)Math.Round(pxH / density));
            }
            catch
            {
            }
        }

        private void ReceiveEvent(string eventType, object eventData)
        {
            AWebView awv = _androidWebView;
            if (awv == null || string.IsNullOrEmpty(eventType))
                return;

            try
            {
                string dataJson = JsonSerializer.Serialize(eventData ?? new Dictionary<string, object>());
                string typeJson = JsonSerializer.Serialize(eventType);
                string script =
                    "(function(){try{"
                    + "var type=" + typeJson + ";"
                    + "var data=" + dataJson + ";"
                    + "if(window.Telegram&&Telegram.WebView&&Telegram.WebView.receiveEvent)Telegram.WebView.receiveEvent(type,data);"
                    + "else if(window.TelegramGameProxy&&TelegramGameProxy.receiveEvent)TelegramGameProxy.receiveEvent(type,data);"
                    + "else if(typeof TelegramGameProxy_receiveEvent==='function')TelegramGameProxy_receiveEvent(type,data);"
                    + "}catch(e){}})();";
                awv.EvaluateJavascript(script, null);
            }
            catch
            {
            }
        }

        private void OnExternalUrl(string url)
        {
            MainThread.BeginInvokeOnMainThread(() => OpenExternalUrl(url));
        }

        private void OpenLinkFromMiniApp(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            if (TelegramLinkParser.IsTelegramLink(url))
            {
                NavigateToTelegramLink(url);
                return;
            }

            OpenExternalUrl(url);
        }

        private void OpenTelegramPath(string pathFull)
        {
            if (string.IsNullOrWhiteSpace(pathFull))
                return;
            string path = pathFull.StartsWith('/') ? pathFull : "/" + pathFull;
            NavigateToTelegramLink(path);
        }

        private void NavigateToTelegramLink(string linkOrPath)
        {
            Close();
            TelegramLinkRequest.Pending = new TelegramLinkRequest { Link = linkOrPath };
            try
            {
                Ioc.Instance.GetService<IMessengerService>()?.Send(new TelegramLinkMessage());
            }
            catch
            {
            }
        }

        private static void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
            }
            catch
            {
            }
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrEmpty(propertyName))
                return null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(propertyName, out JsonElement el) && el.ValueKind == JsonValueKind.String)
                    return el.GetString();
            }
            catch
            {
            }

            return null;
        }

        private static string ExtractJsonObjectString(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrEmpty(propertyName))
                return null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(propertyName, out JsonElement el))
                    return null;
                if (el.ValueKind == JsonValueKind.Object || el.ValueKind == JsonValueKind.Array)
                    return el.GetRawText();
                if (el.ValueKind == JsonValueKind.String)
                    return el.GetString();
            }
            catch
            {
            }

            return null;
        }

        private static object ParseJsonPayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }
            catch
            {
                return json;
            }
        }

        private void StartProlongLoop()
        {
            StopProlongLoop();
            if (_request.ProlongAsync == null || !_request.HasQueryId)
                return;

            _prolongCts = new CancellationTokenSource();
            CancellationToken token = _prolongCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(ProlongInterval);
                    while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    {
                        try
                        {
                            await _request.ProlongAsync(token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private void StopProlongLoop()
        {
            try
            {
                _prolongCts?.Cancel();
                _prolongCts?.Dispose();
            }
            catch
            {
            }

            _prolongCts = null;
        }

        private void Close()
        {
            if (_closed)
                return;
            _closed = true;
            StopProlongLoop();
            _closedSource.TrySetResult();
        }

        /// <summary>
        /// JS bridge expected by telegram-web-app.js (<c>TelegramWebviewProxy.postEvent</c>).
        /// </summary>
        private sealed class TelegramJsBridge : Java.Lang.Object
        {
            private readonly Action<string, string> _handler;

            public TelegramJsBridge(Action<string, string> handler)
            {
                _handler = handler;
            }

            [JavascriptInterface]
            [Export("postEvent")]
            public void PostEvent(string eventType, string eventData)
            {
                _handler?.Invoke(eventType, eventData);
            }
        }

        private sealed class MiniAppWebViewClient : WebViewClient
        {
            private readonly Action<string> _openExternal;
            private readonly Action<string> _pageFinished;

            public MiniAppWebViewClient(Action<string> openExternal, Action<string> pageFinished)
            {
                _openExternal = openExternal;
                _pageFinished = pageFinished;
            }

            public override bool ShouldOverrideUrlLoading(AWebView view, IWebResourceRequest request)
            {
                string url = request?.Url?.ToString();
                if (ShouldOpenExternally(url))
                {
                    _openExternal?.Invoke(url);
                    return true;
                }

                return false;
            }

#pragma warning disable CS0672
            public override bool ShouldOverrideUrlLoading(AWebView view, string url)
            {
                if (ShouldOpenExternally(url))
                {
                    _openExternal?.Invoke(url);
                    return true;
                }

                return false;
            }
#pragma warning restore CS0672

            public override void OnPageFinished(AWebView view, string url)
            {
                base.OnPageFinished(view, url);
                _pageFinished?.Invoke(url);
            }

            private static bool ShouldOpenExternally(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return false;
                return url.StartsWith("tg:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("telegram:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("ton:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("intent:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("market:", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Shows the Mini App page and restores the previous root page when it closes.
        /// Must run on the UI thread. Does not wait for the Mini App to close.
        /// </summary>
        public static bool Present(BotWebAppHostRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
                return false;
            if (MauiApplication.Current?.Windows == null || MauiApplication.Current.Windows.Count == 0)
                return false;

            try
            {
                Window window = MauiApplication.Current.Windows[0];
                Page previousPage = window.Page;
                var closedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var page = new TelegramWebAppPage(request, closedSource);
                window.Page = page;

                _ = closedSource.Task.ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            if (MauiApplication.Current?.Windows != null
                                && MauiApplication.Current.Windows.Count > 0
                                && ReferenceEquals(MauiApplication.Current.Windows[0].Page, page))
                            {
                                MauiApplication.Current.Windows[0].Page = previousPage;
                            }
                        }
                        catch
                        {
                        }
                    });
                }, TaskScheduler.Default);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
