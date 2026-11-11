// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;
using VanillaCloudStorageClient;

namespace SilentNotes.Models
{
    /// <summary>
    /// An extra cloud or WebDAV target for the File tab (in addition to the primary File WebDAV).
    /// </summary>
    public class CloudFileLocationModel
    {
        /// <summary>Location id used by <see cref="PathTabSessionModel.LocationId"/> for the primary target.</summary>
        public const string PrimaryLocationId = string.Empty;

        public const string ProviderWebdav = "webdav";
        public const string ProviderDropbox = "dropbox";
        public const string ProviderGoogleDrive = "googledrive";
        public const string ProviderOneDrive = "onedrive";
        public const string ProviderMega = "mega";

        /// <summary>Gets or sets a stable id for this location.</summary>
        [XmlAttribute("id")]
        public string Id { get; set; }

        /// <summary>Gets or sets the label shown in menus and path tabs.</summary>
        [XmlAttribute("name")]
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the provider key (see Provider* constants).</summary>
        [XmlElement("provider")]
        public string ProviderId { get; set; }

        /// <summary>Gets or sets the server URL (WebDAV) or unused for OAuth providers.</summary>
        [XmlElement("url")]
        public string Url { get; set; }

        /// <summary>Gets or sets the root remote folder for browsing.</summary>
        [XmlElement("folder")]
        public string RemoteFolder { get; set; }

        /// <summary>Gets or sets the username for WebDAV login.</summary>
        [XmlElement("username")]
        public string Username { get; set; }

        /// <summary>Gets or sets the WebDAV password in plain text (not serialized directly).</summary>
        [XmlIgnore]
        public string Password { get; set; }

        /// <summary>Gets or sets the encrypted WebDAV password on device.</summary>
        [XmlElement("password")]
        public string SerializeablePassword { get; set; }

        /// <summary>
        /// When true, OAuth-backed locations use <see cref="SettingsModel.Credentials"/> when the
        /// cloud storage id matches <see cref="ProviderId"/>.
        /// </summary>
        [XmlElement("use_repo_credentials")]
        public bool UseRepositoryCredentials { get; set; }

        /// <summary>Gets or sets optional per-location OAuth credentials.</summary>
        [XmlElement("cloud_credentials")]
        public SerializeableCloudStorageCredentials CloudCredentials { get; set; }

        /// <summary>Gets a value indicating whether this location is the Mega.nz placeholder.</summary>
        [XmlIgnore]
        public bool IsMegaStub =>
            string.Equals(ProviderId, ProviderMega, StringComparison.OrdinalIgnoreCase);
    }
}
