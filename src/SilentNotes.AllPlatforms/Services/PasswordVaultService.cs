// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using SilentNotes.Crypto;
using SilentNotes.Crypto.KeyDerivation;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <summary>
    /// Loads/saves the password vault as a separate Cryptor-encrypted file.
    /// </summary>
    public class PasswordVaultService : IPasswordVaultService
    {
        /// <summary>Cryptor package name written into the vault file header.</summary>
        public const string CryptorPackageName = "AnoteVault";

        /// <summary>Cryptor package name for the PIN wrap of the master password.</summary>
        public const string PinCryptorPackageName = "AnoteVaultPin";

        /// <summary>Local (and default remote) filename of the encrypted vault.</summary>
        public const string VaultFileName = "anote_password_vault.anote";

        private readonly IRepositoryStorageService _repositoryStorageService;
        private readonly ISettingsService _settingsService;
        private readonly ICryptoRandomService _randomService;

        private PasswordVaultModel _vault;
        private SecureString _masterPassword;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordVaultService"/> class.
        /// </summary>
        public PasswordVaultService(
            IRepositoryStorageService repositoryStorageService,
            ISettingsService settingsService,
            ICryptoRandomService randomService)
        {
            _repositoryStorageService = repositoryStorageService;
            _settingsService = settingsService;
            _randomService = randomService;
        }

        /// <inheritdoc/>
        public bool VaultExists
        {
            get { return File.Exists(GetVaultPath()); }
        }

        /// <inheritdoc/>
        public bool IsUnlocked
        {
            get { return _vault != null && _masterPassword != null; }
        }

        /// <inheritdoc/>
        public bool HasPin
        {
            get
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                return !string.IsNullOrEmpty(settings.SerializeablePasswordVaultPinWrap);
            }
        }

        /// <inheritdoc/>
        public PasswordVaultModel Vault
        {
            get { return _vault; }
        }

        /// <inheritdoc/>
        public bool CreateVault(SecureString masterPassword)
        {
            if (masterPassword == null || masterPassword.Length < 5)
                return false;
            if (VaultExists)
                return false;

            _vault = new PasswordVaultModel();
            ReplaceMasterPassword(masterPassword);
            return Save();
        }

        /// <inheritdoc/>
        public bool UnlockWithMaster(SecureString masterPassword)
        {
            if (masterPassword == null || !VaultExists)
                return false;

            try
            {
                byte[] cipher = File.ReadAllBytes(GetVaultPath());
                ICryptor cryptor = new Cryptor(CryptorPackageName, null);
                byte[] plain = cryptor.Decrypt(cipher, masterPassword, out _);
                XDocument document = XmlUtils.LoadFromXmlBytes(plain);
                _vault = DeserializeVault(document);
                ReplaceMasterPassword(masterPassword);
                return true;
            }
            catch
            {
                Lock();
                return false;
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Password vault models are annotated for XmlSerializer.")]
        private static PasswordVaultModel DeserializeVault(XDocument document)
        {
            return XmlUtils.DeserializeFromXmlDocument<PasswordVaultModel>(document);
        }

        /// <inheritdoc/>
        public bool UnlockWithPin(SecureString pin)
        {
            if (pin == null || !VaultExists || !HasPin)
                return false;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                byte[] wrapCipher = Convert.FromBase64String(settings.SerializeablePasswordVaultPinWrap);
                ICryptor pinCryptor = new Cryptor(PinCryptorPackageName, null);
                byte[] masterBytes = pinCryptor.Decrypt(wrapCipher, pin, out _);
                string masterText = Encoding.UTF8.GetString(masterBytes);
                CryptoUtils.CleanArray(masterBytes);
                SecureString masterPassword = CryptoUtils.StringToSecureString(masterText);
                bool ok = UnlockWithMaster(masterPassword);
                masterPassword?.Dispose();
                return ok;
            }
            catch
            {
                Lock();
                return false;
            }
        }

        /// <inheritdoc/>
        public bool SetPin(SecureString pin)
        {
            // Cryptor rejects secrets shorter than 5 characters.
            if (!IsUnlocked || pin == null || pin.Length < 5)
                return false;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                string masterText = _masterPassword.SecureStringToString();
                byte[] masterBytes = Encoding.UTF8.GetBytes(masterText);
                ICryptor pinCryptor = new Cryptor(PinCryptorPackageName, _randomService);
                byte[] wrapCipher = pinCryptor.Encrypt(
                    masterBytes,
                    pin,
                    KeyDerivationCostType.High,
                    settings.SelectedEncryptionAlgorithm,
                    settings.SelectedKdfAlgorithm);
                CryptoUtils.CleanArray(masterBytes);
                settings.SerializeablePasswordVaultPinWrap = Convert.ToBase64String(wrapCipher);
                _settingsService.TrySaveSettingsToLocalDevice(settings);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public void ClearPin()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (string.IsNullOrEmpty(settings.SerializeablePasswordVaultPinWrap))
                return;
            settings.SerializeablePasswordVaultPinWrap = null;
            _settingsService.TrySaveSettingsToLocalDevice(settings);
        }

        /// <inheritdoc/>
        public void Lock()
        {
            _vault = null;
            if (_masterPassword != null)
            {
                _masterPassword.Dispose();
                _masterPassword = null;
            }
        }

        /// <inheritdoc/>
        public bool Save()
        {
            if (!IsUnlocked)
                return false;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                _vault.RefreshModifiedAt();
                byte[] plain = SerializeVault(_vault);
                ICryptor cryptor = new Cryptor(CryptorPackageName, _randomService);
                byte[] cipher = cryptor.Encrypt(
                    plain,
                    _masterPassword,
                    KeyDerivationCostType.High,
                    settings.SelectedEncryptionAlgorithm,
                    settings.SelectedKdfAlgorithm,
                    Cryptor.CompressionGzip);
                CryptoUtils.CleanArray(plain);
                string path = GetVaultPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                // Atomic write avoids a truncated vault if the process is killed mid-save (large imports).
                new AtomicFileWriter { MinValidFileSize = 32 }.Write(path, stream => stream.Write(cipher, 0, cipher.Length));
                return true;
            }
            catch
            {
                return false;
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Password vault models are annotated for XmlSerializer.")]
        private static byte[] SerializeVault(PasswordVaultModel vault)
        {
            return XmlUtils.SerializeToXmlBytes(vault, Encoding.UTF8);
        }

        /// <inheritdoc/>
        public IReadOnlyList<PasswordEntryModel> FindMatches(string uriOrPackage)
        {
            if (!IsUnlocked || string.IsNullOrWhiteSpace(uriOrPackage))
                return Array.Empty<PasswordEntryModel>();

            string needle = NormalizeMatchToken(uriOrPackage);
            if (string.IsNullOrEmpty(needle))
                return Array.Empty<PasswordEntryModel>();

            return _vault.Entries
                .Where(entry => entry.Uris.Any(uri =>
                {
                    string candidate = NormalizeMatchToken(uri);
                    return !string.IsNullOrEmpty(candidate) &&
                        (candidate.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                         needle.Contains(candidate, StringComparison.OrdinalIgnoreCase));
                }))
                .ToList();
        }

        /// <inheritdoc/>
        public byte[] TryReadEncryptedBytes()
        {
            string path = GetVaultPath();
            if (!File.Exists(path))
                return null;
            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public bool WriteEncryptedBytesAndLock(byte[] cipherBytes)
        {
            if (cipherBytes == null || cipherBytes.Length < 32)
                return false;

            try
            {
                string path = GetVaultPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                new AtomicFileWriter { MinValidFileSize = 32 }.Write(path, stream => stream.Write(cipherBytes, 0, cipherBytes.Length));
                Lock();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public PasswordVaultMergeResult TryMergeRemoteCipher(byte[] remoteCipher)
        {
            if (!IsUnlocked)
                return PasswordVaultMergeResult.Locked;
            if (remoteCipher == null || remoteCipher.Length < 32)
                return PasswordVaultMergeResult.DecryptFailed;

            try
            {
                ICryptor cryptor = new Cryptor(CryptorPackageName, null);
                byte[] plain = cryptor.Decrypt(remoteCipher, _masterPassword, out _);
                XDocument document = XmlUtils.LoadFromXmlBytes(plain);
                CryptoUtils.CleanArray(plain);
                PasswordVaultModel remoteVault = DeserializeVault(document);
                if (remoteVault == null)
                    return PasswordVaultMergeResult.DecryptFailed;

                if (remoteVault.ModifiedAt <= _vault.ModifiedAt)
                    return PasswordVaultMergeResult.LocalKept;

                _vault = remoteVault;
                if (!Save())
                    return PasswordVaultMergeResult.DecryptFailed;
                return PasswordVaultMergeResult.RemoteApplied;
            }
            catch
            {
                return PasswordVaultMergeResult.DecryptFailed;
            }
        }

        private string GetVaultPath()
        {
            return Path.Combine(_repositoryStorageService.GetLocation(), VaultFileName);
        }

        private void ReplaceMasterPassword(SecureString masterPassword)
        {
            if (_masterPassword != null)
            {
                _masterPassword.Dispose();
                _masterPassword = null;
            }
            // Keep an independent copy while the vault stays unlocked (needed for Save / SetPin).
            string text = masterPassword.SecureStringToString();
            _masterPassword = CryptoUtils.StringToSecureString(text);
        }

        private static string NormalizeMatchToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim();
            if (trimmed.StartsWith("androidapp://", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("androidapp://".Length).ToLowerInvariant();

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
            {
                string host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("www.", StringComparison.Ordinal))
                    host = host.Substring(4);
                return host;
            }

            // Bare host or package name
            string lower = trimmed.ToLowerInvariant();
            if (lower.StartsWith("www.", StringComparison.Ordinal))
                lower = lower.Substring(4);
            return lower;
        }
    }
}
