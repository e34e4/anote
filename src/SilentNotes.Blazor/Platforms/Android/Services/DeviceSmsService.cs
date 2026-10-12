// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Provider;
using Android.Telephony;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IDeviceSmsService"/> interface for the Android platform.
    /// It reads the system message store and sends messages with the SmsManager. Contact names are
    /// resolved from the contacts of aNote, so no additional contacts permission is required.
    /// </summary>
    internal class DeviceSmsService : IDeviceSmsService
    {
        private const string ColumnId = "_id";
        private const string ColumnThreadId = "thread_id";
        private const string ColumnAddress = "address";
        private const string ColumnBody = "body";
        private const string ColumnDate = "date";
        private const string ColumnType = "type";
        private const string ColumnRead = "read";

        private const int MessageTypeInbox = 1;
        private const int MessageTypeSent = 2;

        private const string ActionChangeDefault = "android.provider.Telephony.ACTION_CHANGE_DEFAULT";
        private const string ExtraDefaultPackage = "package";
        private const string SmsDefaultPackageSetting = "sms_default_application";

        private readonly IAppContextService _appContext;
        private readonly IRepositoryStorageService _repositoryService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceSmsService"/> class.
        /// </summary>
        public DeviceSmsService(IAppContextService appContextService, IRepositoryStorageService repositoryService)
        {
            _appContext = appContextService;
            _repositoryService = repositoryService;
        }

        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public bool HasReadPermission
        {
            get { return HasPermission(global::Android.Manifest.Permission.ReadSms); }
        }

        /// <inheritdoc/>
        public bool HasSendPermission
        {
            get { return HasPermission(global::Android.Manifest.Permission.SendSms); }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestPermissionsAsync()
        {
            try
            {
                PermissionStatus read = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Sms>());
                return read == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsDefaultSmsApp
        {
            get
            {
                try
                {
                    Context context = _appContext.Context;
                    string current = Settings.Secure.GetString(context.ContentResolver, SmsDefaultPackageSetting);
                    return string.Equals(current, context.PackageName, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public Task<bool> RequestDefaultSmsAppAsync()
        {
            try
            {
                var intent = new Intent(ActionChangeDefault);
                intent.PutExtra(ExtraDefaultPackage, _appContext.Context.PackageName);
                intent.AddFlags(ActivityFlags.NewTask);
                _appContext.Context.StartActivity(intent);
            }
            catch (Exception)
            {
            }
            return Task.FromResult(IsDefaultSmsApp);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SmsThreadModel>> GetThreadsAsync()
        {
            return Task.Run<IReadOnlyList<SmsThreadModel>>(() =>
            {
                var result = new List<SmsThreadModel>();
                if (!HasReadPermission)
                    return result;

                Dictionary<string, string> contacts = LoadContactNames();
                var map = new Dictionary<long, SmsThreadModel>();

                using ICursor cursor = _appContext.Context.ContentResolver.Query(
                    Telephony.Sms.ContentUri, null, null, null, ColumnDate + " DESC");
                if (cursor == null)
                    return result;

                int threadIndex = cursor.GetColumnIndex(ColumnThreadId);
                int addressIndex = cursor.GetColumnIndex(ColumnAddress);
                int bodyIndex = cursor.GetColumnIndex(ColumnBody);
                int dateIndex = cursor.GetColumnIndex(ColumnDate);
                int typeIndex = cursor.GetColumnIndex(ColumnType);
                int readIndex = cursor.GetColumnIndex(ColumnRead);

                while (cursor.MoveToNext())
                {
                    long threadId = GetLong(cursor, threadIndex);
                    if (!map.TryGetValue(threadId, out SmsThreadModel thread))
                    {
                        string address = GetString(cursor, addressIndex);
                        thread = new SmsThreadModel
                        {
                            ThreadId = threadId,
                            Address = address,
                            Snippet = GetString(cursor, bodyIndex),
                            DateUtc = FromMillis(GetLong(cursor, dateIndex)),
                        };
                        thread.DisplayName = ResolveName(contacts, address);
                        map[threadId] = thread;
                    }

                    if ((GetInt(cursor, typeIndex, 0) == MessageTypeInbox) && (GetInt(cursor, readIndex, 1) == 0))
                        thread.UnreadCount++;
                }

                return map.Values.OrderByDescending(t => t.DateUtc).ToList();
            });
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SmsMessageModel>> GetMessagesAsync(long threadId)
        {
            return Task.Run<IReadOnlyList<SmsMessageModel>>(() =>
            {
                var result = new List<SmsMessageModel>();
                if (!HasReadPermission)
                    return result;

                using ICursor cursor = _appContext.Context.ContentResolver.Query(
                    Telephony.Sms.ContentUri, null, ColumnThreadId + "=?",
                    new[] { threadId.ToString(CultureInfo.InvariantCulture) }, ColumnDate + " ASC");
                if (cursor == null)
                    return result;

                int idIndex = cursor.GetColumnIndex(ColumnId);
                int addressIndex = cursor.GetColumnIndex(ColumnAddress);
                int bodyIndex = cursor.GetColumnIndex(ColumnBody);
                int dateIndex = cursor.GetColumnIndex(ColumnDate);
                int typeIndex = cursor.GetColumnIndex(ColumnType);

                while (cursor.MoveToNext())
                {
                    result.Add(new SmsMessageModel
                    {
                        Id = GetLong(cursor, idIndex),
                        ThreadId = threadId,
                        Address = GetString(cursor, addressIndex),
                        Body = GetString(cursor, bodyIndex),
                        DateUtc = FromMillis(GetLong(cursor, dateIndex)),
                        Incoming = GetInt(cursor, typeIndex, MessageTypeInbox) == MessageTypeInbox,
                    });
                }
                return result;
            });
        }

        /// <inheritdoc/>
        public Task SendSmsAsync(string address, string body)
        {
            return Task.Run(() =>
            {
                if (!HasSendPermission || string.IsNullOrWhiteSpace(address))
                    return;

                SmsManager manager = SmsManager.Default;
                if (manager == null)
                    return;

                if (body.Length > 160)
                {
                    IList<string> parts = manager.DivideMessage(body);
                    manager.SendMultipartTextMessage(address, null, parts, null, null);
                }
                else
                {
                    manager.SendTextMessage(address, null, body, null, null);
                }

                if (IsDefaultSmsApp)
                    RecordSentMessage(address, body);
            });
        }

        /// <inheritdoc/>
        public Task MarkThreadReadAsync(long threadId)
        {
            return Task.Run(() =>
            {
                if (!IsDefaultSmsApp)
                    return;
                try
                {
                    var values = new ContentValues();
                    values.Put(ColumnRead, 1);
                    _appContext.Context.ContentResolver.Update(
                        Telephony.Sms.ContentUri, values, ColumnThreadId + "=? AND " + ColumnRead + "=0",
                        new[] { threadId.ToString(CultureInfo.InvariantCulture) });
                }
                catch (Exception)
                {
                }
            });
        }

        private void RecordSentMessage(string address, string body)
        {
            try
            {
                long threadId = Telephony.Threads.GetOrCreateThreadId(_appContext.Context, address);
                var values = new ContentValues();
                values.Put(ColumnAddress, address);
                values.Put(ColumnBody, body);
                values.Put(ColumnDate, ToUnixMillis(DateTime.UtcNow));
                values.Put(ColumnRead, 1);
                values.Put(ColumnType, MessageTypeSent);
                values.Put(ColumnThreadId, threadId);
                _appContext.Context.ContentResolver.Insert(Telephony.Sms.Sent.ContentUri, values);
            }
            catch (Exception)
            {
            }
        }

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

        private static long ToUnixMillis(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        }

        private static DateTime FromMillis(long millis)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
        }

        private static long GetLong(ICursor cursor, int index)
        {
            return (index >= 0) ? cursor.GetLong(index) : 0;
        }

        private static int GetInt(ICursor cursor, int index, int defaultValue)
        {
            return (index >= 0) ? cursor.GetInt(index) : defaultValue;
        }

        private static string GetString(ICursor cursor, int index)
        {
            return (index >= 0) ? cursor.GetString(index) : null;
        }
    }
}
