// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// SOCKS5 proxy options stored in application settings.
    /// </summary>
    public interface IProxySettings
    {
        /// <summary>Gets a value indicating whether the SOCKS5 proxy is enabled.</summary>
        bool Socks5Enabled { get; }

        /// <summary>Gets a value indicating whether Abro AI HTTP traffic should use the proxy.</summary>
        bool AiUseProxy { get; }

        /// <summary>Gets a value indicating whether host and port are configured.</summary>
        bool HasSocks5Config { get; }

        /// <summary>Gets the proxy host name or address.</summary>
        string Socks5Host { get; }

        /// <summary>Gets the proxy port.</summary>
        int Socks5Port { get; }

        /// <summary>Gets the optional proxy username.</summary>
        string Socks5Username { get; }

        /// <summary>Gets the optional proxy password.</summary>
        string Socks5Password { get; }
    }
}
