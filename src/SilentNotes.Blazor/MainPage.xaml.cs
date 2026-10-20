using Microsoft.AspNetCore.Components.WebView;
using SilentNotes.Workers;

namespace SilentNotes;

public partial class MainPage : ContentPage
{
    public static string StartRoute = RouteNames.NoteRepository;
    private bool _autoReloadInProgress;

    public MainPage()
    {
        InitializeComponent();
        blazorWebView.StartPath = StartRoute;
        blazorWebView.BlazorWebViewInitializing += WebViewInitializingEventHandler;
        blazorWebView.BlazorWebViewInitialized += WebViewInitializedEventHandler;
#if ANDROID
        SilentNotes.Platforms.DiagnosticLogging.ReloadRequested = RequestBlazorAutoReload;
#endif
    }

#if ANDROID
    /// <summary>
    /// Recover from Blazor WebView render-batch desync after sleep/resume by triggering the
    /// same Reload action as the red error bar (or a hard WebView reload as fallback).
    /// </summary>
    private async void RequestBlazorAutoReload()
    {
        if (_autoReloadInProgress)
            return;
        _autoReloadInProgress = true;
        try
        {
            if (blazorWebView.Handler?.PlatformView is not global::Android.Webkit.WebView native)
                return;

            // Prefer Blazor's own reload link (restarts the circuit cleanly).
            const string script =
                "(function(){try{" +
                "var a=document.querySelector('#blazor-error-ui a.reload');" +
                "if(a){a.click();return 'clicked';}" +
                "location.reload();return 'reloaded';" +
                "}catch(e){return 'error:'+e;}})()";

            var tcs = new TaskCompletionSource<string>();
            native.EvaluateJavascript(script, new JavascriptResultCallback(tcs));
            string result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (string.IsNullOrEmpty(result) || result.StartsWith("\"error:", StringComparison.Ordinal))
                native.Reload();
        }
        catch
        {
            try
            {
                if (blazorWebView.Handler?.PlatformView is global::Android.Webkit.WebView native)
                    native.Reload();
            }
            catch
            {
            }
        }
        finally
        {
            // Allow another recovery after a short cooldown (JS side also rate-limits).
            await Task.Delay(2000);
            _autoReloadInProgress = false;
        }
    }

    private sealed class JavascriptResultCallback : Java.Lang.Object, global::Android.Webkit.IValueCallback
    {
        private readonly TaskCompletionSource<string> _tcs;

        public JavascriptResultCallback(TaskCompletionSource<string> tcs)
        {
            _tcs = tcs;
        }

        public void OnReceiveValue(Java.Lang.Object value)
        {
            _tcs.TrySetResult(value?.ToString());
        }
    }
#endif

    /// <summary>
    /// Tweak the WebView environment, before the WebView is initialized.
    /// </summary>
    private void WebViewInitializingEventHandler(object sender, BlazorWebViewInitializingEventArgs e)
    {
        blazorWebView.BlazorWebViewInitializing -= WebViewInitializingEventHandler;
#if (WINDOWS)
        // Workaround: On Windows we have to convince the WebView to use the user language (e.g. for auto correction).
        var arguments = new KeyValueList<string, string>();
        arguments["--lang"] = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault();
        arguments["--disable-features"] = "msSmartScreenProtection";
        string additionalBrowserArguments = string.Join(" ", arguments.Select(argument => argument.Key + "=" + argument.Value));

        if (e.EnvironmentOptions == null)
            e.EnvironmentOptions = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
        e.EnvironmentOptions.AdditionalBrowserArguments = additionalBrowserArguments;

        // Workaround: The e.EnvironmentOptions.AdditionalBrowserArguments set here seem to be
        // ignored, so we modify the environment directly.
        string oldArguments = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", oldArguments + " " + additionalBrowserArguments);
#endif
    }

    /// <summary>
    /// Tweak the WebView after it has been initialized.
    /// </summary>
    /// <remarks>
    /// e.WebView is the same as (blazorWebView.Handler.PlatformView as Microsoft.UI.Xaml.Controls.WebView2);
    /// </remarks>
    private void WebViewInitializedEventHandler(object sender, BlazorWebViewInitializedEventArgs e)
    {
        blazorWebView.BlazorWebViewInitialized -= WebViewInitializedEventHandler;
#if (WINDOWS)
        // Set the splash screen background color, shown from when the webview is displayed until
        // the the index.html is loaded.
        var backgroundColor = ColorExtensions.HexToColor("#4793d1");
        var uiBackgroundColor = Windows.UI.Color.FromArgb(backgroundColor.A, backgroundColor.R, backgroundColor.G, backgroundColor.B);
        e.WebView.DefaultBackgroundColor = uiBackgroundColor;
        e.WebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
#endif
#if (WINDOWS && !DEBUG)
        // Workaround: Prevent browser keys like page refresh, or ctrl-shift-i to open the developer view
        e.WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
#if (ANDROID)
        // Settings cannot be set, application will stop running
        //var webSettings = e.WebView.Settings;
        //webSettings.JavaScriptEnabled = true;
        //webSettings.BlockNetworkLoads = true; // only local content allowed
        //webSettings.AllowFileAccess = false; // no local files but from the asset directory
        //webSettings.CacheMode = Android.Webkit.CacheModes.NoCache; // is already local content
        //webSettings.JavaScriptCanOpenWindowsAutomatically = false; // same as default
        //webSettings.SetSupportMultipleWindows(false); // same as default
        //webSettings.TextZoom = 100; // Ignores system font size, so the app controls the font size
        //webSettings.SaveFormData = false;

        // Forward WebView console + Blazor errors to logcat (aNoteWebView / aNoteDiag).
        SilentNotes.Platforms.DiagnosticLogging.AttachWebViewConsoleLogging(e.WebView);
#endif
    }
}
