// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Xml.Serialization;
using SilentNotes.Crypto.KeyDerivation;
using SilentNotes.Crypto.SymmetricEncryption;
using VanillaCloudStorageClient;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of the applications settings.
    /// </summary>
    [XmlRoot(ElementName = "silentnotes_settings")]
    public class SettingsModel
    {
        /// <summary>The highest revision of the settings which can be handled by this application.</summary>
        public const int NewestSupportedRevision = 3;
#if (RELEASE)
        public const string UserSettingsFileName = "silentnotes_user_settings.config";
#else
        public const string UserSettingsFileName = "silentnotes_user_settings_dev.config";
#endif

        /// <summary>The default color for notes, when the application is started the first time.</summary>
        public const string StartDefaultNoteColorHex = "#000000";

        private string _selectedEncryptionAlgorithm;
        private string _selectedKdfAlgorithm;
        private string _transferCode;
        private List<string> _noteColorsHex;
        private List<string> _transferCodeHistory;
        private List<NotificationTriggerModel> _notificationTriggers;
        private List<string> _filterTags;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsModel"/> class.
        /// </summary>
        public SettingsModel()
        {
            Revision = NewestSupportedRevision;
            AutoSyncMode = AutoSynchronizationMode.CostFreeInternetOnly;
            FontScale = 1.0;
            // aNote starts with a plain black background instead of the wallpaper of SilentNotes.
            UseSolidColorTheme = true;
            ColorForSolidTheme = "#000000";
            BackgroundColorHex = "#000000";
            DefaultNoteColorHex = StartDefaultNoteColorHex;
            NoteMaxHeightScale = 1.0;
            DefaultNoteInsertion = NoteInsertionMode.AtTop;
            UseColorForAllNotesInDarkMode = false;
            ColorForAllNotesInDarkModeHex = "#323232";
            KeepScreenUpDuration = 15;
            UseWallpaper = false;
            RememberLastTagFilter = false;
            AlwaysEnglish = false;
            ShowFloatingButton = false;
            ClipboardMonitoringEnabled = false;
            ClipboardHistorySize = 500;
            ClipboardPollIntervalMs = 2500;
            BookyUrlCaptureEnabled = false;
            BookySyncEnabled = false;
            BookySyncUrl = string.Empty;
            BookySyncRemotePath = "booky/bookmarks.json";
            BookySyncUsername = string.Empty;
            BookySyncPassword = string.Empty;
            FileWebdavUrl = string.Empty;
            FileWebdavFolder = string.Empty;
            FileWebdavUsername = string.Empty;
            FileWebdavPassword = string.Empty;
            QrVibrateOnScan = true;
            QrAutoCopyToClipboard = false;
            QrKeepHistory = true;
            PhoneBlocklistEnabled = true;
            PhoneAutoBlockSpam = false;
            PhoneSkipContactsWhenBlocking = true;
            PhoneCustomSpamUrl = string.Empty;
        }

        /// <summary>
        /// Gets or sets the revision, which was used to create the settings.
        /// </summary>
        [XmlAttribute(AttributeName = "revision")]
        public int Revision { get; set; }

        /// <summary>
        /// Gets or sets the credentials for the online storage if available, otherwise this
        /// property is null.
        /// </summary>
        [XmlElement("cloud_storage_credentials")]
        public SerializeableCloudStorageCredentials Credentials { get; set; }

        /// <summary>
        /// Gets or sets the name of the font family for the notes.
        /// </summary>
        [XmlElement("font-family")]
        public string FontFamily { get; set; }

        /// <summary>
        /// Gets or sets a factor to enlarge or reduce the font size of the notes.
        /// </summary>
        [XmlElement("font-scale")]
        public double FontScale { get; set; }

        /// <summary>
        /// Gets or sets the id of the wallpaper selected by the user.
        /// </summary>
        [XmlElement("selected_wallpaper")]
        public string SelectedWallpaper { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the wallpaper should be used as background image.
        /// </summary>
        [XmlElement("use_wallpaper")]
        public bool UseWallpaper { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the a solid color should be used instead of a
        /// background image.
        /// </summary>
        [XmlElement("use_solid_color_theme")]
        public bool UseSolidColorTheme { get; set; }

        /// <summary>
        /// Gets or sets the solid background color for the theme background. It depends on
        /// <see cref="UseSolidColorTheme"/> whether this value is respected.
        /// </summary>
        [XmlElement("color_for_solid_theme")]
        public string ColorForSolidTheme { get; set; }

        /// <summary>
        /// Gets or sets the background color of the application itself, this is the color behind
        /// the pages, e.g. the settings page.
        /// </summary>
        [XmlElement("background_color")]
        public string BackgroundColorHex { get; set; }

        /// <summary>
        /// Gets or sets a value describing whether the dark mode should be used.
        /// </summary>
        [XmlElement("theme_mode")]
        public ThemeMode ThemeMode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the note color should be the same for all notes
        /// in dark mode.
        /// </summary>
        [XmlElement("use_color_for_all_notes_dark")]
        public bool UseColorForAllNotesInDarkMode { get; set; }

        /// <summary>
        /// Gets or sets the background color for new notes. It depends on <see cref="UseColorForAllNotesInDarkMode"/>
        /// whether this value is respected.
        /// </summary>
        [XmlElement("color_for_all_notes_dark")]
        public string ColorForAllNotesInDarkModeHex { get; set; }

        /// <summary>
        /// Gets a list of available background colors for the notes.
        /// </summary>
        [XmlIgnore]
        public List<string> NoteColorsHex
        {
            get
            {
                return
                    _noteColorsHex ??
                    (_noteColorsHex = new List<string>
                    {
                        "#ffffff", "#fbf4c1", "#fdd8bb", "#facbc6", "#fcd5ef", "#d9d9fc", "#cee7fb", "#d0f8f9", "#d9f8c8",
                        "#ae7f0a", "#871908", "#800080", "#0d5696", "#007a7a", "#33750f", "#333333", "#000000",
                    });
            }
        }

        /// <summary>
        /// Gets or sets the background color for new notes.
        /// </summary>
        [XmlElement("default_note_color")]
        public string DefaultNoteColorHex { get; set; }

        /// <summary>
        /// Gets or sets a factor to enlarge or reduce the standard max height of the notes.
        /// </summary>
        [XmlElement("note_max_height_scale")]
        public double NoteMaxHeightScale { get; set; }

        /// <summary>
        /// Gets or sets the duration [min] of the function "KeepScreenOn".
        /// </summary>
        [XmlElement("keep_screen_up_duration")]
        public int KeepScreenUpDuration { get; set; }

        /// <summary>
        /// Gets or sets the place where a new note will be inserted by default.
        /// </summary>
        [XmlElement("default_note_insertion")]
        public NoteInsertionMode DefaultNoteInsertion { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the drawer with the tag list should be kept open
        /// on wide screens.
        /// </summary>
        [XmlElement("start_with_tags_open")]
        public bool StartWithTagsOpen { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the last selected tag to filter the notes
        /// should be remembered across startups.
        /// </summary>
        [XmlElement("remember_last_tag_filter")]
        public bool RememberLastTagFilter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether notes should be hidden in the overview, if they
        /// are part of a closed safe.
        /// </summary>
        [XmlElement("hide_closed_safe_notes")]
        public bool HideClosedSafeNotes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the device should vibrate after a QR code was
        /// scanned or decoded.
        /// </summary>
        [XmlElement("qr_vibrate_on_scan")]
        public bool QrVibrateOnScan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the result of a QR scan should be copied
        /// automatically to the clipboard.
        /// </summary>
        [XmlElement("qr_auto_copy")]
        public bool QrAutoCopyToClipboard { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether scanned QR codes should be stored in the
        /// history of the QR tab.
        /// </summary>
        [XmlElement("qr_keep_history")]
        public bool QrKeepHistory { get; set; }

        /// <summary>
        /// Gets or sets the selected encryption algorithm, used to encrypt the repository
        /// before sending it to the cloud.
        /// </summary>
        [XmlElement("selected_encryption_algorithm")]
        public string SelectedEncryptionAlgorithm
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_selectedEncryptionAlgorithm))
                    _selectedEncryptionAlgorithm = GetDefaultEncryptionAlgorithmName();
                return _selectedEncryptionAlgorithm;
            }

            set { _selectedEncryptionAlgorithm = value; }
        }

        /// <summary>
        /// Gets or sets the selected kdf algorithm, used to generate the key for the safe.
        /// </summary>
        public string SelectedKdfAlgorithm
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_selectedKdfAlgorithm))
                    _selectedKdfAlgorithm = GetDefaultKdfAlgorithmName();
                return _selectedKdfAlgorithm;
            }

            set { _selectedKdfAlgorithm = value; }
        }

        /// <summary>
        /// Gets or sets a value describing whether auto synchronization with the online storage
        /// shoudl be done or not.
        /// </summary>
        [XmlElement("auto_sync_mode")]
        public AutoSynchronizationMode AutoSyncMode { get; set; }

        /// <summary>
        /// Gets or sets the transfer code corresponding with the current repository.
        /// Manages the transfercode history, when adding a new transfer code.
        /// </summary>
        [XmlElement("transfer_code")]
        public string TransferCode
        {
            get { return _transferCode; }

            set
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(_transferCode, value))
                    return;

                // Remove the new transfer code from the archive, if existing
                TransferCodeHistory.Remove(value);

                // Archive the current transfer code if existing
                if (!string.IsNullOrWhiteSpace(_transferCode))
                {
                    TransferCodeHistory.Remove(_transferCode);
                    TransferCodeHistory.Insert(0, _transferCode); // bring to top
                }

                // Replace current transfer code
                _transferCode = value;
            }
        }

        /// <summary>
        /// Gets or sets a history of old transfercodes.
        /// </summary>
        [XmlArray("transfer_code_history")]
        [XmlArrayItem("transfer_code")]
        public List<string> TransferCodeHistory
        {
            get { return _transferCodeHistory ?? (_transferCodeHistory = new List<string>()); }
            set { _transferCodeHistory = value; }
        }

        /// <summary>
        /// Gets the name of the algorithm to use, if the selected algorithm is not yet stored.
        /// </summary>
        /// <returns>The name of the default encryption algorithm.</returns>
        public static string GetDefaultEncryptionAlgorithmName()
        {
            return BouncyCastleXChaCha20.CryptoAlgorithmName;
        }

        /// <summary>
        /// Gets the name of the algorithm to use, if the selected algorithm is not yet stored.
        /// </summary>
        /// <returns>The name of the default kdf algorithm.</returns>
        public static string GetDefaultKdfAlgorithmName()
        {
            return BouncyCastleArgon2.CryptoKdfName;
        }

        /// <summary>
        /// Gets or sets a value indicating whether taking screenshots should be forbidden or allowed.
        /// </summary>
        [XmlElement("prevent_screenshots")]
        public bool PreventScreenshots { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the OS language should be ignored and the
        /// english preferred if the GUI.
        /// </summary>
        [XmlElement("always_english")]
        public bool AlwaysEnglish {  get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a floating button should be shown, which opens
        /// the application. This is only supported on Android by an accessibility service.
        /// </summary>
        [XmlElement("show_floating_button")]
        public bool ShowFloatingButton { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the system clipboard should be monitored in the
        /// background, to build a clipboard history. This is only supported on Android by an
        /// accessibility service.
        /// </summary>
        [XmlElement("clipboard_monitoring")]
        public bool ClipboardMonitoringEnabled { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of non-favorite clipboard items to keep. Favorite items
        /// are never removed by this limit.
        /// </summary>
        [XmlElement("clipboard_history_size")]
        public int ClipboardHistorySize { get; set; }

        /// <summary>
        /// Gets or sets the interval in milliseconds, in which the background monitor checks the
        /// clipboard for changes.
        /// </summary>
        [XmlElement("clipboard_poll_interval_ms")]
        public int ClipboardPollIntervalMs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the url of the page currently open in a browser
        /// should be captured, so it can be used as the default url when creating a bookmark. This
        /// is only supported on Android by an accessibility service.
        /// </summary>
        [XmlElement("booky_url_capture")]
        public bool BookyUrlCaptureEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the bookmarks of the Booky tab should be
        /// synchronized with the shared bookmarks file of the Booky browser extension.
        /// </summary>
        [XmlElement("booky_sync_enabled")]
        public bool BookySyncEnabled { get; set; }

        /// <summary>
        /// Gets or sets the base url of the WebDAV server which hosts the shared Booky bookmarks
        /// file. The remote path is appended to this url.
        /// </summary>
        [XmlElement("booky_sync_url")]
        public string BookySyncUrl { get; set; }

        /// <summary>
        /// Gets or sets the path of the shared Booky bookmarks file, relative to
        /// <see cref="BookySyncUrl"/>, e.g. "booky/bookmarks.json".
        /// </summary>
        [XmlElement("booky_sync_remote_path")]
        public string BookySyncRemotePath { get; set; }

        /// <summary>
        /// Gets or sets the user name for the WebDAV server of the Booky sync.
        /// </summary>
        [XmlElement("booky_sync_username")]
        public string BookySyncUsername { get; set; }

        /// <summary>
        /// Gets or sets the password for the WebDAV server of the Booky sync, in plain text. Use
        /// <see cref="SerializeableBookySyncPassword"/> for the value stored on the device.
        /// </summary>
        [XmlIgnore]
        public string BookySyncPassword { get; set; }

        /// <summary>
        /// Gets or sets the encrypted password of the Booky sync, as it is stored on the local
        /// device. Keeping this in a separate property (like the cloud storage credentials do)
        /// makes repeated saving idempotent.
        /// </summary>
        [XmlElement("booky_sync_password")]
        public string SerializeableBookySyncPassword { get; set; }

        /// <summary>
        /// Gets a value indicating whether the encrypted Booky password should be serialized.
        /// </summary>
        public bool SerializeableBookySyncPasswordSpecified
        {
            get { return !string.IsNullOrEmpty(SerializeableBookySyncPassword); }
        }

        /// <summary>
        /// Gets or sets the base url of the WebDAV server for the File tab.
        /// </summary>
        [XmlElement("file_webdav_url")]
        public string FileWebdavUrl { get; set; }

        /// <summary>
        /// Gets or sets the remote folder path on the WebDAV server for the File tab.
        /// </summary>
        [XmlElement("file_webdav_folder")]
        public string FileWebdavFolder { get; set; }

        /// <summary>
        /// Gets or sets the user name for the WebDAV server of the File tab.
        /// </summary>
        [XmlElement("file_webdav_username")]
        public string FileWebdavUsername { get; set; }

        /// <summary>
        /// Gets or sets the password for the WebDAV server of the File tab, in plain text.
        /// </summary>
        [XmlIgnore]
        public string FileWebdavPassword { get; set; }

        /// <summary>
        /// Gets or sets the encrypted password of the File tab WebDAV server, as stored on device.
        /// </summary>
        [XmlElement("file_webdav_password")]
        public string SerializeableFileWebdavPassword { get; set; }

        /// <summary>
        /// Gets a value indicating whether the encrypted File WebDAV password should be serialized.
        /// </summary>
        public bool SerializeableFileWebdavPasswordSpecified
        {
            get { return !string.IsNullOrEmpty(SerializeableFileWebdavPassword); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether incoming calls matching the Phone blocklist
        /// (including wildcard masks like +7903*) are automatically rejected.
        /// </summary>
        [XmlElement("phone_blocklist_enabled")]
        public bool PhoneBlocklistEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether incoming calls flagged as negative/spam in the
        /// spam database are automatically rejected.
        /// </summary>
        [XmlElement("phone_auto_block_spam")]
        public bool PhoneAutoBlockSpam { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether numbers present in the repository contacts are
        /// exempt from automatic blocking.
        /// </summary>
        [XmlElement("phone_skip_contacts_when_blocking")]
        public bool PhoneSkipContactsWhenBlocking { get; set; }

        /// <summary>
        /// Gets or sets an optional custom URL (TXT/CSV/XML) to include when updating the spam database.
        /// </summary>
        [XmlElement("phone_custom_spam_url")]
        public string PhoneCustomSpamUrl { get; set; }

        /// <summary>
        /// Gets a value indicating whether a cloud storage is set.
        /// </summary>
        public bool HasCloudStorageClient
        {
            get { return Credentials?.CloudStorageId != null; }
        }

        /// <summary>
        /// Gets a value indicating whether at least one transfercode is set.
        /// </summary>
        public bool HasTransferCode
        {
            get { return !string.IsNullOrWhiteSpace(TransferCode); }
        }

        /// <summary>
        /// Gets or sets a temporary list of selected tags which are used for filtering.
        /// </summary>
        [XmlElement("filter_tags")]
        public List<string> FilterTags
        {
            get { return _filterTags ?? (_filterTags = new List<string>()); }
            set { _filterTags = value; }
        }

        /// <summary>
        /// Gets or sets a list of stored startup notification triggers.
        /// </summary>
        [XmlArray("notification_triggers")]
        [XmlArrayItem("notification_trigger")]
        public List<NotificationTriggerModel> NotificationTriggers
        {
            get { return _notificationTriggers ?? (_notificationTriggers = new List<NotificationTriggerModel>()); }
            set { _notificationTriggers = value; }
        }

        /// <summary>
        /// Gets or sets the currently active note filter.
        /// </summary>
        [XmlIgnore]
        public string Filter { get; set; }
    }
}
