// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Security;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Manages the separate encrypted password vault file (master password + optional PIN).
    /// </summary>
    public interface IPasswordVaultService
    {
        /// <summary>Gets a value indicating whether a vault file exists on disk.</summary>
        bool VaultExists { get; }

        /// <summary>Gets a value indicating whether the vault is currently unlocked in memory.</summary>
        bool IsUnlocked { get; }

        /// <summary>Gets a value indicating whether a PIN unlock wrap is configured.</summary>
        bool HasPin { get; }

        /// <summary>Gets the unlocked vault model, or null when locked.</summary>
        PasswordVaultModel Vault { get; }

        /// <summary>Creates a new empty vault encrypted with the master password.</summary>
        bool CreateVault(SecureString masterPassword);

        /// <summary>Unlocks an existing vault with the master password.</summary>
        bool UnlockWithMaster(SecureString masterPassword);

        /// <summary>Unlocks an existing vault with the PIN (requires a previously set PIN).</summary>
        bool UnlockWithPin(SecureString pin);

        /// <summary>Sets or replaces the PIN unlock (vault must be unlocked).</summary>
        bool SetPin(SecureString pin);

        /// <summary>Removes the PIN unlock.</summary>
        void ClearPin();

        /// <summary>Locks the vault and clears secrets from memory.</summary>
        void Lock();

        /// <summary>Persists the current vault to the encrypted file (must be unlocked).</summary>
        bool Save();

        /// <summary>Reads the encrypted vault file bytes, or null when the file is missing.</summary>
        byte[] TryReadEncryptedBytes();

        /// <summary>
        /// Replaces the encrypted vault file and locks the in-memory vault so the next unlock
        /// reloads from disk.
        /// </summary>
        bool WriteEncryptedBytesAndLock(byte[] cipherBytes);

        /// <summary>
        /// When unlocked, decrypts a remote vault ciphertext and keeps the newer vault by
        /// <see cref="PasswordVaultModel.ModifiedAt"/>. Returns whether the remote replaced local.
        /// </summary>
        PasswordVaultMergeResult TryMergeRemoteCipher(byte[] remoteCipher);

        /// <summary>Finds entries matching a web domain or Android package name.</summary>
        IReadOnlyList<PasswordEntryModel> FindMatches(string uriOrPackage);
    }

    /// <summary>Result of merging a remote encrypted vault into the unlocked local vault.</summary>
    public enum PasswordVaultMergeResult
    {
        /// <summary>Local vault is newer or equal; keep local.</summary>
        LocalKept,

        /// <summary>Remote vault was newer and replaced the local unlocked vault (and was saved).</summary>
        RemoteApplied,

        /// <summary>Vault is locked; caller should decide without decrypting.</summary>
        Locked,

        /// <summary>Remote ciphertext could not be decrypted with the current master password.</summary>
        DecryptFailed,
    }
}
