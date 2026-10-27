// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Services;
using AndroidEnv = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="ITelegramService"/>.
    /// Store under no-backup files; downloads/sessions under /sdcard/tg (external storage).
    /// </summary>
    internal class TelegramService : TelegramServiceBase
    {
        private static readonly string[] OfficialTelegramPackages =
        {
            "org.telegram.messenger",
            "org.telegram.messenger.web",
            "org.telegram.plus",
            "org.thunderdog.challegram",
        };

        private readonly IAppContextService _appContext;

        public TelegramService(
            IAppContextService appContextService,
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService)
            : base(xmlFileService, dataProtectionService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        protected override string GetStoreDirectory()
        {
            string baseDir = ContextCompat.GetNoBackupFilesDir(_appContext.Context).AbsolutePath;
            return Path.Combine(baseDir, "telegram");
        }

        /// <inheritdoc/>
        protected override string GetExternalTgRoot()
        {
            try
            {
                Java.IO.File dir = AndroidEnv.ExternalStorageDirectory;
                string root = dir?.AbsolutePath ?? "/storage/emulated/0";
                return Path.Combine(root, "tg");
            }
            catch (System.Exception)
            {
                return "/storage/emulated/0/tg";
            }
        }

        /// <inheritdoc/>
        protected override void OpenOfficialTelegramAppCore()
        {
            TryStartTelegramUri("tg://resolve?domain=telegram");
        }

        /// <inheritdoc/>
        protected override void OpenOfficialTelegramDevicesCore()
        {
            // Prefer Devices / Link Desktop Device so the user can scan our QR.
            if (TryStartTelegramUri("tg://settings/devices"))
                return;
            if (TryStartTelegramUri("tg://settings"))
                return;
            OpenOfficialTelegramAppCore();
        }

        private bool TryStartTelegramUri(string uri)
        {
            try
            {
                Context context = _appContext.RootActivity ?? _appContext.Context;
                if (context == null)
                    return false;

                PackageManager pm = context.PackageManager;
                foreach (string packageName in OfficialTelegramPackages)
                {
                    try
                    {
                        var view = new Intent(Intent.ActionView, AndroidUri.Parse(uri));
                        view.SetPackage(packageName);
                        view.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                        if (view.ResolveActivity(pm) != null)
                        {
                            context.StartActivity(view);
                            return true;
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                }

                foreach (string packageName in OfficialTelegramPackages)
                {
                    Intent launch = pm?.GetLaunchIntentForPackage(packageName);
                    if (launch == null)
                        continue;

                    launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                    context.StartActivity(launch);
                    return true;
                }

                var fallback = new Intent(Intent.ActionView, AndroidUri.Parse(uri));
                fallback.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(fallback);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        protected override Task<bool> PresentBotWebAppAsync(
            BotWebAppHostRequest request,
            CancellationToken cancellationToken)
        {
            return MainThread.InvokeOnMainThreadAsync(() => TelegramWebAppPage.Present(request));
        }

        /// <inheritdoc/>
        protected override void ShowIncomingMessageNotification(string title, string body, string chatKey, bool silent)
        {
            try
            {
                var context = _appContext.Context;
                if (context == null)
                    return;

                NotificationChannels.CreateAll(context);
                int notificationId = AppNotifications.TelegramNotificationIdForChat(chatKey);
                Notification.Builder builder = AppNotifications.CreateBuilder(context, NotificationChannels.TelegramChannelId);
                builder.SetContentTitle(string.IsNullOrEmpty(title) ? "Telegram" : title);
                builder.SetContentText(body ?? string.Empty);
                builder.SetStyle(new Notification.BigTextStyle().BigText(body ?? string.Empty));
                builder.SetSmallIcon(AppNotifications.SmallIcon(context));
                builder.SetAutoCancel(true);
                builder.SetPriority((int)NotificationPriority.High);
                builder.SetCategory(Notification.CategoryMessage);
                if (silent)
                {
                    builder.SetSound(null);
                    builder.SetVibrate(Array.Empty<long>());
                }
                builder.SetContentIntent(AppNotifications.BuildOpenAppIntent(context, notificationId));
                AppNotifications.GetManager(context)?.Notify(notificationId, builder.Build());
            }
            catch (System.Exception)
            {
            }
        }
    }
}
