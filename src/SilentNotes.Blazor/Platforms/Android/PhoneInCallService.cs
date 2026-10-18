// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Telecom;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Android InCallService required for aNote to be eligible as the system default phone (dialer) app,
    /// and to manage incoming and ongoing phone calls.
    /// </summary>
    [Service(Exported = true, Permission = "android.permission.BIND_INCALL_SERVICE")]
    [MetaData("android.telecom.IN_CALL_SERVICE_UI", Value = "true")]
    [MetaData("android.telecom.IN_CALL_SERVICE_RINGING", Value = "true")]
    [IntentFilter(new[] { "android.telecom.InCallService" })]
    public class PhoneInCallService : InCallService
    {
        public const string ActionAnswerCall = "app.anote.action.ANSWER_CALL";
        public const string ActionHangupCall = "app.anote.action.HANGUP_CALL";
        public const string ExtraOpenPhone = "anote_open_phone";

        private static PhoneInCallService _instance;
        private static Call _currentCall;
        private static DateTime? _connectedAtUtc;
        private static bool _isMuted;
        private static bool _isSpeakerOn;
        private static PhoneSpamEntryModel _currentCallSpam;
        private static string _lastBlockedBanner;

        private CallCallback _callback;

        /// <inheritdoc/>
        public override void OnCreate()
        {
            base.OnCreate();
            _instance = this;
            _callback = new CallCallback(this);
        }

        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string action = intent?.Action;
            if (action == ActionAnswerCall)
            {
                AnswerCurrentCall();
                return StartCommandResult.NotSticky;
            }
            if (action == ActionHangupCall)
            {
                HangupCurrentCall();
                return StartCommandResult.NotSticky;
            }
            return base.OnStartCommand(intent, flags, startId);
        }

        /// <inheritdoc/>
        public override void OnCallAdded(Call call)
        {
            base.OnCallAdded(call);
            _instance = this;
            CallState state = GetCallState(call);
            string number = ExtractNumber(call);

            // Check blocklist masks (+7903*, etc.) and spam auto-blocking on incoming ringing calls
            if (state == CallState.Ringing && !string.IsNullOrWhiteSpace(number))
            {
                try
                {
                    var spamService = Ioc.Instance.GetService<IPhoneSpamService>();
                    if (spamService != null)
                    {
                        bool isContact = IsNumberInRepositoryContacts(number);
                        IncomingCallDecision decision = spamService.EvaluateIncomingCall(number, isContact);
                        if (decision.ShouldBlock)
                        {
                            _lastBlockedBanner = $"{number}: {decision.BlockReason}";
                            call.Reject(false, null);
                            NotifyCallChanged();
                            return;
                        }
                        _currentCallSpam = decision.SpamEntry ?? spamService.LookupLocal(number);
                    }
                }
                catch (Exception)
                {
                }
            }
            else
            {
                _currentCallSpam = Ioc.Instance.GetService<IPhoneSpamService>()?.LookupLocal(number);
            }

            _currentCall = call;
            _isMuted = false;
            _isSpeakerOn = false;
            _connectedAtUtc = (state == CallState.Active) ? DateTime.UtcNow : null;

            try
            {
                call?.RegisterCallback(_callback);
            }
            catch (Exception)
            {
            }

            UpdateCallNotification();
            OpenAppOnPhoneTab();
            NotifyCallChanged();

            if (!string.IsNullOrWhiteSpace(number))
                _ = LookupCallerOnlineInBackgroundAsync(call, number);
        }

        /// <inheritdoc/>
        public override void OnCallRemoved(Call call)
        {
            try
            {
                call?.UnregisterCallback(_callback);
            }
            catch (Exception)
            {
            }

            if (ReferenceEquals(_currentCall, call))
            {
                _currentCall = null;
                _connectedAtUtc = null;
                _isMuted = false;
                _isSpeakerOn = false;
                _currentCallSpam = null;
            }

            AppNotifications.GetManager(this)?.Cancel(AppNotifications.CallNotificationId);
            NotifyCallChanged();
            base.OnCallRemoved(call);
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnDestroy();
        }

        internal static ActiveCallModel GetActiveCallSnapshot(Func<string, string> nameResolver)
        {
            Call call = _currentCall;
            if (call == null)
                return null;

            CallState state = GetCallState(call);
            if (state == CallState.Disconnected || state == CallState.Disconnecting)
                return null;

            string number = ExtractNumber(call);
            PhoneSpamEntryModel spam = _currentCallSpam ?? Ioc.Instance.GetService<IPhoneSpamService>()?.LookupLocal(number);
            return new ActiveCallModel
            {
                Number = number,
                DisplayName = nameResolver?.Invoke(number),
                State = FormatState(state),
                IsRinging = state == CallState.Ringing,
                IsActive = state == CallState.Active,
                IsMuted = _isMuted,
                IsSpeakerOn = _isSpeakerOn,
                ConnectedAtUtc = _connectedAtUtc,
                SpamSummary = spam?.SummaryText,
                IsSpam = spam?.IsSpam ?? false,
            };
        }

        internal static string LastBlockedBanner => _lastBlockedBanner;

        internal static void AnswerCurrentCall()
        {
            try
            {
                _currentCall?.Answer(VideoProfileState.AudioOnly);
            }
            catch (Exception)
            {
            }
        }

        internal static void HangupCurrentCall()
        {
            try
            {
                Call call = _currentCall;
                if (call == null)
                    return;

                if (GetCallState(call) == CallState.Ringing)
                    call.Reject(false, null);
                else
                    call.Disconnect();
            }
            catch (Exception)
            {
            }
        }

        internal static void ToggleMuteCurrentCall()
        {
            try
            {
                if (_instance == null)
                    return;
                _isMuted = !_isMuted;
                _instance.SetMuted(_isMuted);
                NotifyCallChanged();
            }
            catch (Exception)
            {
            }
        }

        internal static void ToggleSpeakerCurrentCall()
        {
            try
            {
                if (_instance == null)
                    return;
                _isSpeakerOn = !_isSpeakerOn;
#pragma warning disable CA1422
                _instance.SetAudioRoute(_isSpeakerOn ? CallAudioRoute.Speaker : CallAudioRoute.Earpiece);
#pragma warning restore CA1422
                NotifyCallChanged();
            }
            catch (Exception)
            {
            }
        }

        internal static void SendDtmfTone(char digit)
        {
            try
            {
                Call call = _currentCall;
                if (call == null)
                    return;
                call.PlayDtmfTone(digit);
                call.StopDtmfTone();
            }
            catch (Exception)
            {
            }
        }

        private void OnCallStateUpdated(Call call, CallState state)
        {
            if (state == CallState.Active && !_connectedAtUtc.HasValue)
                _connectedAtUtc = DateTime.UtcNow;

            if (state == CallState.Disconnected)
                AppNotifications.GetManager(this)?.Cancel(AppNotifications.CallNotificationId);
            else
                UpdateCallNotification();

            NotifyCallChanged();
        }

        private void UpdateCallNotification()
        {
            try
            {
                Call call = _currentCall;
                if (call == null)
                    return;

                NotificationChannels.CreateAll(this);
                CallState state = GetCallState(call);
                string number = ExtractNumber(call);
                string title = string.IsNullOrWhiteSpace(number) ? "Phone call" : number;

                PendingIntent openIntent = BuildOpenPhonePendingIntent();
                PendingIntent hangupIntent = BuildServicePendingIntent(ActionHangupCall, 7602);

                Notification.Builder builder = AppNotifications.CreateBuilder(this, NotificationChannels.CallChannelId);
                builder.SetContentTitle(title);
                string stateText = FormatState(state);
                if (_currentCallSpam != null && !string.IsNullOrWhiteSpace(_currentCallSpam.SummaryText))
                    stateText = $"{stateText} · {_currentCallSpam.SummaryText}";
                builder.SetContentText(stateText);
                builder.SetSmallIcon(AppNotifications.SmallIcon(this));
                builder.SetCategory(Notification.CategoryCall);
                builder.SetPriority((int)NotificationPriority.Max);
                builder.SetOngoing(true);
                builder.SetAutoCancel(false);
                builder.SetContentIntent(openIntent);

                var icon = Icon.CreateWithResource(this, AppNotifications.SmallIcon(this));
                if (state == CallState.Ringing)
                {
                    PendingIntent answerIntent = BuildServicePendingIntent(ActionAnswerCall, 7601);
                    builder.SetFullScreenIntent(openIntent, true);
                    builder.AddAction(new Notification.Action.Builder(icon, "Answer", answerIntent).Build());
                    builder.AddAction(new Notification.Action.Builder(icon, "Decline", hangupIntent).Build());
                }
                else
                {
                    builder.AddAction(new Notification.Action.Builder(icon, "Hang up", hangupIntent).Build());
                }

                AppNotifications.GetManager(this)?.Notify(AppNotifications.CallNotificationId, builder.Build());
            }
            catch (Exception)
            {
            }
        }

        private PendingIntent BuildOpenPhonePendingIntent()
        {
            var intent = new Intent(this, typeof(MainActivity));
            intent.SetAction(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            intent.PutExtra(ExtraOpenPhone, true);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            return PendingIntent.GetActivity(this, 7600, intent, flags);
        }

        private PendingIntent BuildServicePendingIntent(string action, int requestCode)
        {
            var intent = new Intent(this, typeof(PhoneInCallService));
            intent.SetAction(action);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;
            return PendingIntent.GetService(this, requestCode, intent, flags);
        }

        private void OpenAppOnPhoneTab()
        {
            try
            {
                PhoneIntentRequest.Pending = new PhoneIntentRequest();
                var intent = new Intent(this, typeof(MainActivity));
                intent.PutExtra(ExtraOpenPhone, true);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
                StartActivity(intent);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new PhoneIntentMessage());
            }
            catch (Exception)
            {
            }
        }

        private static void NotifyCallChanged()
        {
            try
            {
                Ioc.Instance.GetService<IMessengerService>()?.Send(new PhoneCallChangedMessage());
            }
            catch (Exception)
            {
            }
        }

        private async Task LookupCallerOnlineInBackgroundAsync(Call call, string number)
        {
            try
            {
                var spamService = Ioc.Instance.GetService<IPhoneSpamService>();
                if (spamService == null)
                    return;

                PhoneSpamEntryModel entry = await spamService.LookupOnlineAsync(number);
                if (entry == null || !ReferenceEquals(_currentCall, call))
                    return;

                _currentCallSpam = entry;
                if (GetCallState(call) == CallState.Ringing &&
                    spamService.AutoBlockSpam &&
                    entry.IsSpam &&
                    (!spamService.SkipContactsWhenBlocking || !IsNumberInRepositoryContacts(number)))
                {
                    _lastBlockedBanner = $"{number}: Spam ({entry.SummaryText})";
                    call.Reject(false, null);
                    NotifyCallChanged();
                    return;
                }

                UpdateCallNotification();
                NotifyCallChanged();
            }
            catch (Exception)
            {
            }
        }

        private static bool IsNumberInRepositoryContacts(string number)
        {
            try
            {
                var repoService = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repoService == null)
                    return false;
                repoService.LoadRepositoryOrDefault(out NoteRepositoryModel repo);
                if (repo?.Contacts == null)
                    return false;

                string norm = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
                if (norm.Length > 9)
                    norm = norm.Substring(norm.Length - 9);
                if (string.IsNullOrEmpty(norm))
                    return false;

                foreach (ContactModel c in repo.Contacts)
                {
                    foreach (string p in c.Phones)
                    {
                        string pn = new string((p ?? string.Empty).Where(char.IsDigit).ToArray());
                        if (pn.Length > 9)
                            pn = pn.Substring(pn.Length - 9);
                        if (!string.IsNullOrEmpty(pn) && pn == norm)
                            return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static CallState GetCallState(Call call)
        {
            if (call == null)
                return CallState.Disconnected;
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    return (CallState)(call.GetDetails()?.State ?? (int)CallState.New);
#pragma warning disable CA1422
                return call.State;
#pragma warning restore CA1422
            }
            catch (Exception)
            {
                return CallState.New;
            }
        }

        private static string ExtractNumber(Call call)
        {
            try
            {
                return call?.GetDetails()?.GetHandle()?.SchemeSpecificPart ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string FormatState(CallState state)
        {
            return state switch
            {
                CallState.Ringing => "Incoming call…",
                CallState.Dialing => "Dialing…",
                CallState.Connecting => "Connecting…",
                CallState.Active => "Connected",
                CallState.Holding => "On hold",
                CallState.Disconnecting => "Ending call…",
                _ => "Call",
            };
        }

        private class CallCallback : Call.Callback
        {
            private readonly PhoneInCallService _service;

            public CallCallback(PhoneInCallService service)
            {
                _service = service;
            }

            public override void OnStateChanged(Call call, CallState state)
            {
                base.OnStateChanged(call, state);
                _service.OnCallStateUpdated(call, state);
            }
        }
    }
}
