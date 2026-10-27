// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Parameters for hosting a bot Mini App in a platform WebView.
    /// </summary>
    public sealed class BotWebAppHostRequest
    {
        public string Url { get; init; }

        public string Title { get; init; }

        public long QueryId { get; init; }

        public bool HasQueryId { get; init; }

        public Func<CancellationToken, Task> ProlongAsync { get; init; }

        /// <summary>
        /// Sends <c>web_app_data_send</c> payload via Messages_SendWebViewData.
        /// Arguments: buttonText, data, cancellationToken.
        /// </summary>
        public Func<string, string, CancellationToken, Task> SendWebAppDataAsync { get; init; }

        /// <summary>
        /// Invokes <c>bots.invokeWebViewCustomMethod</c> (CloudStorage and other custom methods).
        /// Arguments: method name, params JSON object string, cancellationToken.
        /// Returns the response JSON object string from <c>DataJSON.data</c>.
        /// </summary>
        public Func<string, string, CancellationToken, Task<string>> InvokeCustomMethodAsync { get; init; }
    }
}
