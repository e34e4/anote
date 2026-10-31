// Copyright © 2023 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Widget;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Stories.SynchronizationStory;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Handles application lifecycle events for the Android platform.
    /// Possible events are listed here: https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle
    /// </summary>
    internal class ApplicationEventHandler
    {
        private const string LogTag = "aNoteSharedText";
        private DateTime? _lastPauseTime; // The (UTC) time the application was sent to the background the last time.

        internal void OnCreate(Activity activity)
        {
            System.Diagnostics.Debug.WriteLine("*** ApplicationEventHandler.OnCreate() " + GetId(activity));

            bool isAndroid15Plus = System.OperatingSystem.IsAndroidVersionAtLeast(35);
            if (!isAndroid15Plus)
            {
                // Workaround: Android soft keyboard hides the lower part of the content,
                // see https://learn.microsoft.com/en-us/dotnet/maui/android/platform-specifics/soft-keyboard-input-mode
                // Seems that from Android 15 onwards this won't be required anymore, because it is handled
                // correctly by the 'SafeAreaEdges' attribute.
                Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
                    Microsoft.Maui.Controls.Application.Current.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
                    Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
            }

            // Inform services about the new main activity.
            Ioc.Instance.GetService<IAppContextService>().Initialize(activity);

            // Make sure the notification channels exist before any notification is shown.
            NotificationChannels.CreateAll(activity);

            // Check for text which was shared by another application.
            ConsumeSharedTextParameter(activity.Intent);

            // Check whether the app was started by a calendar intent of another application.
            ConsumeCalendarIntent(activity.Intent);

            // Check whether the app was started because an alarm is ringing.
            ConsumeClockIntent(activity.Intent);

            // Check whether the app was started by a phone/dial intent or incoming call.
            ConsumePhoneIntent(activity.Intent);

            // Check whether the app was started by an sms/smsto compose intent.
            ConsumeSmsIntent(activity.Intent);

            // Check whether the app was started from the floating button menu to open a specific tab.
            ConsumeTabIntent(activity.Intent);

            // Check whether the app was started to open/play an audio or video file.
            ConsumeMediaIntent(activity.Intent);

            // Check whether the app was started to open a document in the Reader tab.
            ConsumeDocumentIntent(activity.Intent);

            // Check whether the app was started by a Telegram deep link (t.me / tg://).
            ConsumeTelegramLinkIntent(activity.Intent);

            // Prevent notes from being visible in list of recent apps and screenshots
            ISettingsService settingsService = Ioc.Instance.GetService<ISettingsService>();
            SettingsModel settings = settingsService.LoadSettingsOrDefault();
            if (settings.PreventScreenshots)
                Ioc.Instance.GetService<IEnvironmentService>().Screenshots.PreventScreenshots = true;
        }

        /// <inheritdoc/>
        internal void OnNewIntent(Intent intent)
        {
            // After reading the parameters from the passed intent, we forget about it and keep
            // the old intent running. This is called if the app was closed with the back key and
            // was not completely closed.
            ConsumeSharedTextParameter(intent);
            ConsumeCalendarIntent(intent);
            ConsumeClockIntent(intent);
            ConsumePhoneIntent(intent);
            ConsumeSmsIntent(intent);
            ConsumeTabIntent(intent);
            ConsumeMediaIntent(intent);
            ConsumeDocumentIntent(intent);
            ConsumeTelegramLinkIntent(intent);
        }

        /// <summary>
        /// Checks whether the intent opens an audio/video file (VIEW or SEND), and remembers it in
        /// <see cref="MediaIntentRequest.Pending"/> so the Player tab can play it.
        /// </summary>
        private void ConsumeMediaIntent(Intent intent)
        {
            MediaIntentRequest request = ParseMediaIntent(intent);
            if (request == null)
                return;

            MediaIntentRequest.Pending = request;
            Ioc.Instance.GetService<IMessengerService>()?.Send(new MediaIntentMessage());
        }

        private static MediaIntentRequest ParseMediaIntent(Intent intent)
        {
            if (intent == null)
                return null;

            string action = intent.Action;
            string type = intent.Type ?? string.Empty;
            bool isAudioOrVideo =
                type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

            if (action == Intent.ActionView)
            {
                global::Android.Net.Uri data = intent.Data;
                if (data == null)
                    return null;

                // Ignore calendar/tel/Telegram intents which also use VIEW.
                string scheme = data.Scheme ?? string.Empty;
                if (string.Equals(scheme, "tel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "sms", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "smsto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "mms", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "mmsto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "tg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(data.Host, "com.android.calendar", StringComparison.OrdinalIgnoreCase) ||
                    SilentNotes.Workers.TelegramLinkParser.IsTelegramLink(data.ToString()))
                {
                    return null;
                }

                if (!isAudioOrVideo)
                {
                    // Fallback: detect by path extension when MIME is missing.
                    string path = data.Path ?? data.ToString();
                    var kind = Services.MediaPlayerService.DetectKind(path, type);
                    if (kind != MediaKind.Audio && kind != MediaKind.Video)
                        return null;
                }

                return new MediaIntentRequest
                {
                    UriOrPath = data.ToString(),
                    MimeType = string.IsNullOrEmpty(type) ? null : type,
                    DisplayName = data.LastPathSegment,
                };
            }

            if ((action == Intent.ActionSend) && isAudioOrVideo)
            {
                global::Android.Net.Uri stream = null;
                try
                {
                    stream = (global::Android.Net.Uri)intent.GetParcelableExtra(Intent.ExtraStream);
                }
                catch (Exception)
                {
                }
                if (stream == null)
                    return null;

                return new MediaIntentRequest
                {
                    UriOrPath = stream.ToString(),
                    MimeType = type,
                    DisplayName = stream.LastPathSegment,
                };
            }

            return null;
        }

        /// <summary>
        /// Checks whether the intent opens a document (VIEW or SEND), and remembers it in
        /// <see cref="DocumentIntentRequest.Pending"/> so the Reader tab can open it.
        /// </summary>
        private void ConsumeDocumentIntent(Intent intent)
        {
            // Prefer media intents when both could match.
            if (MediaIntentRequest.Pending != null)
                return;

            DocumentIntentRequest request = ParseDocumentIntent(intent);
            if (request == null)
                return;

            DocumentIntentRequest.Pending = request;
            Ioc.Instance.GetService<IMessengerService>()?.Send(new DocumentIntentMessage());
        }

        private static DocumentIntentRequest ParseDocumentIntent(Intent intent)
        {
            if (intent == null)
                return null;

            string action = intent.Action;
            string type = intent.Type ?? string.Empty;

            if (action == Intent.ActionView)
            {
                global::Android.Net.Uri data = intent.Data;
                if (data == null)
                    return null;

                string scheme = data.Scheme ?? string.Empty;
                if (string.Equals(scheme, "tel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "sms", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "smsto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "mms", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "mmsto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "tg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(data.Host, "com.android.calendar", StringComparison.OrdinalIgnoreCase) ||
                    SilentNotes.Workers.TelegramLinkParser.IsTelegramLink(data.ToString()))
                {
                    return null;
                }

                string path = data.Path ?? data.ToString();
                DocumentKind kind = DocumentReaderService.DetectKind(path, type);
                if (kind == DocumentKind.Other || kind == DocumentKind.Folder)
                {
                    // Also accept generic text/* MIME even if extension is unknown.
                    if (!type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                        !type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("epub", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("fictionbook", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("officedocument", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("msword", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("ms-excel", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("opendocument", StringComparison.OrdinalIgnoreCase) &&
                        !type.Contains("djvu", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }

                // Skip audio/video — those belong to the Player tab.
                if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                    type.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return new DocumentIntentRequest
                {
                    UriOrPath = data.ToString(),
                    MimeType = string.IsNullOrEmpty(type) ? null : type,
                    DisplayName = data.LastPathSegment,
                };
            }

            if (action == Intent.ActionSend)
            {
                bool looksLikeDocument =
                    type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("epub", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("fictionbook", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("officedocument", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("msword", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("ms-excel", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("opendocument", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("djvu", StringComparison.OrdinalIgnoreCase);

                if (!looksLikeDocument)
                    return null;

                global::Android.Net.Uri stream = null;
                try
                {
                    stream = (global::Android.Net.Uri)intent.GetParcelableExtra(Intent.ExtraStream);
                }
                catch (Exception)
                {
                }
                if (stream == null)
                    return null;

                return new DocumentIntentRequest
                {
                    UriOrPath = stream.ToString(),
                    MimeType = type,
                    DisplayName = stream.LastPathSegment,
                };
            }

            return null;
        }

        /// <summary>
        /// Checks whether the intent opens a Telegram deep link (https://t.me/…, tg://…), and
        /// remembers it in <see cref="TelegramLinkRequest.Pending"/> so the TG tab can open it.
        /// </summary>
        private void ConsumeTelegramLinkIntent(Intent intent)
        {
            string link = ParseTelegramLinkIntent(intent);
            if (string.IsNullOrWhiteSpace(link))
                return;

            TelegramLinkRequest.Pending = new TelegramLinkRequest { Link = link };
            Ioc.Instance.GetService<IMessengerService>()?.Send(new TelegramLinkMessage());
        }

        private static string ParseTelegramLinkIntent(Intent intent)
        {
            if (intent == null || intent.Action != Intent.ActionView)
                return null;

            global::Android.Net.Uri data = intent.Data;
            if (data == null)
                return null;

            string raw = data.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (!SilentNotes.Workers.TelegramLinkParser.IsTelegramLink(raw))
                return null;

            return raw;
        }

        private void ConsumeTabIntent(Intent intent)
        {
            if (intent == null || !intent.HasExtra(FloatingButtonAccessibilityService.ExtraOpenTab))
                return;

            int tabIndex = intent.GetIntExtra(FloatingButtonAccessibilityService.ExtraOpenTab, -1);
            intent.RemoveExtra(FloatingButtonAccessibilityService.ExtraOpenTab);
            if (tabIndex < 0)
                return;

            TabIntentRequest.Pending = new TabIntentRequest { TabIndex = tabIndex };
            Ioc.Instance.GetService<IMessengerService>()?.Send(new TabIntentMessage());
        }

        private void ConsumePhoneIntent(Intent intent)
        {
            if (intent == null)
                return;

            bool openPhone = intent.GetBooleanExtra(PhoneInCallService.ExtraOpenPhone, false);
            string action = intent.Action;
            global::Android.Net.Uri data = intent.Data;

            if (openPhone)
            {
                intent.RemoveExtra(PhoneInCallService.ExtraOpenPhone);
                PhoneIntentRequest.Pending = new PhoneIntentRequest();
                Ioc.Instance.GetService<IMessengerService>()?.Send(new PhoneIntentMessage());
                return;
            }

            if ((action == Intent.ActionDial) ||
                ((action == Intent.ActionView) && string.Equals(data?.Scheme, "tel", StringComparison.OrdinalIgnoreCase)))
            {
                string number = data?.SchemeSpecificPart;
                PhoneIntentRequest.Pending = new PhoneIntentRequest { PhoneNumber = number };
                Ioc.Instance.GetService<IMessengerService>()?.Send(new PhoneIntentMessage());
            }
        }

        /// <summary>
        /// Checks whether the intent opens an SMS composer (SENDTO/VIEW with sms/smsto/mms/mmsto),
        /// and remembers it in <see cref="SmsIntentRequest.Pending"/> so the SMS tab can compose.
        /// </summary>
        private void ConsumeSmsIntent(Intent intent)
        {
            SmsIntentRequest request = ParseSmsIntent(intent);
            if (request == null)
                return;

            SmsIntentRequest.Pending = request;
            Ioc.Instance.GetService<IMessengerService>()?.Send(new SmsIntentMessage());
        }

        private static SmsIntentRequest ParseSmsIntent(Intent intent)
        {
            if (intent == null)
                return null;

            string action = intent.Action;
            global::Android.Net.Uri data = intent.Data;
            string scheme = data?.Scheme ?? string.Empty;
            bool isSmsScheme =
                string.Equals(scheme, "sms", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scheme, "smsto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scheme, "mms", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scheme, "mmsto", StringComparison.OrdinalIgnoreCase);

            if (!isSmsScheme)
                return null;

            if ((action != Intent.ActionSendto) && (action != Intent.ActionView))
                return null;

            string number = data.SchemeSpecificPart ?? string.Empty;
            int queryIndex = number.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex >= 0)
                number = number.Substring(0, queryIndex);
            number = global::Android.Net.Uri.Decode(number)?.Trim() ?? string.Empty;

            string body = GetStringExtraSafe(intent, "sms_body");
            if (string.IsNullOrEmpty(body))
                body = GetStringExtraSafe(intent, Intent.ExtraText);
            if (string.IsNullOrEmpty(body))
            {
                try
                {
                    body = data.GetQueryParameter("body");
                }
                catch (Exception)
                {
                    body = null;
                }
            }

            return new SmsIntentRequest
            {
                PhoneNumber = string.IsNullOrEmpty(number) ? null : number,
                Body = string.IsNullOrEmpty(body) ? null : body,
            };
        }

        /// <summary>
        /// Checks whether the intent was created by a ringing alarm, and remembers the request in
        /// <see cref="ClockIntentRequest.Pending"/> so the clock tab can open its alarm section.
        /// </summary>
        /// <param name="intent">The active intent.</param>
        private void ConsumeClockIntent(Intent intent)
        {
            if (intent == null)
                return;

            if (!intent.GetBooleanExtra(AlarmRingingService.ExtraOpenClock, false))
                return;

            intent.RemoveExtra(AlarmRingingService.ExtraOpenClock);
            ClockIntentRequest.Pending = new ClockIntentRequest { Action = ClockIntentAction.OpenAlarm };
            Ioc.Instance.GetService<IMessengerService>()?.Send(new ClockIntentMessage());
        }

        /// <summary>
        /// Checks whether the intent was created by a calendar action of another application
        /// (e.g. "open the calendar at this day" or "add this event"), and remembers the request
        /// in <see cref="CalendarIntentRequest.Pending"/>, so the calendar tab can handle it.
        /// </summary>
        /// <param name="intent">The active intent.</param>
        private void ConsumeCalendarIntent(Intent intent)
        {
            CalendarIntentRequest request = ParseCalendarIntent(intent);
            if (request == null)
                return;

            CalendarIntentRequest.Pending = request;
            Ioc.Instance.GetService<IMessengerService>()?.Send(new CalendarIntentMessage());
        }

        private static CalendarIntentRequest ParseCalendarIntent(Intent intent)
        {
            if (intent == null)
                return null;

            string action = intent.Action;
            global::Android.Net.Uri data = intent.Data;
            string path = data?.Path ?? string.Empty;

            if ((action == Intent.ActionView) && (data != null))
            {
                if ((data.Host == "com.android.calendar") && path.StartsWith("/time"))
                {
                    long millis = ParseTrailingId(data);
                    DateTime date = millis > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(millis).ToLocalTime().Date
                        : DateTime.Today;
                    return new CalendarIntentRequest { Action = CalendarIntentAction.OpenDate, Date = date };
                }
                if (path.StartsWith("/events") || (intent.Type == "vnd.android.cursor.item/event"))
                {
                    return new CalendarIntentRequest { Action = CalendarIntentAction.OpenEvent, EventId = ParseTrailingId(data) };
                }
            }

            if (action == Intent.ActionEdit)
            {
                return new CalendarIntentRequest { Action = CalendarIntentAction.EditEvent, EventId = ParseTrailingId(data) };
            }

            if ((action == Intent.ActionInsert) || (action == "android.intent.action.INSERT_OR_EDIT"))
            {
                var request = new CalendarIntentRequest
                {
                    Action = CalendarIntentAction.NewEvent,
                    Title = GetStringExtraSafe(intent, "title"),
                    Description = GetStringExtraSafe(intent, "description"),
                    Location = GetStringExtraSafe(intent, "eventLocation"),
                    AllDay = GetBooleanExtraSafe(intent, "allDay"),
                };

                long begin = intent.GetLongExtra("beginTime", 0);
                long end = intent.GetLongExtra("endTime", 0);
                if (begin > 0)
                {
                    request.BeginUtc = DateTimeOffset.FromUnixTimeMilliseconds(begin).UtcDateTime;
                    request.Date = DateTimeOffset.FromUnixTimeMilliseconds(begin).ToLocalTime().Date;
                }
                if (end > 0)
                    request.EndUtc = DateTimeOffset.FromUnixTimeMilliseconds(end).UtcDateTime;
                return request;
            }

            return null;
        }

        private static long ParseTrailingId(global::Android.Net.Uri uri)
        {
            if (uri == null)
                return 0;
            return long.TryParse(uri.LastPathSegment, out long id) ? id : 0;
        }

        private static string GetStringExtraSafe(Intent intent, string name)
        {
            try
            {
                return intent.GetStringExtra(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool GetBooleanExtraSafe(Intent intent, string name)
        {
            try
            {
                return intent.GetBooleanExtra(name, false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether the intent was created by an <see cref="ActionSendActivity"/> or a
        /// <see cref="ProcessTextActivity"/>, and stores the shared text in the clipboard history.
        /// </summary>
        /// <param name="intent">The active intent.</param>
        private void ConsumeSharedTextParameter(Intent intent)
        {
            string sharedText = intent?.GetStringExtra(ActionSendActivity.SharedTextParam);
            if (string.IsNullOrEmpty(sharedText))
                return;

            intent.RemoveExtra(ActionSendActivity.SharedTextParam);
            SaveSharedTextToClipboard(sharedText);
        }

        /// <summary>
        /// Stores a text which was shared by another application in the clipboard history.
        /// </summary>
        private void SaveSharedTextToClipboard(string text)
        {
            try
            {
                int limit = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault()?.ClipboardHistorySize ?? 500;

                // Writing the history file can take a moment, so do it in the background.
                Task.Run(() =>
                {
                    try
                    {
                        Ioc.Instance.GetService<IClipboardHistoryService>()?.AddText(text.Trim(), limit);
                        Ioc.Instance.GetService<IClipboardWebdavSyncService>();
                        Ioc.Instance.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error(LogTag, "Could not store the shared text: " + ex);
                    }
                });

                string message = Ioc.Instance.GetService<ILanguageService>()?["clipboard_gesture_saved"];
                Context context = Ioc.Instance.GetService<IAppContextService>()?.Context;
                Toast.MakeText(context, message ?? "Text saved to the clipboard history", ToastLength.Short)?.Show();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not store the shared text: " + ex);
            }
        }

        /// <summary>
        /// The OnStart() and OnNewIntent() methods have no guaranteed order, so we do all the
        /// work for starting up the app here, this is guaranteed to be called after them.
        /// </summary>
        internal void OnResume(Activity activity)
        {
            System.Diagnostics.Debug.WriteLine("*** ApplicationEventHandler.OnResume() " + GetId(activity));
            try
            {
                AccessibilityServiceWatchdog.EnsureEnabled(activity);
            }
            catch (Exception)
            {
            }

            var synchronizationService = Ioc.Instance.GetService<ISynchronizationService>();
            synchronizationService.StopAutoSynchronization(Ioc.Instance);

            if (SilentNotes.MailOAuthSession.Peek() != null)
            {
                Ioc.Instance.GetService<IMessengerService>()?.Send(
                    new SilentNotes.MailGoogleOAuthRedirectMessage(SilentNotes.MailOAuthSession.Peek()));
            }
            else if (IsStartedByOAuthRedirectIndent(synchronizationService))
            {
                // Coming back from an OAuth2 redirect.
                var startStep = new HandleOAuthRedirectStep();
                _ = startStep.RunStoryAndShowLastFeedback(synchronizationService.ManualSynchronization, Ioc.Instance, synchronizationService.ManualSynchronization.StoryMode);
            }
            else if (_lastPauseTime.HasValue)
            {
                // Close safes if timeout is reached
                bool safesClosed = CloseSafesWhenTimeoutReached();
                var messenger = Ioc.Instance.GetService<IMessengerService>();
                messenger.Send(new AfterResumeMessage { SafesClosed = safesClosed });
            }
        }

        private bool CloseSafesWhenTimeoutReached()
        {
            if (!_lastPauseTime.HasValue)
                return false;

            TimeSpan pausedFor = DateTime.UtcNow - _lastPauseTime.Value;
            if (pausedFor < TimeSpan.FromMinutes(3))
                return false;

            ISafeKeyService keyService = Ioc.Instance.GetService<ISafeKeyService>();
            bool closedSafes = keyService.CloseAllSafes();
            try
            {
                IPasswordVaultService vault = Ioc.Instance.GetService<IPasswordVaultService>();
                if (vault != null && vault.IsUnlocked)
                {
                    vault.Lock();
                    closedSafes = true;
                }
            }
            catch
            {
            }
            return closedSafes;
        }

        internal void OnPause(Activity activity)
        {
            System.Diagnostics.Debug.WriteLine("*** ApplicationEventHandler.OnPause() " + GetId(activity));
            _lastPauseTime = DateTime.UtcNow;
            var messenger = Ioc.Instance.GetService<IMessengerService>();
            messenger.Send(new StoreUnsavedDataMessage(MessageSender.ApplicationEventHandler));
        }

        internal void OnStop(Activity activity)
        {
            System.Diagnostics.Debug.WriteLine("*** ApplicationEventHandler.OnPause() " + GetId(activity));
            var synchronizationService = Ioc.Instance.GetService<ISynchronizationService>();
            synchronizationService.AutoSynchronizeAtShutdown(Ioc.Instance);
        }

        internal void OnDestroy(Activity activity)
        {
            System.Diagnostics.Debug.WriteLine("*** ApplicationEventHandler.OnDestroy() " + GetId(activity));
            var messenger = Ioc.Instance.GetService<IMessengerService>();
            messenger.Send(new ClosePageMessage());
            Ioc.Instance.GetService<IActivityResultAwaiter>().RedirectedOnDestroy();
        }

        internal void OnActivityResult(Activity activity, int requestCode, Result resultCode, Intent data)
        {
            Ioc.Instance.GetService<IActivityResultAwaiter>().RedirectedOnActivityResult(requestCode, resultCode, data);
        }

        private bool IsStartedByOAuthRedirectIndent(ISynchronizationService synchronizationService)
        {
            return synchronizationService.ManualSynchronization?.OauthRedirectUrl != null;
        }

        public void OnMainLayoutReady()
        {
        }

        private static string GetId(Activity activity)
        {
            if (activity is MainActivity mainActivity)
                return mainActivity.Id.ToString();
            return null;
        }
    }
}
