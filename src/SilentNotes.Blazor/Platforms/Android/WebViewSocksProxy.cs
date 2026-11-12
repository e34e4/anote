// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Applies process-wide WebView SOCKS override (AndroidX ProxyController) from aNote settings.
    /// Abro uses the same API — without this, download WebViews bypass the proxy while Abro works.
    /// ProxyConfig rejects credentials in the URL ("Invalid Proxy URL"); authenticated SOCKS is
    /// exposed via a loopback <see cref="LocalSocks5Forwarder"/>.
    /// </summary>
    internal static class WebViewSocksProxy
    {
        public static void ApplyFromSettings(ISettingsService settingsService)
        {
            try
            {
                SettingsModel settings = settingsService?.LoadSettingsOrDefault();
                Apply(settings);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "WebView SOCKS apply failed: " + ex.Message);
            }
        }

        /// <summary>Applies proxy and waits briefly for WebView to adopt it before page loads.</summary>
        public static Task ApplyFromSettingsAsync(ISettingsService settingsService)
        {
            try
            {
                SettingsModel settings = settingsService?.LoadSettingsOrDefault();
                return ApplyAsync(settings);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "WebView SOCKS apply failed: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public static void Apply(SettingsModel settings)
        {
            _ = ApplyAsync(settings);
        }

        public static Task ApplyAsync(SettingsModel settings)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                if (!AndroidX.WebKit.WebViewFeature.IsFeatureSupported(AndroidX.WebKit.WebViewFeature.ProxyOverride))
                {
                    AnoteFileLog.Append("ytdlp", "WebView ProxyOverride not supported on this device");
                    tcs.TrySetResult(false);
                    return tcs.Task;
                }

                var executor = new ProxyExecutor();
                var listener = new ProxyRunnable(() => tcs.TrySetResult(true));

                if (settings != null && settings.Socks5Enabled && settings.HasSocks5Config)
                {
                    string ruleHost = settings.Socks5Host.Trim();
                    int rulePort = settings.Socks5Port;

                    if (!string.IsNullOrWhiteSpace(settings.Socks5Username))
                    {
                        int localPort = LocalSocks5Forwarder.EnsureRunning(
                            ruleHost,
                            rulePort,
                            settings.Socks5Username,
                            settings.Socks5Password);
                        if (localPort <= 0)
                        {
                            AnoteFileLog.Append(
                                "ytdlp",
                                "WebView SOCKS: auth proxy needs local forwarder; apply aborted");
                            tcs.TrySetResult(false);
                            return tcs.Task;
                        }

                        ruleHost = "127.0.0.1";
                        rulePort = localPort;
                    }

                    // ProxyConfig forbids userinfo in the URL — host:port only.
                    string rule = "socks://" + ruleHost + ":"
                        + rulePort.ToString(CultureInfo.InvariantCulture);

                    AndroidX.WebKit.ProxyConfig config = new AndroidX.WebKit.ProxyConfig.Builder()
                        .AddProxyRule(rule)
                        .Build();
                    AndroidX.WebKit.ProxyController.Instance.SetProxyOverride(config, executor, listener);
                    AnoteFileLog.Append(
                        "ytdlp",
                        "WebView SOCKS override " + rule
                        + (ruleHost == "127.0.0.1"
                            ? " (via local forwarder → " + settings.Socks5Host + ":" + settings.Socks5Port + ")"
                            : string.Empty));
                }
                else
                {
                    LocalSocks5Forwarder.Stop();
                    AndroidX.WebKit.ProxyController.Instance.ClearProxyOverride(executor, listener);
                    AnoteFileLog.Append("ytdlp", "WebView SOCKS override cleared");
                }
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "WebView SOCKS apply error: " + ex.Message);
                tcs.TrySetResult(false);
            }

            return WaitWithTimeout(tcs.Task, 2500);
        }

        private static async Task WaitWithTimeout(Task task, int timeoutMs)
        {
            Task finished = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (finished != task)
                AnoteFileLog.Append("ytdlp", "WebView SOCKS apply timed out waiting for listener");
        }

        private sealed class ProxyExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
        {
            public void Execute(Java.Lang.IRunnable command) => command?.Run();
        }

        private sealed class ProxyRunnable : Java.Lang.Object, Java.Lang.IRunnable
        {
            private readonly Action _onRun;

            public ProxyRunnable(Action onRun) => _onRun = onRun;

            public void Run() => _onRun?.Invoke();
        }
    }
}
