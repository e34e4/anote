// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Synchronizes the encrypted password vault file with a WebDAV path that shares the Booky
    /// sync credentials. The remote file is the Cryptor ciphertext as stored locally.
    /// </summary>
    public interface IPasswordVaultSyncService
    {
        /// <summary>
        /// Gets a value indicating whether Booky WebDAV is enabled and a password path is set.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Gets the message of the last error, empty after a successful synchronization.
        /// </summary>
        string LastError { get; }

        /// <summary>
        /// Downloads the remote vault when needed, merges by <c>ModifiedAt</c> when the vault is
        /// unlocked, and uploads the winning ciphertext.
        /// </summary>
        Task<PasswordVaultSyncResult> SyncAsync();
    }

    /// <summary>
    /// Result of a password vault WebDAV synchronization.
    /// </summary>
    public enum PasswordVaultSyncResult
    {
        /// <summary>Booky sync is disabled, nothing was done.</summary>
        Disabled,

        /// <summary>Enabled but the WebDAV url or password path is missing.</summary>
        NotConfigured,

        /// <summary>Nothing to sync (no local and no remote vault).</summary>
        NothingToSync,

        /// <summary>The vault file was synchronized successfully.</summary>
        Success,

        /// <summary>The synchronization failed.</summary>
        Failed,
    }
}
