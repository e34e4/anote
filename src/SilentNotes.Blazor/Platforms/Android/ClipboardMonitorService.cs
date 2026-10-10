// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// A foreground service which monitors the system clipboard in the background and stores the
    /// copied content in the clipboard history.
    /// </summary>
    /// <remarks>
    /// Android does not allow regular applications to read the clipboard while they are in the
    /// background, the app must own the focused window. To work around this, the service shows a
    /// tiny, invisible, focusable overlay window for a moment, which gives it the focus and with it
    /// the permission to read the clipboard. The clipboard is checked on a timer, the interval can
    /// be configured in the settings.
    /// </remarks>
    [Service(
        Exported = false,
        ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
    [MetaData("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE", Value = "clipboard_monitoring")]
    public class ClipboardMonitorService : Service
    {
        /// <summary>Intent action to start (or refresh) the monitoring.</summary>
        public const string ActionStart = "app.anote.action.CLIPBOARD_MONITOR_START";

        /// <summary>Intent action to stop the monitoring.</summary>
        public const string ActionStop = "app.anote.action.CLIPBOARD_MONITOR_STOP";

        private const string NotificationChannelId = "anote_clipboard_monitoring";
        private const int NotificationId = 0x0A17;
        private const string LogTag = "aNoteClipboard";
        private const string SensitiveExtraKey = "android.content.extra.IS_SENSITIVE";
        private const int MaxFullImageDimension = 1200;
        private const int MaxThumbnailDimension = 200;
        private const int OverlayFocusDelayMs = 90;
        private const int DefaultIntervalMs = 2500;

        private Handler _handler;
        private IWindowManager _windowManager;
        private global::Android.Views.View _overlayView;
        private WindowManagerLayoutParams _overlayParams;
        private ClipboardManager _clipboardManager;
        private IServiceProvider _serviceProvider;
        private int _intervalMs = DefaultIntervalMs;
        private string _lastSignature;
        private bool _polling;

        /// <summary>
        /// Gets a value indicating whether the background monitor is currently running.
        /// </summary>
        public static bool IsRunning { get; private set; }

        /// <inheritdoc/>
        public override void OnCreate()
        {
            base.OnCreate();
            _handler = new Handler(Looper.MainLooper);
        }

        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if ((intent != null) && (intent.Action == ActionStop))
            {
                StopPolling();
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            StartForeground(NotificationId, BuildNotification());
            StartPolling();
            return StartCommandResult.Sticky;
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            StopPolling();
            base.OnDestroy();
        }

        /// <inheritdoc/>
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        private void StartPolling()
        {
            ReloadSettings();
            if (_polling)
                return;

            _polling = true;
            IsRunning = true;
            _clipboardManager = GetSystemService(ClipboardService) as ClipboardManager;
            EnsureOverlay();
            _handler.Post(PollOnce);
        }

        private void StopPolling()
        {
            IsRunning = false;
            _polling = false;
            HideOverlay();
            _handler?.RemoveCallbacksAndMessages(null);
            _serviceProvider = null;
        }

        private void ReloadSettings()
        {
            try
            {
                SettingsModel settings = GetServiceProvider()?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                int interval = (settings != null) ? settings.ClipboardPollIntervalMs : DefaultIntervalMs;
                _intervalMs = Math.Max(500, interval);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the settings: " + ex);
            }
        }

        private void PollOnce()
        {
            if (!_polling)
                return;

            try
            {
                if (!IsScreenOn())
                {
                    ScheduleNextPoll();
                    return;
                }

                // When our own app is focused, we are allowed to read the clipboard directly.
                if (IsOurAppFocused())
                {
                    TryReadClipboard();
                    ScheduleNextPoll();
                    return;
                }

                // Otherwise show the focusable overlay, read the clipboard and hide it again.
                ShowOverlay();
                _handler.PostDelayed(() =>
                {
                    TryReadClipboard();
                    HideOverlay();
                    ScheduleNextPoll();
                }, OverlayFocusDelayMs);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Polling the clipboard failed: " + ex);
                HideOverlay();
                ScheduleNextPoll();
            }
        }

        private void ScheduleNextPoll()
        {
            if (_polling)
                _handler.PostDelayed(PollOnce, _intervalMs);
        }

        private bool IsScreenOn()
        {
            var powerManager = GetSystemService(PowerService) as PowerManager;
            return (powerManager == null) || powerManager.IsInteractive;
        }

        private bool IsOurAppFocused()
        {
            try
            {
                Activity activity = GetServiceProvider()?.GetService<IAppContextService>()?.RootActivity;
                return (activity != null) && activity.HasWindowFocus;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryReadClipboard()
        {
            try
            {
                if ((_clipboardManager == null) || !_clipboardManager.HasPrimaryClip)
                    return false;

                ClipData clip = _clipboardManager.PrimaryClip;
                if ((clip == null) || (clip.ItemCount == 0))
                    return false;

                // Ignore clips which the source app marked as sensitive (e.g. passwords).
                if ((clip.Description?.Extras != null) && clip.Description.Extras.GetBoolean(SensitiveExtraKey, false))
                    return true;

                ClipData.Item item = clip.GetItemAt(0);
                if (item == null)
                    return true;

                string signature;
                if (item.Text != null)
                    signature = "t:" + item.Text;
                else if (item.Uri != null)
                    signature = "u:" + item.Uri;
                else
                    return true;

                if (signature == _lastSignature)
                    return true;
                _lastSignature = signature;

                if (item.Text != null)
                {
                    string text = item.Text;
                    int limit = GetHistorySizeLimit();
                    Task.Run(() =>
                    {
                        GetServiceProvider()?.GetService<IClipboardHistoryService>()?.AddText(text, limit);
                        NotifyClipboardChanged();
                    });
                }
                else
                {
                    StoreImage(item.Uri);
                }
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the clipboard: " + ex);
                return false;
            }
        }

        private void StoreImage(global::Android.Net.Uri uri)
        {
            Task.Run(() =>
            {
                try
                {
                    byte[] raw;
                    using (Stream stream = ContentResolver.OpenInputStream(uri))
                    {
                        if (stream == null)
                            return;
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            raw = memory.ToArray();
                        }
                    }

                    byte[] fullImage = null;
                    byte[] thumbnail = null;
                    using (Bitmap decoded = DecodeDownscaled(raw, MaxFullImageDimension))
                    {
                        if (decoded == null)
                            return;
                        fullImage = CompressPng(decoded);
                        using (Bitmap thumb = CreateScaledBitmap(decoded, MaxThumbnailDimension))
                            thumbnail = CompressPng(thumb);
                    }

                    GetServiceProvider()?.GetService<IClipboardHistoryService>()?.AddImage(fullImage, thumbnail, GetHistorySizeLimit());
                    NotifyClipboardChanged();
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "Could not store an image clip: " + ex);
                }
            });
        }

        private int GetHistorySizeLimit()
        {
            try
            {
                SettingsModel settings = GetServiceProvider()?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                return ((settings != null) && (settings.ClipboardHistorySize >= 0)) ? settings.ClipboardHistorySize : 500;
            }
            catch (Exception)
            {
                return 500;
            }
        }

        private void NotifyClipboardChanged()
        {
            try
            {
                GetServiceProvider()?.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not notify about a clipboard change: " + ex);
            }
        }

        private static Bitmap DecodeDownscaled(byte[] raw, int maxDimension)
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(raw, 0, raw.Length, bounds);
            if ((bounds.OutWidth <= 0) || (bounds.OutHeight <= 0))
                return null;

            int sampleSize = 1;
            int largest = Math.Max(bounds.OutWidth, bounds.OutHeight);
            while (largest / sampleSize > maxDimension)
                sampleSize *= 2;

            var options = new BitmapFactory.Options { InSampleSize = sampleSize };
            return BitmapFactory.DecodeByteArray(raw, 0, raw.Length, options);
        }

        private static Bitmap CreateScaledBitmap(Bitmap source, int maxDimension)
        {
            int width = source.Width;
            int height = source.Height;
            float scale = Math.Min(1f, (float)maxDimension / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)(width * scale));
            int targetHeight = Math.Max(1, (int)(height * scale));
            return Bitmap.CreateScaledBitmap(source, targetWidth, targetHeight, true);
        }

        private static byte[] CompressPng(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Compress(Bitmap.CompressFormat.Png, 100, memory);
                return memory.ToArray();
            }
        }

        #region Overlay window

        private void EnsureOverlay()
        {
            if (_overlayView != null)
                return;

            try
            {
                _windowManager = GetSystemService(WindowService)?.JavaCast<IWindowManager>();
                if (_windowManager == null)
                {
                    global::Android.Util.Log.Error(LogTag, "WindowManager is not available");
                    return;
                }

                _overlayView = new global::Android.Views.View(this);
                _overlayParams = new WindowManagerLayoutParams(
                    1,
                    1,
                    WindowManagerTypes.ApplicationOverlay,
                    WindowManagerFlags.NotTouchable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.Start,
                    X = 0,
                    Y = 0,
                    Alpha = 0.01f,
                };
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not create the overlay: " + ex);
                _overlayView = null;
            }
        }

        private void ShowOverlay()
        {
            try
            {
                if ((_overlayView != null) && (_overlayView.Parent == null) && (_windowManager != null))
                    _windowManager.AddView(_overlayView, _overlayParams);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show the overlay: " + ex);
            }
        }

        private void HideOverlay()
        {
            try
            {
                if ((_overlayView != null) && (_overlayView.Parent != null) && (_windowManager != null))
                    _windowManager.RemoveView(_overlayView);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not hide the overlay: " + ex);
            }
        }

        #endregion

        private Notification BuildNotification()
        {
            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    NotificationChannelId, "Clipboard", NotificationImportance.Low);
                notificationManager?.CreateNotificationChannel(channel);
            }

            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask);
            PendingIntent contentIntent = PendingIntent.GetActivity(
                this, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            Notification.Builder builder = (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                ? new Notification.Builder(this, NotificationChannelId)
                : new Notification.Builder(this);

            return builder
                .SetContentTitle("aNote")
                .SetContentText("Clipboard monitoring is active")
                .SetSmallIcon(Resource.Drawable.floating_button_pencil)
                .SetOngoing(true)
                .SetContentIntent(contentIntent)
                .Build();
        }

        private IServiceProvider GetServiceProvider()
        {
            if (_serviceProvider != null)
                return _serviceProvider;

            try
            {
                var services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider serviceProvider = services.BuildServiceProvider();
                serviceProvider.GetService<IAppContextService>().InitializeWithContextOnly(this);
                _serviceProvider = serviceProvider;
                return _serviceProvider;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not build the service provider: " + ex);
                return null;
            }
        }
    }
}
