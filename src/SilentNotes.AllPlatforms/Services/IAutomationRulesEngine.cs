// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Evaluates device-local automation rules stored in settings.
    /// </summary>
    public interface IAutomationRulesEngine
    {
        /// <summary>
        /// Runs all enabled rules matching <paramref name="triggerType"/> and the rule keyword.
        /// </summary>
        /// <param name="triggerType">A value from <see cref="Models.AutomationTriggerTypes"/>.</param>
        /// <param name="context">Trigger-specific data; may be null.</param>
        void Evaluate(string triggerType, AutomationRuleContext context);
    }
}