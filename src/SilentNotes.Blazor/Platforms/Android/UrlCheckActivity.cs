// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Workers;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using ScrollView = Android.Widget.ScrollView;
using Uri = Android.Net.Uri;

namespace SilentNotes
{
    /// <summary>
    /// URLCheck-style browser interceptor activity. When aNote is set as the system default browser
    /// (or selected to open an http/https link), this overlay window appears on top of the current
    /// app, allowing the user to inspect/edit the URL, remove tracking parameters, unshorten
    /// redirects, copy/share the link, and open it in any installed web browser on the device.
    /// </summary>
    [Activity(
        Label = "aNote URLCheck",
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        Exported = true,
        ExcludeFromRecents = true,
        NoHistory = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "http", "https" })]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "http", "https" },
        DataMimeTypes = new[] { "text/html", "text/plain", "application/xhtml+xml" })]
    public class UrlCheckActivity : Activity
    {
        private EditText _urlInput;
        private TextView _domainStatusView;
        private TextView _virusTotalStatusView;
        private TextView _virusTotalBtn;
        private TextView _cleanBtn;
        private TextView _unshortenBtn;
        private TextView _httpsBtn;
        private LinearLayout _browsersContainer;
        private float _density;
        private bool _preferNewTab;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _density = Resources.DisplayMetrics.Density;
            _preferNewTab = Intent?.GetBooleanExtra(global::Android.Provider.Browser.ExtraCreateNewTab, false) ?? false;

            string initialUrl = ExtractUrlFromIntent(Intent);
            if (string.IsNullOrWhiteSpace(initialUrl))
            {
                Finish();
                return;
            }

            ConfigureDialogWindow();
            BuildOverlayUi(initialUrl);
        }

        private void ConfigureDialogWindow()
        {
            if (Window == null)
                return;

            Window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            Window.AddFlags(WindowManagerFlags.DimBehind);
            WindowManagerLayoutParams lp = Window.Attributes;
            if (lp != null)
            {
                lp.DimAmount = 0.6f;
                lp.Width = (int)(Resources.DisplayMetrics.WidthPixels * 0.94f);
                lp.Height = ViewGroup.LayoutParams.WrapContent;
                lp.Gravity = GravityFlags.Center;
                Window.Attributes = lp;
            }
        }

        private void BuildOverlayUi(string initialUrl)
        {
            int pad = (int)(14 * _density);

            var card = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            card.SetPadding(pad, pad, pad, pad);
            var cardBg = new GradientDrawable();
            cardBg.SetColor(Color.ParseColor("#121212"));
            cardBg.SetCornerRadius(16 * _density);
            cardBg.SetStroke((int)(1 * _density), Color.ParseColor("#333333"));
            card.Background = cardBg;

            // Header row: Title + Close button
            var headerRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            headerRow.SetGravity(GravityFlags.CenterVertical);

            var titleView = new TextView(this)
            {
                Text = "aNote URLCheck",
                TextSize = 16f,
            };
            titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            titleView.SetTextColor(Color.White);
            headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            TextView closeBtn = CreateActionChip("✕", "#222222", "#FFFFFF");
            closeBtn.Click += (s, e) => Finish();
            headerRow.AddView(closeBtn);
            card.AddView(headerRow);

            // Domain / Security summary line
            _domainStatusView = new TextView(this)
            {
                TextSize = 12.5f,
            };
            _domainStatusView.SetPadding(0, (int)(6 * _density), 0, (int)(2 * _density));
            card.AddView(_domainStatusView);

            _virusTotalStatusView = new TextView(this)
            {
                TextSize = 11.5f,
                Visibility = ViewStates.Gone,
            };
            _virusTotalStatusView.SetPadding(0, 0, 0, (int)(6 * _density));
            card.AddView(_virusTotalStatusView);

            // Editable URL field
            _urlInput = new EditText(this)
            {
                Text = initialUrl,
                TextSize = 13.5f,
                InputType = InputTypes.ClassText | InputTypes.TextVariationUri | InputTypes.TextFlagMultiLine,
            };
            _urlInput.SetMinLines(2);
            _urlInput.SetMaxLines(4);
            _urlInput.SetTextColor(Color.White);
            _urlInput.SetHintTextColor(Color.ParseColor("#777777"));
            int inputPad = (int)(10 * _density);
            _urlInput.SetPadding(inputPad, inputPad, inputPad, inputPad);
            var inputBg = new GradientDrawable();
            inputBg.SetColor(Color.ParseColor("#050505"));
            inputBg.SetCornerRadius(10 * _density);
            inputBg.SetStroke((int)(1 * _density), Color.ParseColor("#2E2E2E"));
            _urlInput.Background = inputBg;
            _urlInput.TextChanged += (s, e) => RefreshUrlAnalysis();
            card.AddView(_urlInput, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            // URLCheck Tools Row 1: Clean Trackers | Unshorten | HTTPS
            var toolsRow1 = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            toolsRow1.SetPadding(0, (int)(10 * _density), 0, (int)(6 * _density));

            _cleanBtn = CreateActionChip("🧹 Clean", "#1E293B", "#38BDF8");
            _cleanBtn.Click += (s, e) => OnCleanClicked();
            toolsRow1.AddView(_cleanBtn, CreateWeightedChipParams());

            _unshortenBtn = CreateActionChip("🔗 Unshorten", "#1E293B", "#A78BFA");
            _unshortenBtn.Click += async (s, e) => await OnUnshortenClickedAsync();
            toolsRow1.AddView(_unshortenBtn, CreateWeightedChipParams());

            _httpsBtn = CreateActionChip("🔒 HTTPS", "#1E293B", "#4ADE80");
            _httpsBtn.Click += (s, e) =>
            {
                _urlInput.Text = UrlCheckUtils.UpgradeToHttps(CurrentUrl);
            };
            toolsRow1.AddView(_httpsBtn, CreateWeightedChipParams());

            card.AddView(toolsRow1, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            // URLCheck Tools Row 2: Copy | Share | Save to Clip
            var toolsRow2 = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            toolsRow2.SetPadding(0, 0, 0, (int)(10 * _density));

            TextView copyBtn = CreateActionChip("📋 Copy", "#1A1A1A", "#E5E5E5");
            copyBtn.Click += (s, e) => CopyCurrentUrl();
            toolsRow2.AddView(copyBtn, CreateWeightedChipParams());

            TextView shareBtn = CreateActionChip("📤 Share", "#1A1A1A", "#E5E5E5");
            shareBtn.Click += (s, e) => ShareCurrentUrl();
            toolsRow2.AddView(shareBtn, CreateWeightedChipParams());

            TextView clipBtn = CreateActionChip("📌 +Clip", "#1A1A1A", "#E5E5E5");
            clipBtn.Click += (s, e) => SaveUrlToClipboardHistory();
            toolsRow2.AddView(clipBtn, CreateWeightedChipParams());

            card.AddView(toolsRow2, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            var toolsRow3 = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            toolsRow3.SetPadding(0, 0, 0, (int)(10 * _density));

            _virusTotalBtn = CreateActionChip("🦠 VirusTotal", "#1E293B", "#F472B6");
            _virusTotalBtn.Click += async (s, e) => await OnVirusTotalClickedAsync();
            toolsRow3.AddView(_virusTotalBtn, CreateWeightedChipParams());

            TextView ytDlpBtn = CreateActionChip("⬇ yt-dlp", "#1E293B", "#FBBF24");
            ytDlpBtn.Click += async (s, e) => await OnYtDlpDownloadClickedAsync();
            toolsRow3.AddView(ytDlpBtn, CreateWeightedChipParams());

            card.AddView(toolsRow3, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            // Section header: Open with installed browser
            var browsersHeader = new TextView(this)
            {
                Text = "OPEN IN INSTALLED BROWSER",
                TextSize = 11.5f,
            };
            browsersHeader.SetTextColor(Color.ParseColor("#9E9E9E"));
            browsersHeader.SetPadding(0, (int)(4 * _density), 0, (int)(6 * _density));
            card.AddView(browsersHeader);

            var scrollView = new ScrollView(this);
            _browsersContainer = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            scrollView.AddView(_browsersContainer);

            int maxListHeight = (int)(Resources.DisplayMetrics.HeightPixels * 0.42f);
            card.AddView(scrollView, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Height = ViewGroup.LayoutParams.WrapContent,
            });
            scrollView.SetMinimumHeight(0);
            if (maxListHeight > 0)
            {
                // Cap maximum height if many browsers are installed
            }

            SetContentView(card);

            RefreshUrlAnalysis();
            PopulateInstalledBrowsers();
            TryAutoVirusTotalScan();
        }

        private string CurrentUrl => (_urlInput?.Text ?? string.Empty).Trim();

        private void RefreshUrlAnalysis()
        {
            string url = CurrentUrl;
            string summary = UrlCheckUtils.BuildDomainSummary(url, out bool hasWarning);
            _domainStatusView.Text = summary;
            _domainStatusView.SetTextColor(hasWarning
                ? Color.ParseColor("#FBBF24")
                : Color.ParseColor("#4ADE80"));

            int trackers = UrlCheckUtils.CountTrackingParams(url);
            _cleanBtn.Text = trackers > 0 ? $"🧹 Clean ({trackers})" : "🧹 Clean";

            bool isHttp = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            _httpsBtn.Visibility = isHttp ? ViewStates.Visible : ViewStates.Gone;
        }

        private void OnCleanClicked()
        {
            string cleaned = UrlCheckUtils.CleanTrackingParams(CurrentUrl, out int removed);
            if (removed > 0)
            {
                _urlInput.Text = cleaned;
                Toast.MakeText(this, $"Removed {removed} tracking parameter(s)", ToastLength.Short)?.Show();
            }
            else
            {
                Toast.MakeText(this, "No tracking parameters found", ToastLength.Short)?.Show();
            }
        }

        private void TryAutoVirusTotalScan()
        {
            try
            {
                SettingsModel settings = TryGetServiceProvider()?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                if (settings == null || !settings.VirusTotalAutoScan || string.IsNullOrWhiteSpace(settings.VirusTotalApiKey))
                    return;

                _ = RunVirusTotalScanAsync(settings.VirusTotalApiKey.Trim());
            }
            catch (Exception ex)
            {
                AnoteFileLog.Error("UrlCheck", "Auto VirusTotal scan setup failed", ex);
            }
        }

        private async Task OnVirusTotalClickedAsync()
        {
            SettingsModel settings = TryGetServiceProvider()?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
            string apiKey = settings?.VirusTotalApiKey?.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                Toast.MakeText(this, "Set VirusTotal API key in aNote Settings", ToastLength.Long)?.Show();
                return;
            }

            await RunVirusTotalScanAsync(apiKey).ConfigureAwait(true);
        }

        private async Task OnYtDlpDownloadClickedAsync()
        {
            string url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                AnoteFileLog.Info("UrlCheck", "Starting yt-dlp from URL check");
                IServiceProvider sp = TryGetServiceProvider();
                IYtDlpDownloadService download = sp?.GetService<IYtDlpDownloadService>();
                if (download == null)
                {
                    AnoteFileLog.Error("UrlCheck", "yt-dlp service unavailable");
                    Toast.MakeText(this, "yt-dlp service unavailable", ToastLength.Short)?.Show();
                    return;
                }

                await Platforms.Services.YtDlpDownloadUi.PromptQualityAndDownloadAsync(this, download, url)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Error("YtDlp", "URLCheck yt-dlp failed", ex);
                Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show();
            }
        }

        private async Task RunVirusTotalScanAsync(string apiKey)
        {
            string url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (_virusTotalBtn != null)
                _virusTotalBtn.Enabled = false;
            ShowVirusTotalStatus("Scanning with VirusTotal…", Color.ParseColor("#94A3B8"));

            try
            {
                VirusTotalUrlCheckResult result = await Task.Run(() =>
                    VirusTotalUrlChecker.CheckUrlAsync(url, apiKey)).ConfigureAwait(true);

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    AnoteFileLog.Warn("UrlCheck", "VirusTotal: " + result.ErrorMessage);
                    ShowVirusTotalStatus("VirusTotal: " + result.ErrorMessage, Color.ParseColor("#F87171"));
                    Toast.MakeText(this, result.ErrorMessage, ToastLength.Long)?.Show();
                    return;
                }

                string line = result.Summary ?? "Scan complete";
                Color color = result.MaliciousCount > 0
                    ? Color.ParseColor("#F87171")
                    : result.SuspiciousCount > 0
                        ? Color.ParseColor("#FBBF24")
                        : Color.ParseColor("#4ADE80");
                ShowVirusTotalStatus("🦠 " + line, color);
            }
            finally
            {
                if (_virusTotalBtn != null)
                    _virusTotalBtn.Enabled = true;
            }
        }

        private void ShowVirusTotalStatus(string text, Color color)
        {
            if (_virusTotalStatusView == null)
                return;
            _virusTotalStatusView.Visibility = ViewStates.Visible;
            _virusTotalStatusView.Text = text;
            _virusTotalStatusView.SetTextColor(color);
        }

        private async Task OnUnshortenClickedAsync()
        {
            string before = CurrentUrl;
            if (string.IsNullOrEmpty(before))
                return;

            _unshortenBtn.Enabled = false;
            _unshortenBtn.Text = "⏳...";
            try
            {
                string resolved = await Task.Run(() => UrlCheckUtils.UnshortenUrlAsync(before));
                if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, before, StringComparison.Ordinal))
                {
                    _urlInput.Text = resolved;
                    Toast.MakeText(this, "Resolved redirect URL", ToastLength.Short)?.Show();
                }
                else
                {
                    Toast.MakeText(this, "No redirect found", ToastLength.Short)?.Show();
                }
            }
            finally
            {
                _unshortenBtn.Enabled = true;
                _unshortenBtn.Text = "🔗 Unshorten";
            }
        }

        private void CopyCurrentUrl()
        {
            try
            {
                var cm = GetSystemService(ClipboardService) as global::Android.Content.ClipboardManager;
                if (cm != null)
                {
                    cm.PrimaryClip = ClipData.NewPlainText("URL", CurrentUrl);
                    Toast.MakeText(this, "URL copied", ToastLength.Short)?.Show();
                }
            }
            catch (Exception)
            {
            }
        }

        private void ShareCurrentUrl()
        {
            try
            {
                var shareIntent = new Intent(Intent.ActionSend);
                shareIntent.SetType("text/plain");
                shareIntent.PutExtra(Intent.ExtraText, CurrentUrl);
                Intent chooser = Intent.CreateChooser(shareIntent, CurrentUrl);
                chooser.AddFlags(ActivityFlags.NewTask);
                StartActivity(chooser);
                Finish();
            }
            catch (Exception)
            {
            }
        }

        private void SaveUrlToClipboardHistory()
        {
            try
            {
                IServiceProvider sp = TryGetServiceProvider();
                var history = sp?.GetService<IClipboardHistoryService>();
                if (history != null && !string.IsNullOrWhiteSpace(CurrentUrl))
                {
                    int limit = sp.GetService<ISettingsService>()?.LoadSettingsOrDefault()?.ClipboardHistorySize ?? 500;
                    history.AddText(CurrentUrl, limit);
                    sp.GetService<IClipboardWebdavSyncService>();
                    sp.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
                    AnoteToast.Show(this, "Pasted from your clipboard", FeedbackSeverity.Unknown);
                }
            }
            catch (Exception)
            {
            }
        }

        private void PopulateInstalledBrowsers()
        {
            _browsersContainer.RemoveAllViews();
            List<BrowserTarget> browsers = QueryInstalledBrowsers(this);

            if (browsers.Count == 0)
            {
                var emptyText = new TextView(this)
                {
                    Text = "No other web browsers found on device.",
                    TextSize = 13f,
                };
                emptyText.SetTextColor(Color.ParseColor("#9E9E9E"));
                emptyText.SetPadding(0, (int)(12 * _density), 0, (int)(12 * _density));
                _browsersContainer.AddView(emptyText);
                return;
            }

            foreach (BrowserTarget browser in browsers)
            {
                _browsersContainer.AddView(CreateBrowserRow(browser));
            }
        }

        private global::Android.Views.View CreateBrowserRow(BrowserTarget browser)
        {
            int padH = (int)(10 * _density);
            int padV = (int)(8 * _density);

            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(padH, padV, padH, padV);

            var rowBg = new GradientDrawable();
            rowBg.SetColor(Color.ParseColor("#1B1B1B"));
            rowBg.SetCornerRadius(10 * _density);
            rowBg.SetStroke((int)(1 * _density), Color.ParseColor("#2B2B2B"));
            row.Background = rowBg;

            var rowParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowParams.SetMargins(0, 0, 0, (int)(6 * _density));
            row.LayoutParameters = rowParams;

            if (browser.Icon != null)
            {
                var iconView = new ImageView(this);
                iconView.SetImageDrawable(browser.Icon);
                int iconSize = (int)(36 * _density);
                var iconParams = new LinearLayout.LayoutParams(iconSize, iconSize);
                iconParams.SetMargins(0, 0, (int)(10 * _density), 0);
                row.AddView(iconView, iconParams);
            }

            var textCol = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            var nameView = new TextView(this)
            {
                Text = browser.Label,
                TextSize = 14.5f,
            };
            nameView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            nameView.SetTextColor(Color.White);
            textCol.AddView(nameView);

            var pkgView = new TextView(this)
            {
                Text = browser.PackageName,
                TextSize = 11f,
            };
            pkgView.SetTextColor(Color.ParseColor("#888888"));
            textCol.AddView(pkgView);

            row.AddView(textCol, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            TextView newTabBtn = CreateActionChip("+Tab", "#262626", "#38BDF8");
            newTabBtn.Click += (s, e) => LaunchInBrowser(browser, newTab: true);
            row.AddView(newTabBtn);

            row.Click += (s, e) => LaunchInBrowser(browser, newTab: _preferNewTab);
            return row;
        }

        private void LaunchInBrowser(BrowserTarget browser, bool newTab)
        {
            string url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Uri uri = Uri.Parse(url);
                var intent = new Intent(Intent.ActionView, uri);
                intent.AddCategory(Intent.CategoryBrowsable);
                intent.SetComponent(new ComponentName(browser.PackageName, browser.ActivityName));
                intent.AddFlags(ActivityFlags.NewTask);
                if (newTab)
                {
                    intent.PutExtra(global::Android.Provider.Browser.ExtraApplicationId, PackageName);
                    intent.PutExtra(global::Android.Provider.Browser.ExtraCreateNewTab, true);
                }
                StartActivity(intent);
                Finish();
            }
            catch (Exception ex)
            {
                try
                {
                    var fallback = new Intent(Intent.ActionView, Uri.Parse(url));
                    fallback.SetPackage(browser.PackageName);
                    fallback.AddFlags(ActivityFlags.NewTask);
                    StartActivity(fallback);
                    Finish();
                }
                catch (Exception)
                {
                    Toast.MakeText(this, "Could not launch " + browser.Label + ": " + ex.Message, ToastLength.Short)?.Show();
                }
            }
        }

        internal static List<BrowserTarget> QueryInstalledBrowsers(Context context)
        {
            var results = new List<BrowserTarget>();
            var seenActivities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                PackageManager pm = context.PackageManager;
                if (pm == null)
                    return PrependAbroBrowser(context, results);

                string selfPackage = context.PackageName;
                string abroActivityName = TryGetAbroActivityName(context);
                string[] probeUrls = new[] { "https://example.com", "http://example.com" };

                foreach (string probeUrl in probeUrls)
                {
                    var probeIntent = new Intent(Intent.ActionView, Uri.Parse(probeUrl));
                    probeIntent.AddCategory(Intent.CategoryBrowsable);

                    IList<ResolveInfo> resolvedList;
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                    {
                        resolvedList = pm.QueryIntentActivities(
                            probeIntent,
                            PackageManager.ResolveInfoFlags.Of((long)PackageInfoFlags.MatchAll));
                    }
                    else
                    {
                        #pragma warning disable CS0618
                        resolvedList = pm.QueryIntentActivities(probeIntent, PackageInfoFlags.MatchAll);
                        #pragma warning restore CS0618
                    }

                    if (resolvedList == null)
                        continue;

                    foreach (ResolveInfo info in resolvedList)
                    {
                        ActivityInfo act = info?.ActivityInfo;
                        if (act == null || string.IsNullOrEmpty(act.PackageName) || string.IsNullOrEmpty(act.Name))
                            continue;

                        if (string.Equals(act.PackageName, selfPackage, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!IsOwnPackageBrowserActivity(act.Name, abroActivityName))
                                continue;
                        }

                        string activityKey = act.PackageName + "/" + act.Name;
                        if (!seenActivities.Add(activityKey))
                            continue;

                        string label = info.LoadLabel(pm)?.ToString();
                        if (string.IsNullOrWhiteSpace(label))
                            label = act.PackageName;

                        if (IsOwnPackageBrowserActivity(act.Name, abroActivityName))
                            label = "Abro";

                        Drawable icon = null;
                        try
                        {
                            icon = info.LoadIcon(pm);
                        }
                        catch (Exception)
                        {
                        }

                        results.Add(new BrowserTarget
                        {
                            PackageName = act.PackageName,
                            ActivityName = act.Name,
                            Label = label,
                            Icon = icon,
                        });
                    }
                }
            }
            catch (Exception)
            {
            }

            results = results.OrderBy(b => b.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            return PrependAbroBrowser(context, results);
        }

        private static bool IsOwnPackageBrowserActivity(string activityClassName, string abroActivityName)
        {
            if (string.IsNullOrEmpty(activityClassName))
                return false;
            if (!string.IsNullOrEmpty(abroActivityName)
                && string.Equals(activityClassName, abroActivityName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return activityClassName.EndsWith(".AbroBrowserActivity", StringComparison.OrdinalIgnoreCase)
                || activityClassName.EndsWith("AbroBrowserActivity", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryGetAbroActivityName(Context context)
        {
            try
            {
                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(AbroBrowserActivity)));
                ActivityInfo info = context.PackageManager?.GetActivityInfo(component, 0);
                return info?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<BrowserTarget> PrependAbroBrowser(Context context, List<BrowserTarget> results)
        {
            results ??= new List<BrowserTarget>();
            BrowserTarget abro = TryCreateAbroBrowserTarget(context);
            if (abro == null)
                return results;

            results.RemoveAll(b =>
                string.Equals(b.ActivityName, abro.ActivityName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.Label, "Abro", StringComparison.OrdinalIgnoreCase));
            results.Insert(0, abro);
            return results;
        }

        private static BrowserTarget TryCreateAbroBrowserTarget(Context context)
        {
            try
            {
                PackageManager pm = context.PackageManager;
                if (pm == null)
                    return null;

                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(AbroBrowserActivity)));
                ActivityInfo act = pm.GetActivityInfo(component, 0);
                if (act == null)
                    return null;

                Drawable icon = null;
                try
                {
                    icon = act.LoadIcon(pm);
                }
                catch (Exception)
                {
                }

                return new BrowserTarget
                {
                    PackageName = act.PackageName,
                    ActivityName = act.Name,
                    Label = "Abro",
                    Icon = icon,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ExtractUrlFromIntent(Intent intent)
        {
            if (intent == null)
                return null;

            string dataUrl = intent.DataString;
            if (!string.IsNullOrWhiteSpace(dataUrl))
                return dataUrl.Trim();

            string extraText = intent.GetStringExtra(Intent.ExtraText);
            if (!string.IsNullOrWhiteSpace(extraText))
            {
                string found = BookyUrlUtils.FirstUrl(extraText);
                return !string.IsNullOrEmpty(found) ? found : extraText.Trim();
            }

            return null;
        }

        private TextView CreateActionChip(string text, string bgHex, string fgHex)
        {
            var tv = new TextView(this)
            {
                Text = text,
                TextSize = 12.5f,
                Gravity = GravityFlags.Center,
            };
            tv.SetTextColor(Color.ParseColor(fgHex));
            int hPad = (int)(10 * _density);
            int vPad = (int)(7 * _density);
            tv.SetPadding(hPad, vPad, hPad, vPad);

            var bg = new GradientDrawable();
            bg.SetColor(Color.ParseColor(bgHex));
            bg.SetCornerRadius(8 * _density);
            bg.SetStroke((int)(1 * _density), Color.ParseColor("#33FFFFFF"));
            tv.Background = bg;
            return tv;
        }

        private LinearLayout.LayoutParams CreateWeightedChipParams()
        {
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            lp.SetMargins((int)(3 * _density), 0, (int)(3 * _density), 0);
            return lp;
        }

        private IServiceProvider TryGetServiceProvider()
        {
            try
            {
                if (Ioc.Instance != null)
                {
                    var existing = Ioc.Instance.GetService<IClipboardHistoryService>();
                    if (existing != null)
                        return Ioc.Instance;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider sp = services.BuildServiceProvider();
                sp.GetService<IAppContextService>()?.InitializeWithContextOnly(this);
                return sp;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal class BrowserTarget
        {
            public string PackageName { get; set; }
            public string ActivityName { get; set; }
            public string Label { get; set; }
            public Drawable Icon { get; set; }
        }
    }
}
