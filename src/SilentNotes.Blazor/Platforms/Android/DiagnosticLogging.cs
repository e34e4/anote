// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.Runtime;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using AndroidWebView = global::Android.Webkit.WebView;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Routes unhandled .NET / Android / WebView console errors to logcat so sleep/resume
    /// Blazor failures can be diagnosed with <c>adb logcat -s aNoteDiag aNoteWebView aNote</c>.
    /// </summary>
    internal static class DiagnosticLogging
    {
        public const string Tag = "aNoteDiag";
        public const string WebViewTag = "aNoteWebView";
        public const string DotNetTag = "aNote";

        private const string RenderBatchDesyncMarker = "unexpected acknowledgement for render batch";
        private static readonly object ReloadGate = new object();
        private static bool _installed;
        private static DateTime _lastReloadRequestUtc = DateTime.MinValue;

        /// <summary>
        /// Invoked on the UI thread when a known Blazor WebView render-batch desync is detected.
        /// MainPage wires this to click the Blazor error-UI Reload link / reload the WebView.
        /// </summary>
        public static Action ReloadRequested { get; set; }

        /// <summary>
        /// Installs process-wide exception hooks and enables WebView remote debugging.
        /// Safe to call more than once.
        /// </summary>
        public static void Install()
        {
            if (_installed)
                return;
            _installed = true;

            try
            {
                AndroidWebView.SetWebContentsDebuggingEnabled(true);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(Tag, "SetWebContentsDebuggingEnabled failed: " + ex);
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;

            global::Android.Util.Log.Info(Tag, "Diagnostic logging installed (UnhandledException, UnobservedTask, AndroidUnhandled, WebView debug, auto-reload).");
        }

        /// <summary>
        /// Wraps the Blazor WebChromeClient so console.error / warn from the WebView appears in logcat.
        /// </summary>
        public static void AttachWebViewConsoleLogging(AndroidWebView webView)
        {
            if (webView == null)
                return;

            try
            {
                WebChromeClient existing = webView.WebChromeClient;
                webView.SetWebChromeClient(new DiagnosticWebChromeClient(existing));
                global::Android.Util.Log.Info(Tag, "WebView console logging attached.");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(Tag, "AttachWebViewConsoleLogging failed: " + ex);
            }
        }

        public static ILoggerProvider CreateLoggerProvider()
        {
            return new AndroidLoggerProvider();
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            global::Android.Util.Log.Error(Tag, "UnhandledException (isTerminating=" + e.IsTerminating + "): " + e.ExceptionObject);
            RequestReloadIfRenderBatchDesync(e.ExceptionObject);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            global::Android.Util.Log.Error(Tag, "UnobservedTaskException: " + e.Exception);
            e.SetObserved();
            RequestReloadIfRenderBatchDesync(e.Exception);
        }

        private static void OnAndroidUnhandledException(object sender, RaiseThrowableEventArgs e)
        {
            global::Android.Util.Log.Error(Tag, "AndroidUnhandledException: " + e.Exception);
            RequestReloadIfRenderBatchDesync(e.Exception);
        }

        private static void RequestReloadIfRenderBatchDesync(object exceptionOrText)
        {
            string text = exceptionOrText?.ToString() ?? string.Empty;
            if (text.IndexOf(RenderBatchDesyncMarker, StringComparison.OrdinalIgnoreCase) < 0)
                return;

            lock (ReloadGate)
            {
                // Debounce the flood of UnobservedTaskException entries for one desync.
                if ((DateTime.UtcNow - _lastReloadRequestUtc) < TimeSpan.FromSeconds(3))
                    return;
                _lastReloadRequestUtc = DateTime.UtcNow;
            }

            Action handler = ReloadRequested;
            if (handler == null)
            {
                global::Android.Util.Log.Warn(Tag, "Render batch desync detected but no ReloadRequested handler is registered.");
                return;
            }

            global::Android.Util.Log.Warn(Tag, "Render batch desync detected — requesting WebView auto-reload.");
            try
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(handler);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(Tag, "Failed to dispatch ReloadRequested: " + ex);
            }
        }

        private sealed class DiagnosticWebChromeClient : WebChromeClient
        {
            private readonly WebChromeClient _inner;

            public DiagnosticWebChromeClient(WebChromeClient inner)
            {
                _inner = inner;
            }

            public override bool OnConsoleMessage(ConsoleMessage consoleMessage)
            {
                if (consoleMessage != null)
                {
                    ConsoleMessage.MessageLevel level = consoleMessage.InvokeMessageLevel();
                    global::Android.Util.LogPriority priority = global::Android.Util.LogPriority.Info;
                    if (level == ConsoleMessage.MessageLevel.Error)
                        priority = global::Android.Util.LogPriority.Error;
                    else if (level == ConsoleMessage.MessageLevel.Warning)
                        priority = global::Android.Util.LogPriority.Warn;
                    else if (level == ConsoleMessage.MessageLevel.Debug)
                        priority = global::Android.Util.LogPriority.Debug;
                    else if (level == ConsoleMessage.MessageLevel.Tip)
                        priority = global::Android.Util.LogPriority.Verbose;

                    string line = string.Format(
                        "{0}:{1}: {2}",
                        consoleMessage.SourceId(),
                        consoleMessage.LineNumber(),
                        consoleMessage.Message());
                    global::Android.Util.Log.WriteLine(priority, WebViewTag, line);
                }

                return _inner?.OnConsoleMessage(consoleMessage) ?? false;
            }

            public override void OnProgressChanged(AndroidWebView view, int newProgress)
            {
                if (_inner != null)
                    _inner.OnProgressChanged(view, newProgress);
                else
                    base.OnProgressChanged(view, newProgress);
            }

            public override void OnReceivedTitle(AndroidWebView view, string title)
            {
                if (_inner != null)
                    _inner.OnReceivedTitle(view, title);
                else
                    base.OnReceivedTitle(view, title);
            }

            public override bool OnShowFileChooser(
                AndroidWebView webView,
                IValueCallback filePathCallback,
                FileChooserParams fileChooserParams)
            {
                if (_inner != null)
                    return _inner.OnShowFileChooser(webView, filePathCallback, fileChooserParams);
                return base.OnShowFileChooser(webView, filePathCallback, fileChooserParams);
            }

            public override void OnPermissionRequest(PermissionRequest request)
            {
                if (_inner != null)
                    _inner.OnPermissionRequest(request);
                else
                    base.OnPermissionRequest(request);
            }

            public override void OnGeolocationPermissionsShowPrompt(
                string origin,
                GeolocationPermissions.ICallback callback)
            {
                if (_inner != null)
                    _inner.OnGeolocationPermissionsShowPrompt(origin, callback);
                else
                    base.OnGeolocationPermissionsShowPrompt(origin, callback);
            }

            public override bool OnJsAlert(AndroidWebView view, string url, string message, JsResult result)
            {
                if (_inner != null)
                    return _inner.OnJsAlert(view, url, message, result);
                return base.OnJsAlert(view, url, message, result);
            }

            public override bool OnJsConfirm(AndroidWebView view, string url, string message, JsResult result)
            {
                if (_inner != null)
                    return _inner.OnJsConfirm(view, url, message, result);
                return base.OnJsConfirm(view, url, message, result);
            }

            public override bool OnJsPrompt(
                AndroidWebView view,
                string url,
                string message,
                string defaultValue,
                JsPromptResult result)
            {
                if (_inner != null)
                    return _inner.OnJsPrompt(view, url, message, defaultValue, result);
                return base.OnJsPrompt(view, url, message, defaultValue, result);
            }

            public override bool OnCreateWindow(
                AndroidWebView view,
                bool isDialog,
                bool isUserGesture,
                global::Android.OS.Message resultMsg)
            {
                if (_inner != null)
                    return _inner.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
                return base.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
            }

            public override void OnCloseWindow(AndroidWebView window)
            {
                if (_inner != null)
                    _inner.OnCloseWindow(window);
                else
                    base.OnCloseWindow(window);
            }
        }

        private sealed class AndroidLoggerProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName)
            {
                return new AndroidLogger(categoryName);
            }

            public void Dispose()
            {
            }
        }

        private sealed class AndroidLogger : ILogger
        {
            private readonly string _category;

            public AndroidLogger(string category)
            {
                _category = category ?? "aNote";
            }

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel) || formatter == null)
                    return;

                string message = formatter(state, exception);
                if (exception != null)
                    message = message + "\n" + exception;

                string shortCategory = _category;
                int lastDot = shortCategory.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < shortCategory.Length - 1)
                    shortCategory = shortCategory.Substring(lastDot + 1);

                global::Android.Util.LogPriority priority = global::Android.Util.LogPriority.Info;
                if (logLevel == LogLevel.Critical || logLevel == LogLevel.Error)
                    priority = global::Android.Util.LogPriority.Error;
                else if (logLevel == LogLevel.Warning)
                    priority = global::Android.Util.LogPriority.Warn;

                global::Android.Util.Log.WriteLine(priority, DotNetTag, shortCategory + ": " + message);
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose()
                {
                }
            }
        }
    }
}
