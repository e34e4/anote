// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model of the LAN bridge page, which starts and stops the local HTTP/WebSocket server
    /// that lets a desktop browser on the same Wi-Fi read notes and exchange clipboard items.
    /// </summary>
    public class LanBridgeViewModel : ViewModelBase
    {
        private readonly ILanBridgeService _lanBridgeService;
        private readonly ISettingsService _settingsService;
        private readonly IFeedbackService _feedbackService;
        private readonly ILanguageService _languageService;
        private int _port;
        private string _accessToken;
        private bool _allowClipboardWrites;
        private bool _autoStart;
        private bool _isRunning;
        private string _lastError;

        /// <summary>
        /// Initializes a new instance of the <see cref="LanBridgeViewModel"/> class.
        /// </summary>
        public LanBridgeViewModel(
            ILanBridgeService lanBridgeService,
            ISettingsService settingsService,
            IFeedbackService feedbackService,
            ILanguageService languageService)
        {
            _lanBridgeService = lanBridgeService;
            _settingsService = settingsService;
            _feedbackService = feedbackService;
            _languageService = languageService;

            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            _port = settings.LanBridgePort <= 0 ? 8787 : settings.LanBridgePort;
            _accessToken = settings.LanBridgeAccessToken ?? string.Empty;
            _allowClipboardWrites = settings.LanBridgeAllowWrites;
            _autoStart = settings.LanBridgeEnabled;
            _isRunning = _lanBridgeService.IsRunning;

            // Be secure by default: if no token was ever configured, create one.
            if (string.IsNullOrEmpty(_accessToken))
            {
                _accessToken = CreateToken();
                settings.LanBridgeAccessToken = _accessToken;
                _settingsService.TrySaveSettingsToLocalDevice(settings);
            }

            StartCommand = new RelayCommand(Start);
            StopCommand = new RelayCommand(Stop);
            ToggleCommand = new RelayCommand(Toggle);
            NewTokenCommand = new RelayCommand(GenerateNewToken);
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        /// <summary>Gets or sets the TCP port of the server.</summary>
        public int Port
        {
            get { return _port; }
            set { SetProperty(ref _port, value); }
        }

        /// <summary>Gets or sets the access token required by desktop clients.</summary>
        public string AccessToken
        {
            get { return _accessToken; }
            set { SetProperty(ref _accessToken, value); }
        }

        /// <summary>Gets or sets a value indicating whether the desktop may push clipboard items.</summary>
        public bool AllowClipboardWrites
        {
            get { return _allowClipboardWrites; }
            set { SetProperty(ref _allowClipboardWrites, value); }
        }

        /// <summary>Gets or sets a value indicating whether the server is started with the app.</summary>
        public bool AutoStart
        {
            get { return _autoStart; }
            set { SetProperty(ref _autoStart, value); }
        }

        /// <summary>Gets a value indicating whether the server is currently running.</summary>
        public bool IsRunning
        {
            get { return _isRunning; }
            private set { SetProperty(ref _isRunning, value); }
        }

        /// <summary>Gets the last error of the server, or null.</summary>
        public string LastError
        {
            get { return _lastError; }
            private set { SetProperty(ref _lastError, value); }
        }

        /// <summary>Gets the urls under which the server is reachable.</summary>
        public ObservableCollection<string> Urls { get; } = new ObservableCollection<string>();

        /// <summary>Gets the command which starts the server.</summary>
        public IRelayCommand StartCommand { get; }

        /// <summary>Gets the command which stops the server.</summary>
        public IRelayCommand StopCommand { get; }

        /// <summary>Gets the command which toggles the server.</summary>
        public IRelayCommand ToggleCommand { get; }

        /// <summary>Gets the command which generates a new random access token.</summary>
        public IRelayCommand NewTokenCommand { get; }

        /// <summary>Gets the command which refreshes the displayed state.</summary>
        public IRelayCommand RefreshCommand { get; }

        /// <summary>
        /// Starts the server with the currently configured settings and stores the settings, so
        /// they are used again on the next start.
        /// </summary>
        public void Start()
        {
            SaveSettings();

            string token = string.IsNullOrWhiteSpace(AccessToken) ? null : AccessToken.Trim();
            string url = _lanBridgeService.Start(Port <= 0 ? 0 : Port, token, AllowClipboardWrites);
            LastError = _lanBridgeService.LastError;
            IsRunning = _lanBridgeService.IsRunning;
            Refresh();

            if (IsRunning)
                _feedbackService.ShowToast(_languageService.LoadTextFmt("lan_bridge_started_fmt", _lanBridgeService.Port), FeedbackSeverity.Success);
            else
                _feedbackService.ShowToast(_languageService.LoadTextFmt("lan_bridge_failed_fmt", LastError), FeedbackSeverity.Error);
        }

        /// <summary>Stops the server.</summary>
        public void Stop()
        {
            _lanBridgeService.Stop();
            IsRunning = false;
            Urls.Clear();
            _feedbackService.ShowToast(_languageService["lan_bridge_stopped"]);
        }

        /// <summary>Toggles the server and stores the resulting state for the next app start.</summary>
        public void Toggle()
        {
            if (_lanBridgeService.IsRunning)
            {
                AutoStart = false;
                SaveSettings();
                Stop();
            }
            else
            {
                AutoStart = true;
                Start();
            }
        }

        private void GenerateNewToken()
        {
            AccessToken = CreateToken();
        }

        /// <summary>
        /// Creates a short random token for the LAN bridge.
        /// </summary>
        /// <returns>Hex encoded random token.</returns>
        public static string CreateToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(6);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private void Refresh()
        {
            Urls.Clear();
            if (_lanBridgeService.IsRunning)
            {
                foreach (string url in _lanBridgeService.GetLanUrls())
                    Urls.Add(url);
            }
        }

        private void SaveSettings()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            settings.LanBridgeEnabled = AutoStart;
            settings.LanBridgePort = Port;
            settings.LanBridgeAccessToken = AccessToken ?? string.Empty;
            settings.LanBridgeAllowWrites = AllowClipboardWrites;
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        /// <summary>
        /// Starts the LAN bridge on app startup when the user enabled it.
        /// </summary>
        /// <param name="lanBridgeService">The service to start.</param>
        /// <param name="settingsService">Service which provides the settings.</param>
        public static void StartIfEnabled(ILanBridgeService lanBridgeService, ISettingsService settingsService)
        {
            if (lanBridgeService == null || lanBridgeService.IsRunning)
                return;

            SettingsModel settings = settingsService.LoadSettingsOrDefault();
            if (!settings.LanBridgeEnabled)
                return;

            string token = settings.LanBridgeAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                token = CreateToken();
                settings.LanBridgeAccessToken = token;
                settingsService.TrySaveSettingsToLocalDevice(settings);
            }
            lanBridgeService.Start(settings.LanBridgePort, token, settings.LanBridgeAllowWrites);
        }
    }
}
