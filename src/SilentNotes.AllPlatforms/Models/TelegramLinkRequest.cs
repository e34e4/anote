// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Pending request to open a Telegram deep link (t.me / tg://) in the TG tab.
    /// </summary>
    public class TelegramLinkRequest
    {
        /// <summary>Gets or sets the pending request for the main page / TG tab.</summary>
        public static TelegramLinkRequest Pending { get; set; }

        /// <summary>
        /// Raw link or Mini App path, e.g. <c>https://t.me/mknewsru</c>, <c>/mknewsru</c>,
        /// or <c>tg://resolve?domain=mknewsru</c>.
        /// </summary>
        public string Link { get; set; }
    }
}
