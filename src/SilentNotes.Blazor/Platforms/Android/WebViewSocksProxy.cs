// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Applies process-wide WebView SOCKS override (AndroidX ProxyController) from aNote settings.
    /// Abro uses the same API — without this, download WebViews bypass the proxy while Abro works.
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

        public static void Apply(SettingsModel settings)
        {
            try
            {
                if (!AndroidX.WebKit.WebViewFeature.IsFeatureSupported(AndroidX.WebKit.WebViewFeature.ProxyOverride))
                {
                    AnoteFileLog.Append("ytdlp", "WebView ProxyOverride not supported on this device");
                    return;
                }

                var executor = new ProxyExecutor();
                var listener = new ProxyRunnable();

                if (settings != null && settings.Socks5Enabled && settings.HasSocks5Config)
                {
                    string rule = "socks://" + settings.Socks5Host.Trim() + ":"
                        + settings.Socks5Port.ToString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(settings.Socks5Username))
                    {
                        string user = Uri.EscapeDataString(settings.Socks5Username.Trim());
                        string pass = Uri.EscapeDataString(settings.Socks5Password ?? string.Empty);
                        rule = "socks://" + user + ":" + pass + "@" + settings.Socks5Host.Trim() + ":"
                            + settings.Socks5Port.ToString(CultureInfo.InvariantCulture);
                    }

                    AndroidX.WebKit.ProxyConfig config = new AndroidX.WebKit.ProxyConfig.Builder()
                        .AddProxyRule(rule)
                        .Build();
                    AndroidX.WebKit.ProxyController.Instance.SetProxyOverride(config, executor, listener);
                    AnoteFileLog.Append(
                        "ytdlp",
                        "WebView SOCKS override " + settings.Socks5Host + ":" + settings.Socks5Port);
                }
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "WebView SOCKS apply error: " + ex.Message);
            }
        }

        private sealed class ProxyExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
        {
            public void Execute(Java.Lang.IRunnable command) => command?.Run();
        }

        private sealed class ProxyRunnable : Java.Lang.Object, Java.Lang.IRunnable
        {
            public void Run()
            {
            }
        }
    }
}
