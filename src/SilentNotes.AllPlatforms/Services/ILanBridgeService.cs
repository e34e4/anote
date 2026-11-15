// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Services
{
    /// <summary>
    /// A lightweight local HTTP/WebSocket server, which serves the notes and the clipboard
    /// history to a desktop browser on the same Wi-Fi network. It is only active while aNote is
    /// running and the user started it, no cloud service or external server is involved.
    /// </summary>
    public interface ILanBridgeService
    {
        /// <summary>Gets a value indicating whether the server is currently listening.</summary>
        bool IsRunning { get; }

        /// <summary>Gets the port the server is listening on.</summary>
        int Port { get; }

        /// <summary>Gets the access token required by clients, or an empty string if disabled.</summary>
        string AccessToken { get; }

        /// <summary>Gets the last error message, or null.</summary>
        string LastError { get; }

        /// <summary>Gets a value indicating whether the desktop may push clipboard items.</summary>
        bool AllowClipboardWrites { get; }

        /// <summary>
        /// Starts the local server on all network interfaces.
        /// </summary>
        /// <param name="port">The TCP port to listen on, 0 selects a free port.</param>
        /// <param name="accessToken">An optional token which clients must send, or null/empty to disable the check.</param>
        /// <param name="allowClipboardWrites">Whether clients may add items to the clipboard history.</param>
        /// <returns>The url for local clients, or null when the server could not be started.</returns>
        string Start(int port, string accessToken, bool allowClipboardWrites);

        /// <summary>Stops the server.</summary>
        void Stop();

        /// <summary>Notifies connected clients that the clipboard history changed.</summary>
        void NotifyClipboardChanged();

        /// <summary>Gets the urls under which the server is reachable in the local network.</summary>
        /// <returns>List of urls, including the access token.</returns>
        IReadOnlyList<string> GetLanUrls();
    }
}
