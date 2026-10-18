// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Telecom;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;
using CallDirection = SilentNotes.Models.CallDirection;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IDevicePhoneService"/> for reading/managing the system
    /// call log, placing phone calls, controlling active calls, and requesting the default dialer role.
    /// </summary>
    internal class DevicePhoneService : IDevicePhoneService
    {
        private const int MaxCallLogEntries = 200;

        private readonly IAppContextService _appContext;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        public DevicePhoneService(
            IAppContextService appContextService,
            IRepositoryStorageService repositoryStorageService,
            IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContextService;
            _repositoryService = repositoryStorageService;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasPermissions
        {
            get
            {
                return HasPermission(global::Android.Manifest.Permission.ReadCallLog)
                    && HasPermission(global::Android.Manifest.Permission.CallPhone);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestPermissionsAsync()
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Permissions.RequestAsync<Permissions.Phone>();
                });

                if (!HasPermission(global::Android.Manifest.Permission.ReadCallLog) ||
                    !HasPermission(global::Android.Manifest.Permission.WriteCallLog))
                {
                    Activity activity = _appContext.RootActivity;
                    if (activity != null && Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        var perms = new List<string>
                        {
                            global::Android.Manifest.Permission.ReadCallLog,
                            global::Android.Manifest.Permission.WriteCallLog,
                            global::Android.Manifest.Permission.CallPhone,
                            global::Android.Manifest.Permission.ReadPhoneState,
                        };
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                            perms.Add(global::Android.Manifest.Permission.AnswerPhoneCalls);

                        activity.RequestPermissions(perms.ToArray(), 7701);
                        await Task.Delay(600);
                    }
                }
                return HasPermissions;
            }
            catch (Exception)
            {
                return HasPermissions;
            }
        }

        /// <inheritdoc/>
        public bool IsDefaultDialerApp
        {
            get
            {
                try
                {
                    Context context = _appContext.Context;
                    if (context == null)
                        return false;

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                        if (roleManager != null && roleManager.IsRoleHeld(global::Android.App.Roles.RoleManager.RoleDialer))
                            return true;
                    }

                    var telecom = (TelecomManager)context.GetSystemService(Context.TelecomService);
                    return string.Equals(telecom?.DefaultDialerPackage, context.PackageName, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestDefaultDialerAppAsync()
        {
            try
            {
                Context context = _appContext.Context;
                Activity activity = _appContext.RootActivity;
                Intent intent = null;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                    if (roleManager != null && roleManager.IsRoleAvailable(global::Android.App.Roles.RoleManager.RoleDialer))
                        intent = roleManager.CreateRequestRoleIntent(global::Android.App.Roles.RoleManager.RoleDialer);
                }

                if (intent == null)
                {
                    intent = new Intent(TelecomManager.ActionChangeDefaultDialer);
                    intent.PutExtra(TelecomManager.ExtraChangeDefaultDialerPackageName, context.PackageName);
                }

                if (activity != null && _activityResultAwaiter != null)
                {
                    await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
                }
                else
                {
                    intent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent);
                }
            }
            catch (Exception)
            {
            }
            return IsDefaultDialerApp;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<CallLogEntryModel>> GetCallLogAsync()
        {
            return Task.Run<IReadOnlyList<CallLogEntryModel>>(() =>
            {
                var result = new List<CallLogEntryModel>();
                if (!HasPermission(global::Android.Manifest.Permission.ReadCallLog))
                    return result;

                try
                {
                    Dictionary<string, string> contacts = LoadContactNames();
                    using ICursor cursor = _appContext.Context.ContentResolver.Query(
                        CallLog.Calls.ContentUri,
                        new[]
                        {
                            CallLog.Calls.InterfaceConsts.Id,
                            CallLog.Calls.Number,
                            CallLog.Calls.CachedName,
                            CallLog.Calls.Type,
                            CallLog.Calls.Date,
                            CallLog.Calls.Duration,
                        },
                        null,
                        null,
                        CallLog.Calls.Date + " DESC");

                    if (cursor == null)
                        return result;

                    int idIdx = cursor.GetColumnIndex(CallLog.Calls.InterfaceConsts.Id);
                    int numIdx = cursor.GetColumnIndex(CallLog.Calls.Number);
                    int nameIdx = cursor.GetColumnIndex(CallLog.Calls.CachedName);
                    int typeIdx = cursor.GetColumnIndex(CallLog.Calls.Type);
                    int dateIdx = cursor.GetColumnIndex(CallLog.Calls.Date);
                    int durIdx = cursor.GetColumnIndex(CallLog.Calls.Duration);

                    int count = 0;
                    while (cursor.MoveToNext() && count < MaxCallLogEntries)
                    {
                        string number = (numIdx >= 0) ? cursor.GetString(numIdx) : string.Empty;
                        string cachedName = (nameIdx >= 0) ? cursor.GetString(nameIdx) : null;
                        string resolvedName = ResolveName(contacts, number) ?? cachedName;
                        int rawType = (typeIdx >= 0) ? cursor.GetInt(typeIdx) : 1;
                        long dateMs = (dateIdx >= 0) ? cursor.GetLong(dateIdx) : 0;
                        long durationSec = (durIdx >= 0) ? cursor.GetLong(durIdx) : 0;

                        CallDirection direction = rawType switch
                        {
                            2 => CallDirection.Outgoing,
                            3 => CallDirection.Missed,
                            5 => CallDirection.Rejected,
                            _ => CallDirection.Incoming,
                        };

                        result.Add(new CallLogEntryModel
                        {
                            Id = (idIdx >= 0) ? cursor.GetLong(idIdx) : 0,
                            Number = number ?? string.Empty,
                            DisplayName = resolvedName,
                            Direction = direction,
                            DateUtc = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).UtcDateTime,
                            DurationSeconds = durationSec,
                        });
                        count++;
                    }
                }
                catch (Exception)
                {
                }
                return result;
            });
        }

        /// <inheritdoc/>
        public Task DeleteCallLogEntryAsync(long id)
        {
            return Task.Run(() =>
            {
                if (!HasPermission(global::Android.Manifest.Permission.WriteCallLog))
                    return;
                try
                {
                    _appContext.Context.ContentResolver.Delete(
                        CallLog.Calls.ContentUri,
                        CallLog.Calls.InterfaceConsts.Id + "=?",
                        new[] { id.ToString(CultureInfo.InvariantCulture) });
                }
                catch (Exception)
                {
                }
            });
        }

        /// <inheritdoc/>
        public Task ClearCallLogAsync()
        {
            return Task.Run(() =>
            {
                if (!HasPermission(global::Android.Manifest.Permission.WriteCallLog))
                    return;
                try
                {
                    _appContext.Context.ContentResolver.Delete(CallLog.Calls.ContentUri, null, null);
                }
                catch (Exception)
                {
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> PlaceCallAsync(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return false;

            string trimmed = number.Trim();
            Context context = _appContext.Context;
            global::Android.Net.Uri uri = global::Android.Net.Uri.FromParts("tel", trimmed, null);

            if (!HasPermission(global::Android.Manifest.Permission.CallPhone))
                await RequestPermissionsAsync();

            try
            {
                if (HasPermission(global::Android.Manifest.Permission.CallPhone))
                {
                    var telecom = (TelecomManager)context.GetSystemService(Context.TelecomService);
                    if (telecom != null && Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        telecom.PlaceCall(uri, null);
                        return true;
                    }

                    var callIntent = new Intent(Intent.ActionCall, uri);
                    callIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(callIntent);
                    return true;
                }
                else
                {
                    var dialIntent = new Intent(Intent.ActionDial, uri);
                    dialIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(dialIntent);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public ActiveCallModel GetActiveCall()
        {
            Dictionary<string, string> contacts = LoadContactNames();
            return PhoneInCallService.GetActiveCallSnapshot(num => ResolveName(contacts, num));
        }

        /// <inheritdoc/>
        public void AnswerCall() => PhoneInCallService.AnswerCurrentCall();

        /// <inheritdoc/>
        public void HangupCall() => PhoneInCallService.HangupCurrentCall();

        /// <inheritdoc/>
        public void ToggleMute() => PhoneInCallService.ToggleMuteCurrentCall();

        /// <inheritdoc/>
        public void ToggleSpeaker() => PhoneInCallService.ToggleSpeakerCurrentCall();

        /// <inheritdoc/>
        public void SendDtmf(char digit) => PhoneInCallService.SendDtmfTone(digit);

        private bool HasPermission(string permission)
        {
            Context context = _appContext.Context;
            return (context != null)
                && (ContextCompat.CheckSelfPermission(context, permission) == Permission.Granted);
        }

        private Dictionary<string, string> LoadContactNames()
        {
            var result = new Dictionary<string, string>();
            try
            {
                _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                if (repository != null)
                {
                    foreach (ContactModel contact in repository.Contacts)
                    {
                        foreach (string phone in contact.Phones)
                        {
                            string normalized = NormalizeNumber(phone);
                            if (!string.IsNullOrEmpty(normalized) && !result.ContainsKey(normalized))
                                result[normalized] = contact.Name;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private static string ResolveName(Dictionary<string, string> contacts, string address)
        {
            if (contacts.Count == 0)
                return null;
            string normalized = NormalizeNumber(address);
            if (string.IsNullOrEmpty(normalized))
                return null;
            return contacts.TryGetValue(normalized, out string name) ? name : null;
        }

        private static string NormalizeNumber(string number)
        {
            if (string.IsNullOrEmpty(number))
                return null;
            string digits = new string(number.Where(char.IsDigit).ToArray());
            if (digits.Length > 9)
                digits = digits.Substring(digits.Length - 9);
            return string.IsNullOrEmpty(digits) ? null : digits;
        }
    }
}
