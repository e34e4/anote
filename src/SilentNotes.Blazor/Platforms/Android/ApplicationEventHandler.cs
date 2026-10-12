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
            var synchronizationService = Ioc.Instance.GetService<ISynchronizationService>();
            synchronizationService.StopAutoSynchronization(Ioc.Instance);

            if (IsStartedByOAuthRedirectIndent(synchronizationService))
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
            return keyService.CloseAllSafes();
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
