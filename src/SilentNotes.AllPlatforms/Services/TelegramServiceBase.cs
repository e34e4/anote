// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SilentNotes.Models;
using SilentNotes.Workers;
using TL;
using WTelegram;

namespace SilentNotes.Services
{
    /// <summary>
    /// Cross-platform WTelegramClient implementation of <see cref="ITelegramService"/>.
    /// Privacy: asserts offline after connect and every ~4 minutes; never sends typing or online;
    /// never calls Stories_* read APIs; marks history read only via <see cref="MarkChatReadOnInteractAsync"/>.
    /// Media is never auto-downloaded.
    /// </summary>
    public abstract class TelegramServiceBase : ITelegramService
    {
#if RELEASE
        private const string StoreFileName = "anote_telegram_store.xml";
#else
        private const string StoreFileName = "anote_telegram_store_dev.xml";
#endif

        private static readonly TimeSpan OfflineRefreshInterval = TimeSpan.FromMinutes(4);
        /// <summary>Fail fast on flaky networks so the UI never sits on a spinner forever.</summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan NetworkOperationTimeout = TimeSpan.FromSeconds(15);

        private readonly IXmlFileService _xmlFileService;
        private readonly IDataProtectionService _dataProtectionService;
        private readonly IInternetStateService _internetStateService;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, AccountRuntime> _runtimes = new ConcurrentDictionary<string, AccountRuntime>(StringComparer.Ordinal);

        private LoginContext _pendingLogin;
        private TelegramAuthState _pendingAuthState = TelegramAuthState.LoggedOut;
        private string _lastCodeDeliveryHint = string.Empty;
        private string _lastQrLoginUrl = string.Empty;
        private CancellationTokenSource _qrLoginCts;
        private TaskCompletionSource<string> _passwordPromptTcs;
        private bool _disposed;
        private static Action<int, string> _activeLogSink;

        /// <inheritdoc/>
        public string LastCodeDeliveryHint => _lastCodeDeliveryHint ?? string.Empty;

        /// <inheritdoc/>
        public string LastQrLoginUrl => _lastQrLoginUrl ?? string.Empty;

        protected TelegramServiceBase(
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService,
            IInternetStateService internetStateService = null)
        {
            _xmlFileService = xmlFileService;
            _dataProtectionService = dataProtectionService;
            _internetStateService = internetStateService;
            EnsureCompatibleLogging();
            _activeLogSink = OnWTelegramLog;
        }

        /// <inheritdoc/>
        public bool HasPendingLogin => _pendingLogin != null;

        /// <summary>
        /// Android/MAUI does not support Console.ForegroundColor used by WTelegram's default logger
        /// (throws PlatformNotSupportedException / Arg_PlatformNotSupported when creating a Client).
        /// </summary>
        private static void EnsureCompatibleLogging()
        {
            Helpers.Log = (level, message) =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[WTelegram " + level + "] " + message);
                    _activeLogSink?.Invoke(level, message);
                }
                catch
                {
                }
            };
        }

        /// <inheritdoc/>
        public void OpenOfficialTelegramApp()
        {
            OpenOfficialTelegramAppCore();
        }

        /// <inheritdoc/>
        public void OpenOfficialTelegramDevices()
        {
            OpenOfficialTelegramDevicesCore();
        }

        /// <summary>Platform implementation that brings the official Telegram app to the foreground.</summary>
        protected virtual void OpenOfficialTelegramAppCore()
        {
        }

        /// <summary>Platform hook to open Telegram Settings → Devices (Link Desktop Device).</summary>
        protected virtual void OpenOfficialTelegramDevicesCore()
        {
            OpenOfficialTelegramAppCore();
        }

        /// <summary>App-private directory for the XML account store.</summary>
        protected abstract string GetStoreDirectory();

        /// <summary>
        /// External tg root (Android: /sdcard/tg). Downloads go here; cache/sessions under root/cache.
        /// </summary>
        protected abstract string GetExternalTgRoot();

        /// <summary>Platform hook for incoming Telegram message notifications (Android posts a system notification).</summary>
        protected virtual void ShowIncomingMessageNotification(string title, string body, string chatKey, bool silent)
        {
        }

        /// <inheritdoc/>
        public event Action ChatsChanged;

        /// <inheritdoc/>
        public event Action<string> MessagesChanged;

        /// <inheritdoc/>
        public event Action AuthChanged;

        /// <inheritdoc/>
        public event Action<TelegramMessageModel> MentionOrReplyReceived;

        /// <inheritdoc/>
        public void EnsureDirectories()
        {
            try
            {
                string root = GetExternalTgRoot();
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(GetCacheDirectory());
                Directory.CreateDirectory(GetSessionsDirectory());
                Directory.CreateDirectory(GetStoreDirectory());
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public TelegramStoreModel LoadStore()
        {
            try
            {
                string filePath = Path.Combine(GetStoreDirectory(), StoreFileName);
                if (_xmlFileService.TryLoad(filePath, out XDocument xml))
                {
                    var store = XmlUtils.DeserializeFromXmlDocument<TelegramStoreModel>(xml);
                    if (store != null)
                    {
                        UnprotectSecret(store.EncryptedApiHash, value => store.ApiHash = value);
                        return store;
                    }
                }
            }
            catch (Exception)
            {
            }
            return new TelegramStoreModel();
        }

        /// <inheritdoc/>
        public bool SaveStore(TelegramStoreModel store)
        {
            if (store == null)
                return false;

            try
            {
                if (_dataProtectionService != null)
                    store.EncryptedApiHash = ProtectSecret(store.ApiHash);

                string dir = GetStoreDirectory();
                Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, StoreFileName);
                return _xmlFileService.TrySerializeAndSave(filePath, store);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public TelegramAuthState GetAuthState(string accountId = null)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                if (_pendingLogin != null)
                    return _pendingAuthState;
                accountId = LoadStore().ActiveAccountId;
            }

            if (string.IsNullOrEmpty(accountId))
            {
                var store = LoadStore();
                if (store.ApiId <= 0 || string.IsNullOrWhiteSpace(store.ApiHash))
                    return TelegramAuthState.NeedApiCredentials;
                return store.Accounts.Count == 0 ? TelegramAuthState.LoggedOut : TelegramAuthState.LoggedOut;
            }

            if (_runtimes.TryGetValue(accountId, out AccountRuntime runtime))
                return runtime.AuthState;

            return TelegramAuthState.LoggedOut;
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> StartAddAccountAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var store = LoadStore();
            if (store.ApiId <= 0 || string.IsNullOrWhiteSpace(store.ApiHash))
            {
                SetPendingAuth(TelegramAuthState.NeedApiCredentials);
                return TelegramOperationResult.Fail("API credentials are required.", TelegramAuthState.NeedApiCredentials);
            }

            await CancelLoginAsync().ConfigureAwait(false);

            EnsureDirectories();
            var account = new TelegramAccountModel
            {
                SessionFileName = accountSessionFileName(Guid.NewGuid().ToString("N")),
            };

            Client client;
            try
            {
                client = CreateClient(store, account);
            }
            catch (Exception ex)
            {
                SetPendingAuth(TelegramAuthState.Error);
                return TelegramOperationResult.Fail(ex.Message, TelegramAuthState.Error);
            }

            _lastCodeDeliveryHint = string.Empty;
            _lastQrLoginUrl = string.Empty;
            _activeLogSink = OnWTelegramLog;
            client.OnOther += OnPendingLoginOther;
            _pendingLogin = new LoginContext { Account = account, Client = client, Store = store };
            SetPendingAuth(TelegramAuthState.NeedPhone);
            return TelegramOperationResult.Ok(TelegramAuthState.NeedPhone);
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> StartQrLoginAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var store = LoadStore();
            if (store.ApiId <= 0 || string.IsNullOrWhiteSpace(store.ApiHash))
            {
                SetPendingAuth(TelegramAuthState.NeedApiCredentials);
                return TelegramOperationResult.Fail("API credentials are required.", TelegramAuthState.NeedApiCredentials);
            }

            await CancelLoginAsync().ConfigureAwait(false);

            EnsureDirectories();
            var account = new TelegramAccountModel
            {
                SessionFileName = accountSessionFileName(Guid.NewGuid().ToString("N")),
            };

            Client client;
            try
            {
                client = CreateClient(store, account);
            }
            catch (Exception ex)
            {
                SetPendingAuth(TelegramAuthState.Error);
                return TelegramOperationResult.Fail(ex.Message, TelegramAuthState.Error);
            }

            _lastCodeDeliveryHint =
                "Scan this QR with official Telegram: Settings → Devices → Link Desktop Device. "
                + "SMS codes are not sent to third-party apps (since 2023). Split-screen or another device works best.";
            _lastQrLoginUrl = string.Empty;
            _activeLogSink = OnWTelegramLog;
            _pendingLogin = new LoginContext { Account = account, Client = client, Store = store, IsQrLogin = true };
            SetPendingAuth(TelegramAuthState.NeedQr);
            RaiseAuthChanged();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _qrLoginCts = cts;
            _ = RunQrLoginAsync(client, cts.Token);
            return TelegramOperationResult.Ok(TelegramAuthState.NeedQr, _lastCodeDeliveryHint);
        }

        private async Task RunQrLoginAsync(Client client, CancellationToken ct)
        {
            try
            {
                User me = await client.LoginWithQRCode(
                    url =>
                    {
                        _lastQrLoginUrl = url ?? string.Empty;
                        Helpers.Log(3, "QR login URL refreshed");
                        if (_pendingAuthState == TelegramAuthState.NeedQr
                            || _pendingAuthState == TelegramAuthState.Connecting)
                        {
                            SetPendingAuth(TelegramAuthState.NeedQr);
                        }
                        RaiseAuthChanged();
                    },
                    except_ids: null,
                    logoutFirst: true,
                    ct: ct).ConfigureAwait(false);

                if (me == null || ct.IsCancellationRequested)
                    return;

                if (_pendingLogin?.Client != client)
                    return;

                await FinishLoginWithUserAsync(me).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Helpers.Log(2, "QR login failed: " + ex.Message);
                if (_pendingLogin?.Client == client)
                {
                    _lastCodeDeliveryHint = "QR login failed: " + ex.Message + ". Cancel and try again.";
                    SetPendingAuth(TelegramAuthState.NeedQr);
                    RaiseAuthChanged();
                }
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ContinueLoginAsync(
            string phone = null,
            string code = null,
            string password = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_pendingLogin?.Client == null)
                return TelegramOperationResult.Fail("No login in progress.", TelegramAuthState.LoggedOut);

            TelegramAuthState stepBefore = _pendingAuthState;
            SetPendingAuth(TelegramAuthState.Connecting);
            try
            {
                switch (stepBefore)
                {
                    case TelegramAuthState.NeedPhone:
                        return await SendCodeExplicitAsync(phone).ConfigureAwait(false);
                    case TelegramAuthState.NeedCode:
                        return await SignInExplicitAsync(code).ConfigureAwait(false);
                    case TelegramAuthState.NeedPassword:
                        if (_pendingLogin?.IsQrLogin == true && _passwordPromptTcs != null)
                        {
                            if (string.IsNullOrEmpty(password))
                            {
                                SetPendingAuth(TelegramAuthState.NeedPassword);
                                return TelegramOperationResult.Fail("2FA password is required.", TelegramAuthState.NeedPassword);
                            }

                            TaskCompletionSource<string> tcs = Interlocked.Exchange(ref _passwordPromptTcs, null);
                            tcs?.TrySetResult(password);
                            SetPendingAuth(TelegramAuthState.Connecting);
                            return TelegramOperationResult.Ok(TelegramAuthState.Connecting, "Checking 2FA…");
                        }

                        return await CheckPasswordExplicitAsync(password).ConfigureAwait(false);
                    default:
                        SetPendingAuth(stepBefore);
                        return TelegramOperationResult.Fail("Unexpected login state: " + stepBefore, stepBefore);
                }
            }
            catch (RpcException ex) when (IsRecoverableLoginError(ex))
            {
                SetPendingAuth(stepBefore);
                return TelegramOperationResult.Fail(DescribeLoginRpcError(ex), stepBefore, _lastCodeDeliveryHint);
            }
            catch (Exception ex)
            {
                SetPendingAuth(stepBefore);
                return TelegramOperationResult.Fail(ex.Message, stepBefore, _lastCodeDeliveryHint);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ResendVerificationCodeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_pendingLogin?.Client == null)
                return TelegramOperationResult.Fail("No login in progress.", TelegramAuthState.LoggedOut);
            if (_pendingAuthState != TelegramAuthState.NeedCode)
                return TelegramOperationResult.Fail("Not waiting for a verification code.", _pendingAuthState);
            if (string.IsNullOrEmpty(_pendingLogin.PhoneCodeHash) || string.IsNullOrEmpty(_pendingLogin.Account?.Phone))
                return TelegramOperationResult.Fail("Missing phone_code_hash — submit the phone number again.", TelegramAuthState.NeedPhone);

            SetPendingAuth(TelegramAuthState.Connecting);
            try
            {
#pragma warning disable CS0618
                Auth_SentCodeBase sentBase = await _pendingLogin.Client.Auth_ResendCode(
                    _pendingLogin.Account.Phone,
                    _pendingLogin.PhoneCodeHash).ConfigureAwait(false);
#pragma warning restore CS0618
                return await ApplySentCodeAsync(sentBase, allowAutoResend: false).ConfigureAwait(false);
            }
            catch (RpcException ex) when (IsRecoverableLoginError(ex))
            {
                SetPendingAuth(TelegramAuthState.NeedCode);
                return TelegramOperationResult.Fail(DescribeLoginRpcError(ex), TelegramAuthState.NeedCode, _lastCodeDeliveryHint);
            }
            catch (Exception ex)
            {
                SetPendingAuth(TelegramAuthState.NeedCode);
                return TelegramOperationResult.Fail(ex.Message, TelegramAuthState.NeedCode, _lastCodeDeliveryHint);
            }
        }

        private async Task<TelegramOperationResult> SendCodeExplicitAsync(string phone)
        {
            string normalized = NormalizePhone(phone);
            if (string.IsNullOrEmpty(normalized))
            {
                SetPendingAuth(TelegramAuthState.NeedPhone);
                return TelegramOperationResult.Fail("Phone number is required (international format, e.g. +15551234567).", TelegramAuthState.NeedPhone);
            }

            LoginContext pending = _pendingLogin;
            pending.Account.Phone = normalized;
            Client client = pending.Client;
            TelegramStoreModel store = pending.Store;

            await client.ConnectAsync().ConfigureAwait(false);

            // Prefer call-based fallbacks when Telegram allows them (App codes are easy to miss
            // on the same phone). Never enable Firebase — we cannot complete SafetyNet.
            var settings = new CodeSettings
            {
                flags = CodeSettings.Flags.allow_flashcall
                    | CodeSettings.Flags.allow_missed_call
                    | CodeSettings.Flags.current_number,
            };
#pragma warning disable CS0618
            Auth_SentCodeBase sentBase;
            try
            {
                sentBase = await client.Auth_SendCode(normalized, store.ApiId, store.ApiHash, settings).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.Code == 500 && string.Equals(ex.Message, "AUTH_RESTART", StringComparison.Ordinal))
            {
                sentBase = await client.Auth_SendCode(normalized, store.ApiId, store.ApiHash, settings).ConfigureAwait(false);
            }
#pragma warning restore CS0618

            Helpers.Log(3, "Auth_SendCode returned " + (sentBase?.GetType().Name ?? "null"));
            return await ApplySentCodeAsync(sentBase, allowAutoResend: true).ConfigureAwait(false);
        }

        private async Task<TelegramOperationResult> ApplySentCodeAsync(Auth_SentCodeBase sentBase, bool allowAutoResend = false)
        {
            if (sentBase is Auth_SentCodeSuccess success)
                return await CompleteAuthorizationAsync(success.authorization).ConfigureAwait(false);

            if (sentBase is Auth_SentCodePaymentRequired)
            {
                SetPendingAuth(TelegramAuthState.Error);
                return TelegramOperationResult.Fail("Telegram requires a paid verification for this number (unsupported).", TelegramAuthState.Error);
            }

            if (sentBase is Auth_SentCode { type: Auth_SentCodeTypeSetUpEmailRequired } emailRequired)
            {
                _pendingLogin.PhoneCodeHash = emailRequired.phone_code_hash;
#pragma warning disable CS0618
                Auth_SentCodeBase resentEmail = await _pendingLogin.Client.Auth_ResendCode(
                    _pendingLogin.Account.Phone,
                    _pendingLogin.PhoneCodeHash).ConfigureAwait(false);
#pragma warning restore CS0618
                return await ApplySentCodeAsync(resentEmail, allowAutoResend: false).ConfigureAwait(false);
            }

            if (sentBase is not Auth_SentCode sent)
            {
                SetPendingAuth(TelegramAuthState.Error);
                return TelegramOperationResult.Fail("Unexpected Auth_SendCode result: " + (sentBase?.GetType().Name ?? "null"), TelegramAuthState.Error);
            }

            _pendingLogin.PhoneCodeHash = sent.phone_code_hash;
            _pendingLogin.LastSentCode = sent;
            string typeName = sent.type?.GetType().Name?.Replace("Auth_SentCodeType", string.Empty) ?? "unknown";
            Helpers.Log(3, "A verification code has been sent via " + typeName
                + (sent.next_type != null ? (" next=" + sent.next_type) : " next=none")
                + (sent.timeout > 0 ? (" timeout=" + sent.timeout + "s") : string.Empty));

            // App codes often never surface on the same phone. If Telegram offers another method,
            // wait the full API timeout then ResendCode → SMS/call/etc.
            bool shouldAutoResend = allowAutoResend
                && sent.next_type != null
                && (sent.type is Auth_SentCodeTypeApp
                    || sent.type is Auth_SentCodeTypeFirebaseSms);

            HandleSentCode(sent);
            SetPendingAuth(TelegramAuthState.NeedCode);

            if (shouldAutoResend)
            {
                // Telegram rejects ResendCode until timeout elapses — do not cap short.
                int waitSec = sent.timeout > 0 ? Math.Max(1, sent.timeout) : 60;
                waitSec = Math.Min(waitSec, 180);
                _lastCodeDeliveryHint =
                    "Telegram sent an App code for "
                    + (_pendingLogin.Account.Phone ?? string.Empty)
                    + " (often invisible on this phone). Enter it if you have it; otherwise waiting "
                    + waitSec + "s then switching to " + sent.next_type + "…";
                RaiseAuthChanged();
                Helpers.Log(3, "Auto-resend in " + waitSec + "s → " + sent.next_type);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitSec)).ConfigureAwait(false);
                    if (_pendingLogin == null || _pendingAuthState != TelegramAuthState.NeedCode)
                        return TelegramOperationResult.Ok(_pendingAuthState, _lastCodeDeliveryHint);
#pragma warning disable CS0618
                    Auth_SentCodeBase resent = await _pendingLogin.Client.Auth_ResendCode(
                        _pendingLogin.Account.Phone,
                        _pendingLogin.PhoneCodeHash).ConfigureAwait(false);
#pragma warning restore CS0618
                    return await ApplySentCodeAsync(resent, allowAutoResend: false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Helpers.Log(2, "Auto-resend failed: " + ex.Message);
                    _lastCodeDeliveryHint = BuildCodeHint(sent, typeName)
                        + " Auto-switch failed (" + ex.Message + "). Tap Resend.";
                    RaiseAuthChanged();
                    return TelegramOperationResult.Ok(TelegramAuthState.NeedCode, _lastCodeDeliveryHint);
                }
            }

            _lastCodeDeliveryHint = BuildCodeHint(sent, typeName);
            RaiseAuthChanged();
            return TelegramOperationResult.Ok(TelegramAuthState.NeedCode, _lastCodeDeliveryHint);
        }

        private string BuildCodeHint(Auth_SentCode sent, string typeName)
        {
            string phone = _pendingLogin?.Account?.Phone ?? string.Empty;
            string hint = DescribeSentCode(sent);
            hint += " Number: " + phone + ". [SendCode OK: " + typeName + "]";
            if (sent.next_type != null)
                hint += " Tap Resend for " + sent.next_type + ".";
            else if (sent.type is Auth_SentCodeTypeApp)
            {
                hint += " Official Telegram must already be logged into THIS exact number"
                    + " (another phone/tablet/desktop is best). On this phone check the notification shade"
                    + " and the service chat named “Telegram”. Custom clients (X/Plus/Neko) often do not show App codes.";
            }
            return hint;
        }

        private async Task<TelegramOperationResult> SignInExplicitAsync(string code)
        {
            string trimmed = code?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                SetPendingAuth(TelegramAuthState.NeedCode);
                return TelegramOperationResult.Fail("Verification code is required.", TelegramAuthState.NeedCode, _lastCodeDeliveryHint);
            }

            LoginContext pending = _pendingLogin;
            if (string.IsNullOrEmpty(pending.PhoneCodeHash) || string.IsNullOrEmpty(pending.Account?.Phone))
            {
                SetPendingAuth(TelegramAuthState.NeedPhone);
                return TelegramOperationResult.Fail("Login session incomplete — enter the phone number again.", TelegramAuthState.NeedPhone);
            }

#pragma warning disable CS0618
            try
            {
                Auth_AuthorizationBase authorization = await pending.Client.Auth_SignIn(
                    pending.Account.Phone,
                    pending.PhoneCodeHash,
                    trimmed).ConfigureAwait(false);
                return await CompleteAuthorizationAsync(authorization).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.Code == 401 && string.Equals(ex.Message, "SESSION_PASSWORD_NEEDED", StringComparison.Ordinal))
            {
                SetPendingAuth(TelegramAuthState.NeedPassword);
                return TelegramOperationResult.Ok(TelegramAuthState.NeedPassword);
            }
            catch (RpcException ex) when (ex.Code == 400 && string.Equals(ex.Message, "PHONE_NUMBER_UNOCCUPIED", StringComparison.Ordinal))
            {
                Auth_AuthorizationBase authorization = await pending.Client.Auth_SignUp(
                    pending.Account.Phone,
                    pending.PhoneCodeHash,
                    "aNote",
                    string.Empty).ConfigureAwait(false);
                return await CompleteAuthorizationAsync(authorization).ConfigureAwait(false);
            }
#pragma warning restore CS0618
        }

        private async Task<TelegramOperationResult> CheckPasswordExplicitAsync(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                SetPendingAuth(TelegramAuthState.NeedPassword);
                return TelegramOperationResult.Fail("Password is required.", TelegramAuthState.NeedPassword);
            }

            Client client = _pendingLogin.Client;
            Account_Password accountPassword = await client.Account_GetPassword().ConfigureAwait(false);
            InputCheckPasswordSRP check = await Client.InputCheckPassword(accountPassword, password).ConfigureAwait(false);
#pragma warning disable CS0618
            Auth_AuthorizationBase authorization = await client.Auth_CheckPassword(check).ConfigureAwait(false);
#pragma warning restore CS0618
            return await CompleteAuthorizationAsync(authorization).ConfigureAwait(false);
        }

        private async Task<TelegramOperationResult> CompleteAuthorizationAsync(Auth_AuthorizationBase authorization)
        {
            if (authorization is Auth_AuthorizationSignUpRequired)
            {
                LoginContext pending = _pendingLogin;
#pragma warning disable CS0618
                authorization = await pending.Client.Auth_SignUp(
                    pending.Account.Phone,
                    pending.PhoneCodeHash,
                    "aNote",
                    string.Empty).ConfigureAwait(false);
#pragma warning restore CS0618
            }

            User me = _pendingLogin.Client.LoginAlreadyDone(authorization);
            return await FinishLoginWithUserAsync(me).ConfigureAwait(false);
        }

        private async Task<TelegramOperationResult> FinishLoginWithUserAsync(User me)
        {
            LoginContext pending = _pendingLogin;
            if (pending?.Client == null)
                return TelegramOperationResult.Fail("Login context lost.", TelegramAuthState.Error);

            ApplyUserToAccount(pending.Account, me);
            var store = LoadStore();
            store.ApiId = pending.Store.ApiId;
            store.ApiHash = pending.Store.ApiHash;
            if (store.Accounts.All(a => !string.Equals(a.Id, pending.Account.Id, StringComparison.Ordinal)))
                store.Accounts.Add(pending.Account);
            store.ActiveAccountId = pending.Account.Id;
            SaveStore(store);

            try
            {
                pending.Client.OnOther -= OnPendingLoginOther;
            }
            catch (Exception)
            {
            }

            var runtime = new AccountRuntime
            {
                AccountId = pending.Account.Id,
                Account = pending.Account,
                Client = pending.Client,
                AuthState = TelegramAuthState.Ready,
            };
            _runtimes[pending.Account.Id] = runtime;
            _pendingLogin = null;
            _lastCodeDeliveryHint = string.Empty;
            _lastQrLoginUrl = string.Empty;
            CancellationTokenSource doneQr = Interlocked.Exchange(ref _qrLoginCts, null);
            doneQr?.Dispose();

            AttachUpdateManager(runtime);
            await AssertOfflineAsync(runtime.Client).ConfigureAwait(false);
            StartOfflineTimer(runtime);

            SetPendingAuth(TelegramAuthState.LoggedOut);
            RaiseAuthChanged();
            await RefreshChatsAsync(runtime.AccountId).ConfigureAwait(false);
            return TelegramOperationResult.Ok(TelegramAuthState.Ready);
        }

        /// <inheritdoc/>
        public async Task CancelLoginAsync()
        {
            CancellationTokenSource qrCts = Interlocked.Exchange(ref _qrLoginCts, null);
            try
            {
                qrCts?.Cancel();
            }
            catch (Exception)
            {
            }
            qrCts?.Dispose();

            TaskCompletionSource<string> pwdTcs = Interlocked.Exchange(ref _passwordPromptTcs, null);
            pwdTcs?.TrySetCanceled();

            LoginContext pending = Interlocked.Exchange(ref _pendingLogin, null);
            if (pending?.Client != null)
            {
                try
                {
                    pending.Client.OnOther -= OnPendingLoginOther;
                    await pending.Client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
            _lastCodeDeliveryHint = string.Empty;
            _lastQrLoginUrl = string.Empty;
            SetPendingAuth(TelegramAuthState.LoggedOut);
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ConnectAccountAsync(string accountId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(accountId))
                return TelegramOperationResult.Fail("Account id is required.");

            // Single-flight: overlapping resume/refresh must not stack hung connects.
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ConnectAccountCoreAsync(accountId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private async Task<TelegramOperationResult> ConnectAccountCoreAsync(
            string accountId,
            CancellationToken cancellationToken)
        {
            var store = LoadStore();
            var account = store.Accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));
            if (account == null)
                return TelegramOperationResult.Fail("Account not found.");
            if (store.ApiId <= 0 || string.IsNullOrWhiteSpace(store.ApiHash))
                return TelegramOperationResult.Fail("API credentials are required.", TelegramAuthState.NeedApiCredentials);

            // After long screen-off / VPN flaps the socket can die while AuthState stays Ready.
            // Reuse only a live connection; otherwise tear down and resume the session.
            if (IsLiveConnected(accountId))
                return TelegramOperationResult.Ok(TelegramAuthState.Ready);

            if (_internetStateService != null && !_internetStateService.IsInternetConnected())
            {
                return TelegramOperationResult.Fail(
                    "No internet connection. Tap Refresh when you are back online.",
                    TelegramAuthState.Error);
            }

            await DisconnectAccountAsync(accountId).ConfigureAwait(false);
            EnsureDirectories();

            Client client;
            try
            {
                client = CreateClient(store, account);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, TelegramAuthState.Error);
            }

            var runtime = new AccountRuntime
            {
                AccountId = accountId,
                Account = account,
                Client = client,
                AuthState = TelegramAuthState.Connecting,
            };
            _runtimes[accountId] = runtime;
            RaiseAuthChanged();

            try
            {
                User me = await WithTimeout(
                    client.LoginUserIfNeeded(),
                    ConnectTimeout,
                    "Telegram connect timed out. Check network/VPN and tap Refresh.",
                    cancellationToken).ConfigureAwait(false);
                ApplyUserToAccount(account, me);
                SaveStore(store);

                AttachUpdateManager(runtime);
                client.OnOther += (obj) => OnRuntimeOther(runtime, obj);
                await AssertOfflineAsync(client).ConfigureAwait(false);
                StartOfflineTimer(runtime);

                runtime.AuthState = TelegramAuthState.Ready;
                RaiseAuthChanged();
                // Use core refresh — public RefreshChatsAsync would call EnsureConnected and recurse.
                await RefreshChatsCoreAsync(runtime, cancellationToken).ConfigureAwait(false);
                return TelegramOperationResult.Ok(TelegramAuthState.Ready);
            }
            catch (Exception ex)
            {
                // Dispose the client so abandoned MTProto work cannot keep the process busy.
                await DisconnectAccountAsync(accountId).ConfigureAwait(false);
                return TelegramOperationResult.Fail(
                    DescribeConnectionFailure(ex),
                    TelegramAuthState.Error);
            }
        }

        private static string DescribeConnectionFailure(Exception ex)
        {
            if (ex is TimeoutException)
                return ex.Message;
            if (ex is IOException)
                return "Network error. Tap Refresh when the connection is stable.";
            string msg = ex?.Message ?? "Connect failed";
            if (IsLikelyDeadConnectionMessage(msg))
                return "Telegram unreachable right now. Tap Refresh to retry.";
            return msg;
        }

        private static bool IsLikelyDeadConnectionMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg))
                return false;
            return msg.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("socket", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("transport", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("reactor", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Ensures the account client is connected. Reconnects when the MTProto socket died
        /// (common after long screen-off or VPN changes) while AuthState still looked Ready.
        /// </summary>
        private async Task<TelegramOperationResult> EnsureConnectedAsync(
            string accountId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                accountId = LoadStore().ActiveAccountId;
                if (string.IsNullOrEmpty(accountId))
                    return TelegramOperationResult.Fail("No active account.", TelegramAuthState.LoggedOut);
            }

            if (IsLiveConnected(accountId))
                return TelegramOperationResult.Ok(TelegramAuthState.Ready);

            return await ConnectAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        private Task OnRuntimeOther(AccountRuntime runtime, IObject obj)
        {
            try
            {
                // WTelegram reports reactor/socket failures here; force a clean reconnect next time.
                if (obj != null && string.Equals(obj.GetType().Name, "ReactorError", StringComparison.Ordinal))
                {
                    runtime.AuthState = TelegramAuthState.Error;
                    RaiseAuthChanged();
                }
            }
            catch (Exception)
            {
            }
            return Task.CompletedTask;
        }

        private static async Task<T> WithTimeout<T>(
            Task<T> task,
            TimeSpan timeout,
            string timeoutMessage,
            CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            Task completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }
            return await task.ConfigureAwait(false);
        }

        private static async Task WithTimeout(
            Task task,
            TimeSpan timeout,
            string timeoutMessage,
            CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            Task completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }
            await task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DisconnectAccountAsync(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                return;

            if (!_runtimes.TryRemove(accountId, out AccountRuntime runtime))
                return;

            StopOfflineTimer(runtime);
            runtime.AuthState = TelegramAuthState.LoggedOut;
            if (runtime.Client != null)
            {
                try
                {
                    await runtime.Client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
            RaiseAuthChanged();
            RaiseChatsChanged();
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SwitchActiveAccountAsync(string accountId)
        {
            var store = LoadStore();
            if (!string.IsNullOrEmpty(accountId) && store.Accounts.All(a => !string.Equals(a.Id, accountId, StringComparison.Ordinal)))
                return TelegramOperationResult.Fail("Account not found.");

            store.ActiveAccountId = accountId ?? string.Empty;
            SaveStore(store);
            RaiseAuthChanged();
            RaiseChatsChanged();

            if (!string.IsNullOrEmpty(accountId) && !_runtimes.ContainsKey(accountId))
                return await ConnectAccountAsync(accountId).ConfigureAwait(false);

            return TelegramOperationResult.Ok(GetAuthState(accountId));
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> RemoveAccountAsync(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                return TelegramOperationResult.Fail("Account id is required.");

            await DisconnectAccountAsync(accountId).ConfigureAwait(false);

            var store = LoadStore();
            var account = store.Accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));
            if (account != null)
            {
                store.Accounts.Remove(account);
                if (string.Equals(store.ActiveAccountId, accountId, StringComparison.Ordinal))
                    store.ActiveAccountId = store.Accounts.FirstOrDefault()?.Id ?? string.Empty;
                SaveStore(store);

                try
                {
                    string sessionPath = GetSessionPath(account);
                    if (File.Exists(sessionPath))
                        File.Delete(sessionPath);
                }
                catch (Exception)
                {
                }
            }

            RaiseAuthChanged();
            RaiseChatsChanged();
            return TelegramOperationResult.Ok(GetAuthState(store.ActiveAccountId));
        }

        /// <inheritdoc/>
        public IReadOnlyList<TelegramChatModel> GetChats(string accountId = null)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime))
                return Array.Empty<TelegramChatModel>();
            lock (runtime.Gate)
            {
                return DeduplicateChats(runtime.Chats).ToList();
            }
        }

        private static List<TelegramChatModel> DeduplicateChats(IEnumerable<TelegramChatModel> chats)
        {
            var result = new List<TelegramChatModel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TelegramChatModel chat in chats ?? Enumerable.Empty<TelegramChatModel>())
            {
                if (chat == null || string.IsNullOrEmpty(chat.Id))
                    continue;
                if (!seen.Add(chat.Id))
                    continue;
                result.Add(chat);
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> RefreshChatsAsync(string accountId = null, CancellationToken cancellationToken = default)
        {
            string resolvedAccountId = accountId;
            if (string.IsNullOrEmpty(resolvedAccountId))
                resolvedAccountId = LoadStore().ActiveAccountId;

            if (!IsLiveConnected(resolvedAccountId))
            {
                if (_internetStateService != null && !_internetStateService.IsInternetConnected())
                {
                    return TelegramOperationResult.Fail(
                        "No internet connection. Tap Refresh when you are back online.",
                        TelegramAuthState.Error);
                }

                // ConnectAccountAsync already loads dialogs after a successful resume.
                return await ConnectAccountAsync(resolvedAccountId, cancellationToken).ConfigureAwait(false);
            }

            if (!TryGetRuntime(resolvedAccountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            return await RefreshChatsCoreAsync(runtime, cancellationToken).ConfigureAwait(false);
        }

        private bool IsLiveConnected(string accountId)
        {
            return !string.IsNullOrEmpty(accountId)
                && _runtimes.TryGetValue(accountId, out AccountRuntime existing)
                && existing.Client != null
                && existing.AuthState == TelegramAuthState.Ready
                && !existing.Client.Disconnected;
        }

        private async Task<TelegramOperationResult> RefreshChatsCoreAsync(
            AccountRuntime runtime,
            CancellationToken cancellationToken = default)
        {
            if (runtime?.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                // folder 0 = main list, folder 1 = Archived chats
                Messages_Dialogs mainDialogs = await WithTimeout(
                    runtime.Client.Messages_GetAllDialogs(0),
                    NetworkOperationTimeout,
                    "Loading chats timed out. Tap Refresh.",
                    cancellationToken).ConfigureAwait(false);
                Messages_Dialogs archivedDialogs = await WithTimeout(
                    runtime.Client.Messages_GetAllDialogs(1),
                    NetworkOperationTimeout,
                    "Loading chats timed out. Tap Refresh.",
                    cancellationToken).ConfigureAwait(false);
                CollectPeers(runtime, mainDialogs.users, mainDialogs.chats);
                CollectPeers(runtime, archivedDialogs.users, archivedDialogs.chats);

                var chats = new List<TelegramChatModel>();
                AppendDialogs(runtime, mainDialogs, archived: false, chats);
                AppendDialogs(runtime, archivedDialogs, archived: true, chats);

                chats = DeduplicateChats(chats)
                    .OrderByDescending(c => c.IsPinned)
                    .ThenByDescending(c => c.LastMessageAt)
                    .ToList();

                lock (runtime.Gate)
                {
                    runtime.Chats.Clear();
                    runtime.Chats.AddRange(chats);
                }

                RaiseChatsChanged();
                _ = EnrichBotAppsAsync(runtime);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                if (IsLikelyDeadConnection(ex, runtime))
                {
                    await DisconnectAccountAsync(runtime.AccountId).ConfigureAwait(false);
                    return TelegramOperationResult.Fail(
                        "Telegram connection dropped. Tap Refresh to reconnect.",
                        TelegramAuthState.Error);
                }
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        private static bool IsLikelyDeadConnection(Exception ex, AccountRuntime runtime)
        {
            if (runtime?.Client != null && runtime.Client.Disconnected)
                return true;
            if (ex is TimeoutException or IOException or ObjectDisposedException)
                return true;
            return IsLikelyDeadConnectionMessage(ex?.Message);
        }

        private async Task EnrichBotAppsAsync(AccountRuntime runtime)
        {
            if (runtime?.Client == null)
                return;

            List<TelegramChatModel> bots;
            lock (runtime.Gate)
            {
                bots = runtime.Chats.Where(c => c.IsBot && !c.HasBotApp).ToList();
            }

            bool any = false;
            foreach (TelegramChatModel bot in bots)
            {
                try
                {
                    if (!runtime.PeerByChatKey.TryGetValue(bot.Id, out IPeerInfo peerInfo) || peerInfo is not User user || !user.IsBot)
                        continue;

                    Users_UserFull full = await runtime.Client.Users_GetFullUser(new InputUser(user.id, user.access_hash))
                        .ConfigureAwait(false);
                    if (full?.full_user?.bot_info?.menu_button is BotMenuButton menu
                        && !string.IsNullOrWhiteSpace(menu.url))
                    {
                        bot.HasBotApp = true;
                        bot.BotAppLabel = string.IsNullOrWhiteSpace(menu.text) ? "Open" : menu.text;
                        any = true;
                    }
                }
                catch (Exception)
                {
                }
            }

            if (any)
                RaiseChatsChanged();
        }

        private void AppendDialogs(
            AccountRuntime runtime,
            Messages_Dialogs dialogs,
            bool archived,
            List<TelegramChatModel> chats)
        {
            if (dialogs == null)
                return;

            var messagesByPeer = dialogs.Messages?
                .OfType<Message>()
                .GroupBy(m => BuildChatKey(m.Peer))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.id).First(), StringComparer.Ordinal)
                ?? new Dictionary<string, Message>(StringComparer.Ordinal);

            foreach (DialogBase dialogBase in dialogs.Dialogs ?? Array.Empty<DialogBase>())
            {
                if (dialogBase is not Dialog dialog)
                    continue;

                IPeerInfo peerInfo = dialogs.UserOrChat(dialog);
                if (peerInfo == null)
                    continue;

                string chatKey = BuildChatKey(dialog.Peer);
                var chat = MapChat(dialog, peerInfo, chatKey, archived);
                if (messagesByPeer.TryGetValue(chatKey, out Message top))
                {
                    chat.LastMessageAt = top.date;
                    chat.LastMessagePreview = BuildMessagePreview(top);
                }
                chats.Add(chat);
                runtime.PeerByChatKey[chatKey] = peerInfo;
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> OpenChatAsync(
            string chatKey,
            string accountId = null,
            int limit = 40,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey))
                return TelegramOperationResult.Fail("Chat key is required.");

            string resolvedAccountId = accountId;
            if (string.IsNullOrEmpty(resolvedAccountId))
                resolvedAccountId = LoadStore().ActiveAccountId;

            TelegramOperationResult connected = await EnsureConnectedAsync(resolvedAccountId, cancellationToken)
                .ConfigureAwait(false);
            if (!connected.Success)
                return connected;

            if (!TryGetRuntime(resolvedAccountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                Messages_MessagesBase history = await WithTimeout(
                    runtime.Client.Messages_GetHistory(
                        peer,
                        limit: Math.Clamp(limit, 1, 100)),
                    NetworkOperationTimeout,
                    "Loading messages timed out. Tap Refresh.",
                    cancellationToken).ConfigureAwait(false);

                CollectPeersFromMessages(runtime, history);

                var mapped = new List<TelegramMessageModel>();
                foreach (MessageBase msgBase in history.Messages ?? Array.Empty<MessageBase>())
                {
                    if (msgBase is not Message msg)
                        continue;
                    TelegramMessageModel model = MapMessage(runtime, msg, chatKey);
                    mapped.Add(model);
                }

                // Latest messages first (top of the conversation view).
                mapped = mapped
                    .GroupBy(m => m.Id)
                    .Select(g => g.First())
                    .OrderByDescending(m => m.Date)
                    .ThenByDescending(m => m.Id)
                    .ToList();

                int pageSize = Math.Clamp(limit, 1, 100);
                lock (runtime.Gate)
                {
                    runtime.OpenChatKey = chatKey;
                    runtime.Messages.Clear();
                    runtime.Messages.AddRange(mapped);
                    runtime.HasMoreMessages = mapped.Count >= pageSize;
                }

                RaiseMessagesChanged(chatKey);
                return new TelegramOperationResult
                {
                    Success = true,
                    AuthState = runtime.AuthState,
                    ChatKey = chatKey,
                };
            }
            catch (Exception ex)
            {
                if (IsLikelyDeadConnection(ex, runtime))
                {
                    await DisconnectAccountAsync(runtime.AccountId).ConfigureAwait(false);
                    return TelegramOperationResult.Fail(
                        "Telegram connection dropped. Tap Refresh to reconnect.",
                        TelegramAuthState.Error);
                }
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> OpenTelegramLinkAsync(
            string linkOrPath,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!Workers.TelegramLinkParser.TryParse(linkOrPath, out Workers.TelegramLinkParser.ParsedLink parsed))
                return TelegramOperationResult.Fail("Not a Telegram link.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                if (!string.IsNullOrEmpty(parsed.Username))
                {
                    Contacts_ResolvedPeer resolved = await runtime.Client.Contacts_ResolveUsername(parsed.Username)
                        .ConfigureAwait(false);
                    CollectPeers(runtime, resolved.users, resolved.chats);
                    if (resolved.peer == null)
                        return TelegramOperationResult.Fail("Could not resolve @" + parsed.Username, runtime.AuthState);

                    string chatKey = BuildChatKey(resolved.peer);
                    IPeerInfo peerInfo = resolved.UserOrChat
                        ?? (runtime.PeerByChatKey.TryGetValue(chatKey, out IPeerInfo cached) ? cached : null);
                    EnsureChatInList(runtime, chatKey, peerInfo);
                    RaiseChatsChanged();
                    return await OpenChatAsync(chatKey, runtime.AccountId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(parsed.InviteHash))
                {
                    ChatInviteBase invite = await runtime.Client.Messages_CheckChatInvite(parsed.InviteHash)
                        .ConfigureAwait(false);
                    if (invite is ChatInviteAlready already && already.chat != null)
                    {
                        CollectPeers(runtime, null, new Dictionary<long, ChatBase> { [already.chat.ID] = already.chat });
                        string chatKey = BuildChatKeyFromChat(already.chat);
                        EnsureChatInList(runtime, chatKey, already.chat);
                        RaiseChatsChanged();
                        return await OpenChatAsync(chatKey, runtime.AccountId, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }

                    Messages_ChatInviteJoinResult joinResult = await runtime.Client.Messages_ImportChatInvite(parsed.InviteHash)
                        .ConfigureAwait(false);
                    UpdatesBase updates = (joinResult as Messages_ChatInviteJoinResultOk)?.updates;
                    string joinedKey = ExtractChatKeyFromUpdates(runtime, updates);
                    if (string.IsNullOrEmpty(joinedKey))
                        return TelegramOperationResult.Fail("Joined invite but chat was not returned.", runtime.AuthState);

                    RaiseChatsChanged();
                    return await OpenChatAsync(joinedKey, runtime.AccountId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (parsed.PrivateChannelId > 0)
                {
                    if (!runtime.ChatsById.TryGetValue(parsed.PrivateChannelId, out ChatBase privateChat)
                        || privateChat == null)
                    {
                        return TelegramOperationResult.Fail(
                            "Private channel is not in your chat list. Open it from Telegram once first.",
                            runtime.AuthState);
                    }

                    string chatKey = BuildChatKeyFromChat(privateChat);
                    EnsureChatInList(runtime, chatKey, privateChat);
                    RaiseChatsChanged();
                    return await OpenChatAsync(chatKey, runtime.AccountId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                return TelegramOperationResult.Fail("Unsupported Telegram link.", runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, GetAuthState(accountId));
            }
        }

        private void EnsureChatInList(AccountRuntime runtime, string chatKey, IPeerInfo peerInfo)
        {
            if (string.IsNullOrEmpty(chatKey) || peerInfo == null)
                return;

            lock (runtime.Gate)
            {
                runtime.PeerByChatKey[chatKey] = peerInfo;
                if (peerInfo is User user)
                    runtime.Users[user.id] = user;
                if (peerInfo is ChatBase chatBase)
                    runtime.ChatsById[chatBase.ID] = chatBase;

                if (runtime.Chats.Any(c => string.Equals(c.Id, chatKey, StringComparison.Ordinal)))
                    return;

                var chat = new TelegramChatModel
                {
                    Id = chatKey,
                    PeerId = peerInfo.ID,
                };
                switch (peerInfo)
                {
                    case User u:
                        chat.Title = FormatUserName(u);
                        chat.IsBot = u.IsBot;
                        chat.PeerType = TelegramPeerTypes.User;
                        break;
                    case Chat basic:
                        chat.Title = basic.Title ?? basic.title ?? string.Empty;
                        chat.IsGroup = true;
                        chat.PeerType = TelegramPeerTypes.Chat;
                        break;
                    case Channel channel:
                        chat.Title = channel.Title ?? string.Empty;
                        chat.IsChannel = channel.IsChannel;
                        chat.IsGroup = channel.IsGroup;
                        chat.PeerType = TelegramPeerTypes.Channel;
                        break;
                    default:
                        chat.Title = peerInfo.ToString() ?? chatKey;
                        chat.PeerType = TelegramPeerTypes.User;
                        break;
                }

                runtime.Chats.Insert(0, chat);
            }
        }

        private string ExtractChatKeyFromUpdates(AccountRuntime runtime, UpdatesBase updates)
        {
            switch (updates)
            {
                case Updates u:
                    CollectPeers(runtime, u.users, u.chats);
                    if (u.chats != null)
                    {
                        foreach (ChatBase chat in u.chats.Values)
                        {
                            if (chat == null || !chat.IsActive)
                                continue;
                            string key = BuildChatKeyFromChat(chat);
                            EnsureChatInList(runtime, key, chat);
                            return key;
                        }
                    }

                    break;
                case UpdatesCombined combined:
                    CollectPeers(runtime, combined.users, combined.chats);
                    if (combined.chats != null)
                    {
                        foreach (ChatBase chat in combined.chats.Values)
                        {
                            if (chat == null || !chat.IsActive)
                                continue;
                            string key = BuildChatKeyFromChat(chat);
                            EnsureChatInList(runtime, key, chat);
                            return key;
                        }
                    }

                    break;
            }

            return null;
        }

        /// <inheritdoc/>
        public bool HasMoreMessages(string chatKey = null, string accountId = null)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime))
                return false;
            lock (runtime.Gate)
            {
                if (!string.IsNullOrEmpty(chatKey)
                    && !string.Equals(chatKey, runtime.OpenChatKey, StringComparison.Ordinal))
                    return false;
                return runtime.HasMoreMessages;
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> LoadOlderMessagesAsync(
            string chatKey,
            string accountId = null,
            int limit = 40,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey))
                return TelegramOperationResult.Fail("Chat key is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            int oldestId;
            lock (runtime.Gate)
            {
                if (!string.Equals(runtime.OpenChatKey, chatKey, StringComparison.Ordinal))
                    return TelegramOperationResult.Fail("Chat is not open.");
                if (!runtime.HasMoreMessages || runtime.Messages.Count == 0)
                    return TelegramOperationResult.Ok(runtime.AuthState);
                oldestId = runtime.Messages.Min(m => m.Id);
            }

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                int pageSize = Math.Clamp(limit, 1, 100);
                Messages_MessagesBase history = await runtime.Client.Messages_GetHistory(
                    peer,
                    offset_id: oldestId,
                    limit: pageSize).ConfigureAwait(false);

                CollectPeersFromMessages(runtime, history);

                var mapped = new List<TelegramMessageModel>();
                foreach (MessageBase msgBase in history.Messages ?? Array.Empty<MessageBase>())
                {
                    if (msgBase is not Message msg)
                        continue;
                    if (msg.id >= oldestId)
                        continue;
                    mapped.Add(MapMessage(runtime, msg, chatKey));
                }

                mapped = mapped
                    .GroupBy(m => m.Id)
                    .Select(g => g.First())
                    .OrderByDescending(m => m.Date)
                    .ThenByDescending(m => m.Id)
                    .ToList();

                lock (runtime.Gate)
                {
                    var existing = new HashSet<int>(runtime.Messages.Select(m => m.Id));
                    foreach (TelegramMessageModel model in mapped)
                    {
                        if (existing.Add(model.Id))
                            runtime.Messages.Add(model);
                    }
                    runtime.Messages.Sort((a, b) =>
                    {
                        int cmp = b.Date.CompareTo(a.Date);
                        return cmp != 0 ? cmp : b.Id.CompareTo(a.Id);
                    });
                    runtime.HasMoreMessages = mapped.Count >= pageSize;
                }

                RaiseMessagesChanged(chatKey);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ForwardMessageAsync(
            string fromChatKey,
            int messageId,
            string toChatKey,
            bool dropAuthor,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(fromChatKey) || string.IsNullOrEmpty(toChatKey) || messageId <= 0)
                return TelegramOperationResult.Fail("Forward requires source, target, and message id.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer from = ResolveInputPeer(runtime, fromChatKey);
                InputPeer to = ResolveInputPeer(runtime, toChatKey);
                long randomId = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
                await runtime.Client.Messages_ForwardMessages(
                    from_peer: from,
                    id: new[] { messageId },
                    random_id: new[] { randomId },
                    to_peer: to,
                    drop_author: dropAuthor).ConfigureAwait(false);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> DeleteMessageForMeAsync(
            string chatKey,
            int messageId,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey) || messageId <= 0)
                return TelegramOperationResult.Fail("Message id is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                if (peer is InputPeerChannel channelPeer)
                {
                    var inputChannel = new InputChannel(channelPeer.channel_id, channelPeer.access_hash);
                    await runtime.Client.Channels_DeleteMessages(inputChannel, new[] { messageId }).ConfigureAwait(false);
                }
                else
                {
                    await runtime.Client.Messages_DeleteMessages(new[] { messageId }, revoke: false).ConfigureAwait(false);
                }

                lock (runtime.Gate)
                {
                    if (string.Equals(runtime.OpenChatKey, chatKey, StringComparison.Ordinal))
                        runtime.Messages.RemoveAll(m => m.Id == messageId);
                }
                RaiseMessagesChanged(chatKey);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> EditMessageTextAsync(
            string chatKey,
            int messageId,
            string newText,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey) || messageId <= 0)
                return TelegramOperationResult.Fail("Message id is required.");
            if (string.IsNullOrWhiteSpace(newText))
                return TelegramOperationResult.Fail("Edited text is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                UpdatesBase updates = await runtime.Client.Messages_EditMessage(
                    peer,
                    messageId,
                    message: newText.Trim()).ConfigureAwait(false);

                Message edited = null;
                if (updates is Updates upd)
                    edited = upd.UpdateList?.OfType<UpdateEditMessage>().Select(u => u.message).OfType<Message>().FirstOrDefault()
                        ?? upd.UpdateList?.OfType<UpdateEditChannelMessage>().Select(u => u.message).OfType<Message>().FirstOrDefault();
                else if (updates is UpdateShortSentMessage)
                {
                    // Rare for edits; fall through to local patch.
                }

                lock (runtime.Gate)
                {
                    TelegramMessageModel existing = runtime.Messages.FirstOrDefault(m => m.Id == messageId);
                    if (edited != null)
                    {
                        TelegramMessageModel mapped = MapMessage(runtime, edited, chatKey);
                        int idx = runtime.Messages.FindIndex(m => m.Id == messageId);
                        if (idx >= 0)
                            runtime.Messages[idx] = mapped;
                    }
                    else if (existing != null)
                    {
                        existing.Text = newText.Trim();
                        existing.FormattedText = TelegramTextUtils.ReplaceEmojisWithSymbolic(existing.Text);
                    }
                }

                RaiseMessagesChanged(chatKey);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ToggleLikeReactionAsync(
            string chatKey,
            int messageId,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey) || messageId <= 0)
                return TelegramOperationResult.Fail("Message id is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                bool hasMine;
                lock (runtime.Gate)
                {
                    TelegramMessageModel existing = runtime.Messages.FirstOrDefault(m => m.Id == messageId);
                    hasMine = existing?.HasMyReaction == true;
                }

                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                Reaction[] reactions = hasMine
                    ? Array.Empty<Reaction>()
                    : new Reaction[] { new ReactionEmoji { emoticon = "👍" } };

                UpdatesBase updates = await runtime.Client.Messages_SendReaction(
                    peer,
                    messageId,
                    reaction: reactions,
                    add_to_recent: true).ConfigureAwait(false);

                MessageReactions newReactions = null;
                if (updates is Updates upd)
                {
                    newReactions = upd.UpdateList?.OfType<UpdateMessageReactions>()
                        .Where(u => u.msg_id == messageId)
                        .Select(u => u.reactions)
                        .FirstOrDefault();
                }

                lock (runtime.Gate)
                {
                    TelegramMessageModel existing = runtime.Messages.FirstOrDefault(m => m.Id == messageId);
                    if (existing != null)
                    {
                        if (newReactions != null)
                            ApplyReactions(existing, newReactions);
                        else
                        {
                            existing.HasMyReaction = !hasMine;
                            existing.ReactionCount = Math.Max(0, existing.ReactionCount + (hasMine ? -1 : 1));
                        }
                    }
                }

                RaiseMessagesChanged(chatKey);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public void CloseChat(string accountId = null)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime))
                return;
            string previous;
            lock (runtime.Gate)
            {
                previous = runtime.OpenChatKey;
                runtime.OpenChatKey = null;
                runtime.Messages.Clear();
                runtime.HasMoreMessages = false;
            }
            if (!string.IsNullOrEmpty(previous))
                RaiseMessagesChanged(previous);
        }

        /// <inheritdoc/>
        public IReadOnlyList<TelegramMessageModel> GetMessages(string chatKey = null, string accountId = null)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime))
                return Array.Empty<TelegramMessageModel>();
            lock (runtime.Gate)
            {
                if (!string.IsNullOrEmpty(chatKey) && !string.Equals(chatKey, runtime.OpenChatKey, StringComparison.Ordinal))
                    return Array.Empty<TelegramMessageModel>();
                return runtime.Messages.ToList();
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SendTextMessageAsync(
            string chatKey,
            string text,
            string accountId = null,
            int replyToMsgId = 0,
            TelegramTextFormat format = TelegramTextFormat.MarkdownLite,
            bool useTelegramMarkdownV2 = false,
            bool silent = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return TelegramOperationResult.Fail("Message text is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                string plain = text;
                MessageEntity[] entities = null;

                if (useTelegramMarkdownV2)
                {
                    // Prefer WTelegram MarkdownV2 helpers when explicitly requested.
                    plain = text;
                    entities = Markdown.MarkdownToEntities(runtime.Client, ref plain);
                }
                else if (format.HasFlag(TelegramTextFormat.MarkdownLite) || format == TelegramTextFormat.None)
                {
                    var parsed = TelegramTextUtils.ParseMarkdownLite(text);
                    plain = parsed.PlainText;
                    entities = ToTlEntities(parsed.Entities);
                }

                // NEVER call Messages_SetTyping. Default silent=true → no notification sound for peers.
                UpdatesBase updates = await runtime.Client.Messages_SendMessage(
                    peer,
                    plain,
                    NewSendRandomId(),
                    reply_to: BuildReplyTo(replyToMsgId),
                    entities: entities,
                    silent: silent).ConfigureAwait(false);

                Message sent = ExtractSentMessage(updates, plain);
                if (sent != null)
                {
                    TelegramMessageModel model = MapMessage(runtime, sent, chatKey);
                    AppendMessage(runtime, chatKey, model);
                }

                await MarkChatReadOnInteractAsync(chatKey, runtime.AccountId, cancellationToken).ConfigureAwait(false);
                _ = RefreshChatsAsync(runtime.AccountId, cancellationToken);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SendFileAttachmentAsync(
            string chatKey,
            byte[] content,
            string fileName,
            string accountId = null,
            string caption = null,
            int replyToMsgId = 0,
            bool silent = true,
            CancellationToken cancellationToken = default)
        {
            return await SendUploadedMediaAsync(
                chatKey, content, fileName, accountId, caption, asPhoto: false, replyToMsgId, silent, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SendPhotoAttachmentAsync(
            string chatKey,
            byte[] content,
            string fileName,
            string accountId = null,
            string caption = null,
            int replyToMsgId = 0,
            bool silent = true,
            CancellationToken cancellationToken = default)
        {
            return await SendUploadedMediaAsync(
                chatKey, content, fileName, accountId, caption, asPhoto: true, replyToMsgId, silent, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> DownloadMediaAsync(TelegramMediaRef mediaRef, CancellationToken cancellationToken = default)
        {
            if (mediaRef == null)
                return TelegramOperationResult.Fail("Media reference is required.");
            if (string.Equals(mediaRef.MediaKind, TelegramMediaKinds.Sticker, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaRef.MediaKind, TelegramMediaKinds.Story, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaRef.MediaKind, TelegramMediaKinds.None, StringComparison.OrdinalIgnoreCase))
            {
                return TelegramOperationResult.Fail("This media kind is not downloadable.");
            }

            if (!TryGetRuntime(mediaRef.AccountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                EnsureDirectories();
                string fileName = string.IsNullOrWhiteSpace(mediaRef.FileName)
                    ? BuildDefaultFileName(mediaRef)
                    : SanitizeFileName(mediaRef.FileName);
                string targetPath = Path.Combine(GetExternalTgRoot(), fileName);
                targetPath = MakeUniquePath(targetPath);

                if (!runtime.MediaCache.TryGetValue(MediaCacheKey(mediaRef), out object mediaObj))
                {
                    // Re-fetch the message to obtain Photo/Document handles.
                    InputPeer peer = ResolveInputPeer(runtime, mediaRef.ChatKey);
                    Messages_MessagesBase history = await runtime.Client.Messages_GetHistory(
                        peer,
                        offset_id: mediaRef.MessageId + 1,
                        limit: 5).ConfigureAwait(false);
                    CollectPeersFromMessages(runtime, history);
                    Message msg = history.Messages?.OfType<Message>().FirstOrDefault(m => m.id == mediaRef.MessageId);
                    if (msg?.media != null)
                    {
                        mediaObj = ExtractDownloadableMedia(msg.media);
                        if (mediaObj != null)
                            runtime.MediaCache[MediaCacheKey(mediaRef)] = mediaObj;
                    }
                }

                if (mediaObj == null)
                    return TelegramOperationResult.Fail("Media is no longer available.");

                await using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (mediaObj is Photo photo)
                        await runtime.Client.DownloadFileAsync(photo, fs).ConfigureAwait(false);
                    else if (mediaObj is Document document)
                        await runtime.Client.DownloadFileAsync(document, fs).ConfigureAwait(false);
                    else
                        return TelegramOperationResult.Fail("Unsupported media type.");
                }

                return new TelegramOperationResult
                {
                    Success = true,
                    AuthState = runtime.AuthState,
                    LocalPath = targetPath,
                };
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> ClickBotButtonAsync(
            string chatKey,
            TelegramBotButtonModel button,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (button == null)
                return TelegramOperationResult.Fail("Button is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                if (!string.IsNullOrEmpty(button.Url) || button.IsWebApp)
                    return await OpenBotAppUrlAsync(chatKey, button, runtime.AccountId, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(button.Data))
                {
                    InputPeer peer = ResolveInputPeer(runtime, chatKey);
                    byte[] data = Encoding.UTF8.GetBytes(button.Data);
                    Messages_BotCallbackAnswer answer = await runtime.Client.Messages_GetBotCallbackAnswer(
                        peer,
                        button.MessageId,
                        data).ConfigureAwait(false);

                    return new TelegramOperationResult
                    {
                        Success = true,
                        AuthState = runtime.AuthState,
                        Url = answer?.url,
                        ErrorMessage = answer?.message,
                    };
                }

                // Reply-keyboard style: send the button label as a normal message (commands start with /).
                return await SendTextMessageAsync(
                    chatKey,
                    button.Text ?? string.Empty,
                    runtime.AccountId,
                    format: TelegramTextFormat.None,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public Task<TelegramOperationResult> OpenBotAppUrlAsync(
            string chatKey,
            TelegramBotButtonModel button,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.Url))
                return Task.FromResult(TelegramOperationResult.Fail("Button URL is required."));

            TelegramAuthState state = GetAuthState(accountId);
            return Task.FromResult(new TelegramOperationResult
            {
                Success = true,
                AuthState = state,
                Url = button.Url,
            });
        }

        /// <summary>
        /// Theme JSON passed to Mini Apps so telegram-web-app.js can initialize.
        /// </summary>
        private const string BotWebAppThemeParamsJson =
            "{\"bg_color\":\"#1c1c1e\",\"secondary_bg_color\":\"#2c2c2e\",\"text_color\":\"#ffffff\","
            + "\"hint_color\":\"#8e8e93\",\"link_color\":\"#64b5ef\",\"button_color\":\"#2481cc\","
            + "\"button_text_color\":\"#ffffff\",\"header_bg_color\":\"#1c1c1e\",\"accent_text_color\":\"#64b5ef\","
            + "\"section_bg_color\":\"#1c1c1e\",\"section_header_text_color\":\"#8e8e93\","
            + "\"subtitle_text_color\":\"#8e8e93\",\"destructive_text_color\":\"#ff453a\"}";

        /// <summary>
        /// Hosts a Telegram Mini App in an in-app WebView (with JS bridge). Returns true when the
        /// platform presented the app itself; false means the caller may open <see cref="BotWebAppHostRequest.Url"/>
        /// in a fallback browser.
        /// </summary>
        protected virtual Task<bool> PresentBotWebAppAsync(
            BotWebAppHostRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> OpenBotMainAppAsync(
            string chatKey,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey))
                return TelegramOperationResult.Fail("Chat key is required.");
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                if (!runtime.PeerByChatKey.TryGetValue(chatKey, out IPeerInfo peerInfo) || peerInfo is not User user || !user.IsBot)
                    return TelegramOperationResult.Fail("Only bots can open a Mini App.", runtime.AuthState);

                InputPeer peer = user.ToInputPeer();
                var inputUser = new InputUser(user.id, user.access_hash);

                Users_UserFull full = await runtime.Client.Users_GetFullUser(inputUser).ConfigureAwait(false);
                BotMenuButton menu = full?.full_user?.bot_info?.menu_button as BotMenuButton;
                if (menu == null || string.IsNullOrWhiteSpace(menu.url))
                    return TelegramOperationResult.Fail("This bot has no Open app.", runtime.AuthState);

                var themeParams = new DataJSON { data = BotWebAppThemeParamsJson };
                WebViewResult webView = await runtime.Client.Messages_RequestWebView(
                    peer,
                    inputUser,
                    platform: "android",
                    url: menu.url,
                    theme_params: themeParams,
                    from_bot_menu: true,
                    silent: true).ConfigureAwait(false);

                if (webView is not WebViewResultUrl result || string.IsNullOrWhiteSpace(result.url))
                    return TelegramOperationResult.Fail("Bot app did not return a URL.", runtime.AuthState);

                string appLabel = string.IsNullOrWhiteSpace(menu.text) ? "Open" : menu.text;
                lock (runtime.Gate)
                {
                    TelegramChatModel chat = runtime.Chats.FirstOrDefault(c => string.Equals(c.Id, chatKey, StringComparison.Ordinal));
                    if (chat != null)
                    {
                        chat.HasBotApp = true;
                        chat.BotAppLabel = appLabel;
                    }
                }

                bool hasQueryId = result.flags.HasFlag(WebViewResultUrl.Flags.has_query_id);
                long queryId = result.query_id;
                Client client = runtime.Client;
                var hostRequest = new BotWebAppHostRequest
                {
                    Url = result.url,
                    Title = string.IsNullOrWhiteSpace(user.first_name) ? appLabel : user.first_name,
                    QueryId = queryId,
                    HasQueryId = hasQueryId,
                    ProlongAsync = async ct =>
                    {
                        if (!hasQueryId || client == null)
                            return;
                        await client.Messages_ProlongWebView(peer, inputUser, queryId, silent: true)
                            .ConfigureAwait(false);
                    },
                    SendWebAppDataAsync = async (buttonText, data, ct) =>
                    {
                        if (client == null)
                            return;
                        long randomId = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
                        await client.Messages_SendWebViewData(
                            inputUser,
                            randomId,
                            buttonText ?? string.Empty,
                            data ?? string.Empty).ConfigureAwait(false);
                    },
                    InvokeCustomMethodAsync = async (method, paramsJson, ct) =>
                    {
                        if (client == null)
                            throw new InvalidOperationException("Account is not connected.");
                        DataJSON reply = await client.Bots_InvokeWebViewCustomMethod(
                            inputUser,
                            method ?? string.Empty,
                            new DataJSON { data = string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson })
                            .ConfigureAwait(false);
                        return reply?.data ?? "{}";
                    },
                };

                if (await PresentBotWebAppAsync(hostRequest, cancellationToken).ConfigureAwait(false))
                {
                    return new TelegramOperationResult
                    {
                        Success = true,
                        AuthState = runtime.AuthState,
                    };
                }

                return new TelegramOperationResult
                {
                    Success = true,
                    AuthState = runtime.AuthState,
                    Url = result.url,
                };
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, GetAuthState(accountId));
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SetChatsPinnedAsync(
            IReadOnlyList<string> chatKeys,
            bool pinned,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);
            if (chatKeys == null || chatKeys.Count == 0)
                return TelegramOperationResult.Ok(runtime.AuthState);

            try
            {
                foreach (string key in chatKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal))
                {
                    InputPeer peer = ResolveInputPeer(runtime, key);
                    await runtime.Client.Messages_ToggleDialogPin(
                        new InputDialogPeer { peer = peer },
                        pinned: pinned).ConfigureAwait(false);
                }

                return await RefreshChatsAsync(runtime.AccountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task MarkChatReadOnInteractAsync(string chatKey, string accountId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatKey))
                return;
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return;

            try
            {
                // ONLY call ReadHistory from user interaction paths — never from update handlers.
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                await runtime.Client.ReadHistory(peer).ConfigureAwait(false);

                lock (runtime.Gate)
                {
                    TelegramChatModel chat = runtime.Chats.FirstOrDefault(c => string.Equals(c.Id, chatKey, StringComparison.Ordinal));
                    if (chat != null)
                        chat.UnreadCount = 0;
                }
                RaiseChatsChanged();
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            await CancelLoginAsync().ConfigureAwait(false);
            foreach (string id in _runtimes.Keys.ToList())
                await DisconnectAccountAsync(id).ConfigureAwait(false);
        }

        // ---- login helpers ----

        private Client CreateClient(TelegramStoreModel store, TelegramAccountModel account)
        {
            EnsureCompatibleLogging();
            string sessionPath = GetSessionPath(account);
            Directory.CreateDirectory(Path.GetDirectoryName(sessionPath) ?? GetSessionsDirectory());

            // Present as a desktop client so Telegram prefers in-app / SMS codes over
            // Android Firebase SMS (which third-party apps cannot receive without SafetyNet).
            // Do NOT force my.telegram.org "MTProto server address" — WTelegram already picks
            // the right DC (logs show "2>" = DC2, which is 149.154.167.50). Forcing a DC
            // breaks automatic migration when the account lives on another DC.
            string Config(string what) => what switch
            {
                "api_id" => store.ApiId.ToString(),
                "api_hash" => store.ApiHash,
                "session_pathname" => sessionPath,
                "device_model" => "Desktop",
                "system_version" => "Windows 10",
                "app_version" => "4.2.4 x64",
                "system_lang_code" => CultureLang(),
                "lang_code" => CultureLang(),
                "phone_number" => account.Phone,
                // Decline Firebase attestation — we cannot complete SafetyNet here.
                "firebase" => null,
                // QR login 2FA uses ConfigAsync("password") off the UI thread.
                "password" => (_pendingLogin?.IsQrLogin == true) ? WaitForPasswordConfig() : null,
                _ => null,
            };

            return new Client(Config);
        }

        private string WaitForPasswordConfig()
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _passwordPromptTcs = tcs;
            _lastCodeDeliveryHint = "This account has two-step verification. Enter your 2FA password.";
            SetPendingAuth(TelegramAuthState.NeedPassword);
            RaiseAuthChanged();
            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private Task OnPendingLoginOther(IObject obj)
        {
            try
            {
                if (obj is Auth_SentCode sent)
                    HandleSentCode(sent);
            }
            catch (Exception)
            {
            }
            return Task.CompletedTask;
        }

        private void OnWTelegramLog(int level, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // WTelegram: "A verification code has been sent via App" / "Sms" / …
            const string marker = "verification code has been sent via ";
            int idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return;

            string via = message.Substring(idx + marker.Length).Trim();
            bool viaApp = via.IndexOf("App", StringComparison.OrdinalIgnoreCase) >= 0;
            _lastCodeDeliveryHint = viaApp
                ? "Telegram says code was sent via the official app. Keep aNote open on this screen. On another device (or briefly check Telegram notifications / “Telegram” chat), copy the login code, then type it here. Leaving aNote can drop the login UI."
                : "Code sent via " + via + ". Stay on this screen and enter it, or tap Resend / other method.";
            RaiseAuthChanged();
        }

        private void HandleSentCode(Auth_SentCode sent)
        {
            _lastCodeDeliveryHint = DescribeSentCode(sent);
            RaiseAuthChanged();
        }

        private static string DescribeSentCode(Auth_SentCode sent)
        {
            if (sent?.type == null)
            {
                return "Stay on this screen. Check the official Telegram app (notifications or “Telegram” chat) for a login code.";
            }

            Auth_SentCodeType type = sent.type;
            string primary;
            if (type is Auth_SentCodeTypeApp)
            {
                primary = "Telegram sent an in-app code (not SMS — third-party apps cannot get SMS since 2023). "
                    + "Open official Telegram on a device already logged into this number → “Telegram” service chat / login notification. "
                    + "Or cancel and use Login with QR instead.";
            }
            else if (type is Auth_SentCodeTypeFirebaseSms)
                primary = "Telegram requested Firebase SMS (not supported in aNote). Tap Resend / other method.";
            else if (type is Auth_SentCodeTypeFragmentSms)
                primary = "Code sent via Fragment SMS.";
            else if (type is Auth_SentCodeTypeEmailCode email)
                primary = "Code sent to email " + (email.email_pattern ?? string.Empty) + ".";
            else if (type is Auth_SentCodeTypeSetUpEmailRequired)
                primary = "Telegram requires email setup for this number.";
            else if (type is Auth_SentCodeTypeMissedCall)
                primary = "Missed call: the code is the last digits of the caller number.";
            else if (type is Auth_SentCodeTypeFlashCall)
                primary = "Flash call: the code is in the caller number pattern.";
            else if (type is Auth_SentCodeTypeCall)
                primary = "Telegram will call you and dictate the code.";
            else if (type is Auth_SentCodeTypeSms || type is Auth_SentCodeTypeSmsWord || type is Auth_SentCodeTypeSmsPhrase)
                primary = "Code sent by SMS to your phone number.";
            else
                primary = "Code sent via " + type.GetType().Name.Replace("Auth_SentCodeType", string.Empty) + ".";

            if (sent.next_type != null)
                primary += " Next method available: " + sent.next_type + " (Resend).";

            return primary;
        }

        private static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null;

            string trimmed = phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (trimmed.StartsWith("00", StringComparison.Ordinal))
                trimmed = "+" + trimmed.Substring(2);
            return trimmed;
        }

        private static bool IsRecoverableLoginError(RpcException ex)
        {
            if (ex == null)
                return false;
            string msg = ex.Message ?? string.Empty;
            return msg.IndexOf("PHONE_CODE", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("PHONE_NUMBER", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("CODE_INVALID", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("CODE_EMPTY", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("CODE_EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("FLOOD", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeLoginRpcError(RpcException ex)
        {
            string msg = ex?.Message ?? "Login error";
            if (msg.IndexOf("PHONE_CODE_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Wrong verification code. Open the official Telegram app — the code is usually delivered there, not by SMS/mail.";
            if (msg.IndexOf("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Code expired. Tap Resend to get a new one.";
            if (msg.IndexOf("PHONE_NUMBER_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Invalid phone number. Use international format, e.g. +15551234567.";
            if (msg.IndexOf("FLOOD", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Too many attempts. Wait a few minutes and try again.";
            return msg;
        }

        private static string CultureLang()
        {
            try
            {
                return System.Globalization.CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName ?? "en";
            }
            catch
            {
                return "en";
            }
        }

        private void AttachUpdateManager(AccountRuntime runtime)
        {
            string statePath = Path.Combine(GetCacheDirectory(), "updates_" + runtime.AccountId + ".state");
            runtime.UpdateManager = runtime.Client.WithUpdateManager(
                update => OnAccountUpdateAsync(runtime, update),
                statePath);
        }

        private async Task OnAccountUpdateAsync(AccountRuntime runtime, Update update)
        {
            try
            {
                Message msg = null;
                if (update is UpdateNewMessage unm && unm.message is Message m1)
                    msg = m1;
                else if (update is UpdateNewChannelMessage uncm && uncm.message is Message m2)
                    msg = m2;

                if (msg != null)
                {
                    HandleIncomingMessageUpdate(runtime, msg);
                }
                else if (update is UpdateNotifySettings uns)
                {
                    ApplyNotifySettingsUpdate(runtime, uns);
                }
                else if (update is UpdateEditMessage or UpdateEditChannelMessage
                    or UpdateDeleteMessages or UpdateDeleteChannelMessages
                    or UpdateReadHistoryInbox or UpdateReadHistoryOutbox
                    or UpdateReadChannelInbox)
                {
                    // Lightweight refresh of chat list previews; do NOT mark read here.
                    _ = RefreshChatsAsync(runtime.AccountId);
                }
            }
            catch (Exception)
            {
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private void HandleIncomingMessageUpdate(AccountRuntime runtime, Message msg)
        {
            string chatKey = BuildChatKey(msg.Peer);
            TelegramMessageModel model = MapMessage(runtime, msg, chatKey);
            bool isOutgoing = msg.flags.HasFlag(Message.Flags.out_);

            bool isMention = model.IsMention
                || (msg.entities?.Any(e => e is MessageEntityMention or MessageEntityMentionName) ?? false);
            bool isReplyToUs = false;
            if (msg.reply_to is MessageReplyHeader reply && reply.reply_to_msg_id != 0)
            {
                lock (runtime.Gate)
                {
                    isReplyToUs = runtime.Messages.Any(m =>
                        m.Id == reply.reply_to_msg_id && m.IsOutgoing
                        && string.Equals(m.ChatKey, chatKey, StringComparison.Ordinal));
                }
            }

            bool isOpen;
            bool isMuted;
            string chatTitle;
            lock (runtime.Gate)
            {
                isOpen = string.Equals(runtime.OpenChatKey, chatKey, StringComparison.Ordinal);
                if (isOpen)
                {
                    runtime.Messages.RemoveAll(m => m.Id == model.Id);
                    runtime.Messages.Add(model);
                    runtime.Messages.Sort((a, b) =>
                    {
                        int cmp = b.Date.CompareTo(a.Date);
                        return cmp != 0 ? cmp : b.Id.CompareTo(a.Id);
                    });
                }

                TelegramChatModel chat = runtime.Chats.FirstOrDefault(c => string.Equals(c.Id, chatKey, StringComparison.Ordinal));
                if (chat != null)
                {
                    chat.LastMessageAt = model.Date;
                    chat.LastMessagePreview = string.IsNullOrEmpty(model.Text) ? model.MediaLabel : model.Text;
                    if (!isOutgoing)
                        chat.UnreadCount = Math.Max(0, chat.UnreadCount) + 1;
                    isMuted = chat.IsMuted;
                    chatTitle = chat.Title;
                }
                else
                {
                    isMuted = false;
                    chatTitle = model.FromName;
                }
            }

            if (!isOutgoing)
            {
                if (isMention || isReplyToUs)
                    MentionOrReplyReceived?.Invoke(model);

                // Push for every incoming message unless the chat is open or muted.
                if (!isOpen && !isMuted)
                {
                    string title = string.IsNullOrWhiteSpace(chatTitle)
                        ? (string.IsNullOrEmpty(model.FromName) ? "Telegram" : model.FromName)
                        : chatTitle;
                    string body = string.IsNullOrEmpty(model.Text) ? model.MediaLabel : model.Text;
                    if (string.IsNullOrEmpty(body))
                        body = "New message";
                    bool silent = msg.flags.HasFlag(Message.Flags.silent);
                    ShowIncomingMessageNotification(title, body, chatKey, silent);
                }
            }

            RaiseChatsChanged();
            if (isOpen)
                RaiseMessagesChanged(chatKey);
        }

        // ---- privacy ----

        private static async Task AssertOfflineAsync(Client client)
        {
            if (client == null)
                return;
            try
            {
                // Go offline immediately after connect / login. Never set online.
                // Short timeout — must never block the UI/timer thread on bad networks.
                await WithTimeout(
                    client.Account_UpdateStatus(offline: true),
                    TimeSpan.FromSeconds(5),
                    "offline status timeout",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private void StartOfflineTimer(AccountRuntime runtime)
        {
            StopOfflineTimer(runtime);
            runtime.OfflineTimer = new Timer(async _ =>
            {
                try
                {
                    if (runtime.Client != null)
                        await AssertOfflineAsync(runtime.Client).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }, null, OfflineRefreshInterval, OfflineRefreshInterval);
        }

        private static void StopOfflineTimer(AccountRuntime runtime)
        {
            Timer timer = Interlocked.Exchange(ref runtime.OfflineTimer, null);
            timer?.Dispose();
        }

        // ---- media send ----

        private async Task<TelegramOperationResult> SendUploadedMediaAsync(
            string chatKey,
            byte[] content,
            string fileName,
            string accountId,
            string caption,
            bool asPhoto,
            int replyToMsgId,
            bool silent,
            CancellationToken cancellationToken)
        {
            if (content == null || content.Length == 0)
                return TelegramOperationResult.Fail("File content is required.");
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = asPhoto ? "photo.jpg" : "file.bin";
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);

            try
            {
                InputPeer peer = ResolveInputPeer(runtime, chatKey);
                await using var stream = new MemoryStream(content, writable: false);
                InputFileBase uploaded = await runtime.Client.UploadFileAsync(stream, fileName).ConfigureAwait(false);

                InputMedia media;
                if (asPhoto)
                {
                    media = new InputMediaUploadedPhoto { file = uploaded };
                }
                else
                {
                    media = new InputMediaUploadedDocument
                    {
                        file = uploaded,
                        mime_type = GuessMime(fileName),
                        attributes = new DocumentAttribute[]
                        {
                            new DocumentAttributeFilename { file_name = fileName },
                        },
                    };
                }

                UpdatesBase updates = await runtime.Client.Messages_SendMedia(
                    peer,
                    media,
                    caption ?? string.Empty,
                    NewSendRandomId(),
                    reply_to: BuildReplyTo(replyToMsgId),
                    silent: silent).ConfigureAwait(false);

                Message sent = ExtractSentMessage(updates, caption ?? string.Empty);
                if (sent != null)
                    AppendMessage(runtime, chatKey, MapMessage(runtime, sent, chatKey));

                await MarkChatReadOnInteractAsync(chatKey, runtime.AccountId, cancellationToken).ConfigureAwait(false);
                _ = RefreshChatsAsync(runtime.AccountId, cancellationToken);
                return TelegramOperationResult.Ok(runtime.AuthState);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        private static InputReplyTo BuildReplyTo(int replyToMsgId)
        {
            if (replyToMsgId <= 0)
                return null;
            return new InputReplyToMessage { reply_to_msg_id = replyToMsgId };
        }

        private static long NewSendRandomId()
            => BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);

        private static Message ExtractSentMessage(UpdatesBase updates, string fallbackText)
        {
            if (updates == null)
                return null;

            foreach (Update upd in updates.UpdateList ?? Array.Empty<Update>())
            {
                if (upd is UpdateNewMessage { message: Message m })
                    return m;
                if (upd is UpdateNewChannelMessage { message: Message m2 })
                    return m2;
            }

            if (updates is UpdateShortSentMessage shortSent)
            {
                var msg = new Message
                {
                    id = shortSent.id,
                    date = shortSent.date,
                    message = fallbackText ?? string.Empty,
                    entities = shortSent.entities,
                    media = shortSent.media,
                    flags = Message.Flags.out_,
                };
                return msg;
            }

            return null;
        }

        // ---- mapping ----

        private TelegramMessageModel MapMessage(AccountRuntime runtime, Message msg, string chatKey)
        {
            var model = new TelegramMessageModel
            {
                Id = msg.id,
                ChatKey = chatKey,
                Text = msg.message ?? string.Empty,
                IsOutgoing = msg.flags.HasFlag(Message.Flags.out_),
                Date = msg.date,
                IsMention = msg.flags.HasFlag(Message.Flags.mentioned),
            };

            if (msg.from_id != null)
            {
                model.FromId = msg.from_id.ID;
                model.FromName = ResolvePeerDisplayName(runtime, msg.from_id);
            }
            else if (msg.Peer != null)
            {
                model.FromId = msg.Peer.ID;
                model.FromName = ResolvePeerDisplayName(runtime, msg.Peer);
            }

            if (msg.reply_to is MessageReplyHeader replyHdr && replyHdr.flags.HasFlag(MessageReplyHeader.Flags.has_reply_to_msg_id))
            {
                model.ReplyToMsgId = replyHdr.reply_to_msg_id;
                if (!string.IsNullOrEmpty(replyHdr.quote_text))
                    model.ReplyPreview = TelegramTextUtils.ReplaceEmojisWithSymbolic(replyHdr.quote_text);
            }

            var spans = new List<TelegramTextEntitySpan>();
            if (msg.entities != null)
            {
                foreach (MessageEntity entity in msg.entities)
                {
                    TelegramTextEntitySpan span = FromTlEntity(entity);
                    if (span != null)
                        spans.Add(span);
                    if (entity is MessageEntityMention or MessageEntityMentionName)
                        model.IsMention = true;
                }
            }

            model.FormattedText = TelegramTextUtils.FormatForDisplay(model.Text, spans);
            ApplyMedia(runtime, model, msg);
            model.Buttons = ParseButtons(msg);
            if (msg.flags.HasFlag(Message.Flags.has_reactions) && msg.reactions != null)
                ApplyReactions(model, msg.reactions);
            return model;
        }

        private static void ApplyReactions(TelegramMessageModel model, MessageReactions reactions)
        {
            if (model == null || reactions?.results == null)
            {
                if (model != null)
                {
                    model.ReactionCount = 0;
                    model.HasMyReaction = false;
                }
                return;
            }

            int total = 0;
            bool mine = false;
            foreach (ReactionCount rc in reactions.results)
            {
                if (rc == null)
                    continue;
                total += Math.Max(0, rc.count);
                if (rc.flags.HasFlag(ReactionCount.Flags.has_chosen_order))
                    mine = true;
            }
            model.ReactionCount = total;
            model.HasMyReaction = mine;
        }

        private void ApplyMedia(AccountRuntime runtime, TelegramMessageModel model, Message msg)
        {
            model.MediaKind = TelegramMediaKinds.None;
            model.MediaLabel = string.Empty;
            model.MediaFileName = string.Empty;
            model.HasMedia = false;

            if (msg.media == null)
                return;

            switch (msg.media)
            {
                case MessageMediaPhoto photoMedia:
                    model.MediaKind = TelegramMediaKinds.Photo;
                    model.MediaLabel = "[Photo]";
                    model.MediaFileName = "photo_" + msg.id + ".jpg";
                    model.HasMedia = photoMedia.photo is Photo;
                    if (photoMedia.photo is Photo p)
                        runtime.MediaCache[MediaCacheKey(runtime.AccountId, model.ChatKey, model.Id)] = p;
                    break;

                case MessageMediaDocument docMedia when docMedia.document is Document doc:
                    ClassifyDocument(doc, docMedia, model);
                    if (model.HasMedia)
                        runtime.MediaCache[MediaCacheKey(runtime.AccountId, model.ChatKey, model.Id)] = doc;
                    break;

                case MessageMediaStory:
                    // NEVER call Stories_* read/view APIs — show label only.
                    model.MediaKind = TelegramMediaKinds.Story;
                    model.MediaLabel = "[Story]";
                    model.HasMedia = false;
                    break;

                default:
                    model.MediaKind = TelegramMediaKinds.Other;
                    model.MediaLabel = "[Media]";
                    model.HasMedia = false;
                    break;
            }

            if (string.IsNullOrEmpty(model.Text) && !string.IsNullOrEmpty(model.MediaLabel))
            {
                model.Text = model.MediaLabel;
                model.FormattedText = TelegramTextUtils.EscapeHtml(model.MediaLabel);
            }
        }

        private static void ClassifyDocument(Document doc, MessageMediaDocument docMedia, TelegramMessageModel model)
        {
            bool isSticker = doc.attributes?.OfType<DocumentAttributeSticker>().Any() == true
                || doc.attributes?.OfType<DocumentAttributeCustomEmoji>().Any() == true;
            bool isAnimated = doc.attributes?.OfType<DocumentAttributeAnimated>().Any() == true;
            bool isVideo = docMedia.flags.HasFlag(MessageMediaDocument.Flags.video)
                || doc.attributes?.OfType<DocumentAttributeVideo>().Any() == true;
            bool isVoice = docMedia.flags.HasFlag(MessageMediaDocument.Flags.voice)
                || doc.attributes?.OfType<DocumentAttributeAudio>().Any(a => a.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true;
            bool isAudio = doc.attributes?.OfType<DocumentAttributeAudio>().Any() == true;
            bool isRound = docMedia.flags.HasFlag(MessageMediaDocument.Flags.round);

            string fileName = !string.IsNullOrEmpty(doc.Filename)
                ? doc.Filename
                : doc.attributes?.OfType<DocumentAttributeFilename>().Select(a => a.file_name).FirstOrDefault();

            if (isSticker)
            {
                // Stickers: label only, no download needed.
                model.MediaKind = TelegramMediaKinds.Sticker;
                model.MediaLabel = "[Sticker]";
                model.HasMedia = false;
                return;
            }

            if (isAnimated && !isVideo)
            {
                model.MediaKind = TelegramMediaKinds.Gif;
                model.MediaLabel = "[GIF]";
                model.MediaFileName = string.IsNullOrEmpty(fileName) ? "animation_" + doc.id + ".mp4" : fileName;
                model.HasMedia = true;
                return;
            }

            if (isVoice || isRound)
            {
                model.MediaKind = TelegramMediaKinds.Voice;
                model.MediaLabel = "[Voice]";
                model.MediaFileName = string.IsNullOrEmpty(fileName) ? "voice_" + doc.id + ".ogg" : fileName;
                model.HasMedia = true;
                return;
            }

            if (isVideo)
            {
                model.MediaKind = TelegramMediaKinds.Video;
                model.MediaLabel = "[Video]";
                model.MediaFileName = string.IsNullOrEmpty(fileName) ? "video_" + doc.id + ".mp4" : fileName;
                model.HasMedia = true;
                return;
            }

            if (isAudio)
            {
                model.MediaKind = TelegramMediaKinds.Audio;
                model.MediaLabel = "[Audio]";
                model.MediaFileName = string.IsNullOrEmpty(fileName) ? "audio_" + doc.id + ".mp3" : fileName;
                model.HasMedia = true;
                return;
            }

            model.MediaKind = TelegramMediaKinds.Document;
            model.MediaLabel = "[Document]";
            model.MediaFileName = string.IsNullOrEmpty(fileName) ? "file_" + doc.id : fileName;
            model.HasMedia = true;
        }

        private static List<TelegramBotButtonModel> ParseButtons(Message msg)
        {
            var result = new List<TelegramBotButtonModel>();
            if (msg.reply_markup == null)
                return result;

            if (msg.reply_markup is ReplyInlineMarkup inline && inline.rows != null)
            {
                foreach (KeyboardInlineButtonRow row in inline.rows)
                {
                    if (row?.buttons == null)
                        continue;
                    foreach (KeyboardInlineButton btn in row.buttons)
                    {
                        if (btn == null)
                            continue;
                        var model = new TelegramBotButtonModel
                        {
                            Text = btn.text ?? string.Empty,
                            MessageId = msg.id,
                        };
                        switch (btn.type)
                        {
                            case InlineButtonTypeCallback cb:
                                model.Data = cb.data != null ? Encoding.UTF8.GetString(cb.data) : string.Empty;
                                break;
                            case InlineButtonTypeUrl url:
                                model.Url = url.url ?? string.Empty;
                                break;
                            case InlineButtonTypeWebView web:
                                model.Url = web.url ?? string.Empty;
                                model.IsWebApp = true;
                                break;
                        }
                        result.Add(model);
                    }
                }
            }
            else if (msg.reply_markup is ReplyKeyboardMarkup keyboard && keyboard.rows != null)
            {
                foreach (KeyboardButtonRow row in keyboard.rows)
                {
                    if (row?.buttons == null)
                        continue;
                    foreach (KeyboardButton btn in row.buttons)
                    {
                        if (btn == null)
                            continue;
                        var model = new TelegramBotButtonModel
                        {
                            Text = btn.text ?? string.Empty,
                            MessageId = msg.id,
                        };
                        if (btn.type is ButtonTypeSimpleWebView swv)
                        {
                            model.Url = swv.url ?? string.Empty;
                            model.IsWebApp = true;
                        }
                        result.Add(model);
                    }
                }
            }

            return result;
        }

        private static TelegramChatModel MapChat(Dialog dialog, IPeerInfo peerInfo, string chatKey, bool archived)
        {
            var chat = new TelegramChatModel
            {
                Id = chatKey,
                UnreadCount = dialog.unread_count,
                PeerId = dialog.Peer?.ID ?? 0,
                IsArchived = archived
                    || (dialog.flags.HasFlag(Dialog.Flags.has_folder_id) && dialog.folder_id == 1),
                IsMuted = IsDialogMuted(dialog.notify_settings),
                IsPinned = dialog.flags.HasFlag(Dialog.Flags.pinned),
            };

            switch (peerInfo)
            {
                case User user:
                    chat.Title = FormatUserName(user);
                    chat.IsBot = user.IsBot;
                    chat.IsGroup = false;
                    chat.IsChannel = false;
                    chat.PeerType = TelegramPeerTypes.User;
                    break;
                case Chat basicChat:
                    chat.Title = basicChat.Title ?? basicChat.title ?? string.Empty;
                    chat.IsGroup = true;
                    chat.IsChannel = false;
                    chat.PeerType = TelegramPeerTypes.Chat;
                    break;
                case Channel channel:
                    chat.Title = channel.Title ?? string.Empty;
                    chat.IsChannel = channel.IsChannel;
                    chat.IsGroup = channel.IsGroup;
                    chat.PeerType = TelegramPeerTypes.Channel;
                    break;
                default:
                    chat.Title = peerInfo?.ToString() ?? chatKey;
                    chat.PeerType = TelegramPeerTypes.User;
                    break;
            }

            return chat;
        }

        private static bool IsDialogMuted(PeerNotifySettings settings)
        {
            if (settings == null)
                return false;
            if (!settings.flags.HasFlag(PeerNotifySettings.Flags.has_mute_until))
                return settings.flags.HasFlag(PeerNotifySettings.Flags.has_silent) && settings.silent;
            return settings.mute_until > DateTime.UtcNow;
        }

        // Mute-until far enough to act as “forever” (Telegram’s classic max unix timestamp).
        private static readonly DateTime MuteForeverUntil = new DateTime(2038, 1, 19, 3, 14, 7, DateTimeKind.Utc);

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SetChatsArchivedAsync(
            IReadOnlyList<string> chatKeys,
            bool archived,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);
            if (chatKeys == null || chatKeys.Count == 0)
                return TelegramOperationResult.Ok(runtime.AuthState);

            try
            {
                int folderId = archived ? 1 : 0;
                var peers = new List<InputFolderPeer>();
                foreach (string key in chatKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal))
                {
                    InputPeer peer = ResolveInputPeer(runtime, key);
                    peers.Add(new InputFolderPeer { peer = peer, folder_id = folderId });
                }

                if (peers.Count > 0)
                    await runtime.Client.Folders_EditPeerFolders(peers.ToArray()).ConfigureAwait(false);

                return await RefreshChatsAsync(runtime.AccountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> DeleteChatsForMeAsync(
            IReadOnlyList<string> chatKeys,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);
            if (chatKeys == null || chatKeys.Count == 0)
                return TelegramOperationResult.Ok(runtime.AuthState);

            try
            {
                foreach (string key in chatKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal))
                {
                    InputPeer peer = ResolveInputPeer(runtime, key);
                    if (peer is InputPeerChannel channelPeer)
                    {
                        var inputChannel = new InputChannel(channelPeer.channel_id, channelPeer.access_hash);
                        await runtime.Client.Channels_DeleteHistory(inputChannel, max_id: 0, for_everyone: false)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await runtime.Client.Messages_DeleteHistory(
                            peer,
                            max_id: 0,
                            just_clear: false,
                            revoke: false).ConfigureAwait(false);
                    }
                }

                return await RefreshChatsAsync(runtime.AccountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> SetChatsMutedAsync(
            IReadOnlyList<string> chatKeys,
            bool muted,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);
            if (chatKeys == null || chatKeys.Count == 0)
                return TelegramOperationResult.Ok(runtime.AuthState);

            try
            {
                // TL optional fields are only sent when their has_* flag is set.
                var settings = new InputPeerNotifySettings
                {
                    flags = InputPeerNotifySettings.Flags.has_mute_until,
                    mute_until = muted
                        ? MuteForeverUntil
                        : DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc),
                };

                var appliedKeys = new List<string>();
                foreach (string key in chatKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal))
                {
                    InputPeer peer = ResolveInputPeer(runtime, key);
                    await runtime.Client.Account_UpdateNotifySettings(
                        new InputNotifyPeer { peer = peer },
                        settings).ConfigureAwait(false);
                    appliedKeys.Add(key);
                }

                // Apply locally immediately so in-app notifications stop before dialogs refresh.
                if (appliedKeys.Count > 0)
                {
                    lock (runtime.Gate)
                    {
                        foreach (string key in appliedKeys)
                        {
                            TelegramChatModel chat = runtime.Chats.FirstOrDefault(c =>
                                string.Equals(c.Id, key, StringComparison.Ordinal));
                            if (chat != null)
                                chat.IsMuted = muted;
                        }
                    }
                    RaiseChatsChanged();
                }

                return await RefreshChatsAsync(runtime.AccountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        private void ApplyNotifySettingsUpdate(AccountRuntime runtime, UpdateNotifySettings update)
        {
            if (runtime == null || update?.notify_settings == null)
                return;

            Peer peer = null;
            if (update.peer is NotifyPeer notifyPeer)
                peer = notifyPeer.peer;
            else if (update.peer is NotifyForumTopic notifyTopic)
                peer = notifyTopic.peer;

            if (peer == null)
                return;

            string chatKey = BuildChatKey(peer);
            bool isMuted = IsDialogMuted(update.notify_settings);
            lock (runtime.Gate)
            {
                TelegramChatModel chat = runtime.Chats.FirstOrDefault(c =>
                    string.Equals(c.Id, chatKey, StringComparison.Ordinal));
                if (chat == null || chat.IsMuted == isMuted)
                    return;
                chat.IsMuted = isMuted;
            }

            RaiseChatsChanged();
        }

        /// <inheritdoc/>
        public async Task<TelegramOperationResult> MarkChatsReadAsync(
            IReadOnlyList<string> chatKeys,
            string accountId = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetRuntime(accountId, out AccountRuntime runtime) || runtime.Client == null)
                return TelegramOperationResult.Fail("Account is not connected.", TelegramAuthState.LoggedOut);
            if (chatKeys == null || chatKeys.Count == 0)
                return TelegramOperationResult.Ok(runtime.AuthState);

            try
            {
                foreach (string key in chatKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal))
                    await MarkChatReadOnInteractAsync(key, runtime.AccountId, cancellationToken).ConfigureAwait(false);

                return await RefreshChatsAsync(runtime.AccountId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TelegramOperationResult.Fail(ex.Message, runtime.AuthState);
            }
        }

        private static string BuildMessagePreview(Message msg)
        {
            if (!string.IsNullOrEmpty(msg.message))
                return TelegramTextUtils.ReplaceEmojisWithSymbolic(msg.message);

            return msg.media switch
            {
                MessageMediaPhoto => "[Photo]",
                MessageMediaStory => "[Story]",
                MessageMediaDocument doc when doc.document is Document d =>
                    d.attributes?.OfType<DocumentAttributeSticker>().Any() == true ? "[Sticker]"
                    : doc.flags.HasFlag(MessageMediaDocument.Flags.voice) ? "[Voice]"
                    : doc.flags.HasFlag(MessageMediaDocument.Flags.video) ? "[Video]"
                    : d.attributes?.OfType<DocumentAttributeAnimated>().Any() == true ? "[GIF]"
                    : d.attributes?.OfType<DocumentAttributeAudio>().Any() == true ? "[Audio]"
                    : "[Document]",
                _ => "[Media]",
            };
        }

        private string ResolvePeerDisplayName(AccountRuntime runtime, Peer peer)
        {
            if (peer == null)
                return string.Empty;
            if (runtime.Users.TryGetValue(peer.ID, out User user))
                return FormatUserName(user);
            if (runtime.ChatsById.TryGetValue(peer.ID, out ChatBase chat))
                return chat.Title ?? chat.ToString() ?? string.Empty;
            return peer.ID.ToString();
        }

        private static string FormatUserName(User user)
        {
            if (user == null)
                return string.Empty;
            string name = ((user.first_name ?? string.Empty) + " " + (user.last_name ?? string.Empty)).Trim();
            if (!string.IsNullOrEmpty(name))
                return name;
            if (!string.IsNullOrEmpty(user.MainUsername))
                return "@" + user.MainUsername;
            if (!string.IsNullOrEmpty(user.username))
                return "@" + user.username;
            return user.id.ToString();
        }

        private static void ApplyUserToAccount(TelegramAccountModel account, User me)
        {
            if (account == null || me == null)
                return;
            account.UserId = me.id;
            account.DisplayName = FormatUserName(me);
            account.Username = me.MainUsername ?? me.username ?? string.Empty;
            if (!string.IsNullOrEmpty(me.phone))
                account.Phone = me.phone.StartsWith("+", StringComparison.Ordinal) ? me.phone : "+" + me.phone;
            if (string.IsNullOrEmpty(account.SessionFileName))
                account.SessionFileName = accountSessionFileName(account.Id);
        }

        private static string accountSessionFileName(string accountId) => accountId + ".session";

        // ---- peers / paths ----

        private InputPeer ResolveInputPeer(AccountRuntime runtime, string chatKey)
        {
            if (runtime.PeerByChatKey.TryGetValue(chatKey, out IPeerInfo info))
                return info.ToInputPeer();

            ParseChatKey(chatKey, out string peerType, out long peerId);
            if (string.Equals(peerType, TelegramPeerTypes.User, StringComparison.OrdinalIgnoreCase)
                && runtime.Users.TryGetValue(peerId, out User user))
                return user.ToInputPeer();
            if (runtime.ChatsById.TryGetValue(peerId, out ChatBase chat))
                return chat.ToInputPeer();

            throw new InvalidOperationException("Unknown chat peer: " + chatKey + ". Refresh chats first.");
        }

        private static string BuildChatKey(Peer peer)
        {
            return peer switch
            {
                PeerUser u => TelegramPeerTypes.User + ":" + u.user_id,
                PeerChat c => TelegramPeerTypes.Chat + ":" + c.chat_id,
                PeerChannel ch => TelegramPeerTypes.Channel + ":" + ch.channel_id,
                _ => "unknown:" + (peer?.ID ?? 0),
            };
        }

        private static void ParseChatKey(string chatKey, out string peerType, out long peerId)
        {
            peerType = TelegramPeerTypes.User;
            peerId = 0;
            if (string.IsNullOrEmpty(chatKey))
                return;
            int idx = chatKey.IndexOf(':');
            if (idx <= 0)
                return;
            peerType = chatKey.Substring(0, idx);
            long.TryParse(chatKey.Substring(idx + 1), out peerId);
        }

        private static void CollectPeersFromMessages(AccountRuntime runtime, Messages_MessagesBase history)
        {
            switch (history)
            {
                case Messages_Messages mm:
                    // Also covers Messages_MessagesSlice (subclass of Messages_Messages).
                    CollectPeers(runtime, mm.users, mm.chats);
                    break;
                case Messages_ChannelMessages channel:
                    CollectPeers(runtime, channel.users, channel.chats);
                    break;
            }
        }

        private static void CollectPeers(
            AccountRuntime runtime,
            Dictionary<long, User> users,
            Dictionary<long, ChatBase> chats)
        {
            if (users != null)
            {
                foreach (var kv in users)
                {
                    runtime.Users[kv.Key] = kv.Value;
                    if (kv.Value != null)
                        runtime.PeerByChatKey[TelegramPeerTypes.User + ":" + kv.Key] = kv.Value;
                }
            }
            if (chats != null)
            {
                foreach (var kv in chats)
                {
                    runtime.ChatsById[kv.Key] = kv.Value;
                    if (kv.Value != null && kv.Value.IsActive)
                        runtime.PeerByChatKey[BuildChatKeyFromChat(kv.Value)] = kv.Value;
                }
            }
        }

        private static string BuildChatKeyFromChat(ChatBase chat)
        {
            return chat switch
            {
                Channel channel => TelegramPeerTypes.Channel + ":" + channel.id,
                Chat basic => TelegramPeerTypes.Chat + ":" + basic.id,
                _ => TelegramPeerTypes.Chat + ":" + (chat?.ID ?? 0),
            };
        }

        private string GetCacheDirectory() => Path.Combine(GetExternalTgRoot(), "cache");

        private string GetSessionsDirectory() => Path.Combine(GetCacheDirectory(), "sessions");

        private string GetSessionPath(TelegramAccountModel account)
        {
            string fileName = string.IsNullOrWhiteSpace(account.SessionFileName)
                ? accountSessionFileName(account.Id)
                : account.SessionFileName;
            return Path.Combine(GetSessionsDirectory(), fileName);
        }

        // ---- entities ----

        private static MessageEntity[] ToTlEntities(List<TelegramTextEntitySpan> spans)
        {
            if (spans == null || spans.Count == 0)
                return null;

            var list = new List<MessageEntity>();
            foreach (TelegramTextEntitySpan span in spans)
            {
                MessageEntity entity = span.Kind switch
                {
                    TelegramTextFormat.Bold => new MessageEntityBold { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Italic => new MessageEntityItalic { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Code => new MessageEntityCode { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Pre => new MessageEntityPre { offset = span.Offset, length = span.Length, language = span.Language ?? string.Empty },
                    TelegramTextFormat.Underline => new MessageEntityUnderline { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Strike => new MessageEntityStrike { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Spoiler => new MessageEntitySpoiler { offset = span.Offset, length = span.Length },
                    TelegramTextFormat.Link => new MessageEntityTextUrl { offset = span.Offset, length = span.Length, url = span.Url ?? string.Empty },
                    _ => null,
                };
                if (entity != null)
                    list.Add(entity);
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static TelegramTextEntitySpan FromTlEntity(MessageEntity entity)
        {
            return entity switch
            {
                MessageEntityBold => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Bold },
                MessageEntityItalic => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Italic },
                MessageEntityCode => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Code },
                MessageEntityPre pre => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Pre, Language = pre.language },
                MessageEntityUnderline => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Underline },
                MessageEntityStrike => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Strike },
                MessageEntitySpoiler => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Spoiler },
                MessageEntityTextUrl url => new TelegramTextEntitySpan { Offset = entity.offset, Length = entity.length, Kind = TelegramTextFormat.Link, Url = url.url },
                _ => null,
            };
        }

        // ---- misc helpers ----

        private void AppendMessage(AccountRuntime runtime, string chatKey, TelegramMessageModel model)
        {
            lock (runtime.Gate)
            {
                if (string.Equals(runtime.OpenChatKey, chatKey, StringComparison.Ordinal))
                {
                    runtime.Messages.RemoveAll(m => m.Id == model.Id);
                    runtime.Messages.Insert(0, model);
                }
            }
            RaiseMessagesChanged(chatKey);
        }

        private static object ExtractDownloadableMedia(MessageMedia media)
        {
            return media switch
            {
                MessageMediaPhoto photo when photo.photo is Photo p => p,
                MessageMediaDocument doc when doc.document is Document d => d,
                _ => null,
            };
        }

        private static string MediaCacheKey(TelegramMediaRef mediaRef)
            => MediaCacheKey(mediaRef.AccountId, mediaRef.ChatKey, mediaRef.MessageId);

        private static string MediaCacheKey(string accountId, string chatKey, int messageId)
            => (accountId ?? string.Empty) + "|" + (chatKey ?? string.Empty) + "|" + messageId;

        private static string BuildDefaultFileName(TelegramMediaRef mediaRef)
        {
            string ext = mediaRef.MediaKind switch
            {
                TelegramMediaKinds.Photo => ".jpg",
                TelegramMediaKinds.Video => ".mp4",
                TelegramMediaKinds.Gif => ".mp4",
                TelegramMediaKinds.Audio => ".mp3",
                TelegramMediaKinds.Voice => ".ogg",
                _ => ".bin",
            };
            return "tg_" + mediaRef.MessageId + ext;
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            return string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName;
        }

        private static string MakeUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(candidate))
                    return candidate;
            }
            return Path.Combine(dir, name + "_" + Guid.NewGuid().ToString("N") + ext);
        }

        private static string GuessMime(string fileName)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".mp4" => "video/mp4",
                ".mp3" => "audio/mpeg",
                ".ogg" => "audio/ogg",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".zip" => "application/zip",
                _ => "application/octet-stream",
            };
        }

        private bool TryGetRuntime(string accountId, out AccountRuntime runtime)
        {
            if (string.IsNullOrEmpty(accountId))
                accountId = LoadStore().ActiveAccountId;
            if (string.IsNullOrEmpty(accountId))
            {
                runtime = null;
                return false;
            }
            return _runtimes.TryGetValue(accountId, out runtime);
        }

        private void SetPendingAuth(TelegramAuthState state)
        {
            _pendingAuthState = state;
            RaiseAuthChanged();
        }

        private void RaiseChatsChanged() => ChatsChanged?.Invoke();

        private void RaiseMessagesChanged(string chatKey) => MessagesChanged?.Invoke(chatKey);

        private void RaiseAuthChanged() => AuthChanged?.Invoke();

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TelegramServiceBase));
        }

        private string ProtectSecret(string value)
        {
            if (_dataProtectionService == null || string.IsNullOrEmpty(value))
                return null;
            return _dataProtectionService.Protect(Encoding.UTF8.GetBytes(value));
        }

        private void UnprotectSecret(string encrypted, Action<string> assign)
        {
            if (string.IsNullOrEmpty(encrypted) || _dataProtectionService == null)
                return;
            try
            {
                byte[] raw = _dataProtectionService.Unprotect(encrypted);
                if (raw != null)
                    assign(Encoding.UTF8.GetString(raw));
            }
            catch (Exception)
            {
            }
        }

        private sealed class LoginContext
        {
            public TelegramAccountModel Account;
            public Client Client;
            public TelegramStoreModel Store;
            public string PhoneCodeHash;
            public Auth_SentCode LastSentCode;
            public bool IsQrLogin;
        }

        private sealed class AccountRuntime
        {
            public string AccountId;
            public TelegramAccountModel Account;
            public Client Client;
            public UpdateManager UpdateManager;
            public TelegramAuthState AuthState = TelegramAuthState.LoggedOut;
            public readonly object Gate = new object();
            public readonly List<TelegramChatModel> Chats = new List<TelegramChatModel>();
            public readonly List<TelegramMessageModel> Messages = new List<TelegramMessageModel>();
            public string OpenChatKey;
            public bool HasMoreMessages;
            public Timer OfflineTimer;
            public readonly ConcurrentDictionary<long, User> Users = new ConcurrentDictionary<long, User>();
            public readonly ConcurrentDictionary<long, ChatBase> ChatsById = new ConcurrentDictionary<long, ChatBase>();
            public readonly ConcurrentDictionary<string, IPeerInfo> PeerByChatKey = new ConcurrentDictionary<string, IPeerInfo>(StringComparer.Ordinal);
            public readonly ConcurrentDictionary<string, object> MediaCache = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
