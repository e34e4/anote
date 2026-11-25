// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Event payload passed to <see cref="IAutomationRulesEngine.Evaluate"/>.
    /// </summary>
    public class AutomationRuleContext
    {
        /// <summary>Gets or sets the SMS sender number.</summary>
        public string SmsPhone { get; set; }

        /// <summary>Gets or sets the SMS message body.</summary>
        public string SmsBody { get; set; }

        /// <summary>Gets or sets the RSS feed title.</summary>
        public string FeedTitle { get; set; }

        /// <summary>
        /// Gets or sets text to append or use when creating a note (SMS body, RSS snippet, etc.).
        /// </summary>
        public string AppendText { get; set; }
    }
}