// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single local automation rule: when a trigger fires, run an action.
    /// </summary>
    public class AutomationRuleModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AutomationRuleModel"/> class.
        /// </summary>
        public AutomationRuleModel()
        {
            Id = Guid.NewGuid();
            Enabled = true;
        }

        /// <summary>Gets or sets the stable rule id.</summary>
        [XmlAttribute(AttributeName = "id")]
        public Guid Id { get; set; }

        /// <summary>Gets or sets a value indicating whether this rule is active.</summary>
        [XmlAttribute(AttributeName = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>Gets or sets a display name.</summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>Gets or sets the trigger type (see <see cref="AutomationTriggerTypes"/>).</summary>
        [XmlAttribute(AttributeName = "trigger")]
        public string TriggerType { get; set; }

        /// <summary>Gets or sets an optional case-insensitive keyword; empty matches any.</summary>
        [XmlElement("match_keyword")]
        public string MatchKeyword { get; set; }

        /// <summary>Gets or sets the action type (see <see cref="AutomationActionTypes"/>).</summary>
        [XmlAttribute(AttributeName = "action")]
        public string ActionType { get; set; }

        /// <summary>Gets or sets an optional action parameter (tab index/name, note title, etc.).</summary>
        [XmlElement("action_param")]
        public string ActionParam { get; set; }
    }

    /// <summary>Known automation trigger type names.</summary>
    public static class AutomationTriggerTypes
    {
        /// <summary>Incoming SMS stored by <c>SmsDeliverReceiver</c>.</summary>
        public const string SmsReceived = "SmsReceived";

        /// <summary>Alarm started ringing.</summary>
        public const string AlarmFired = "AlarmFired";

        /// <summary>User stopped a ringing alarm (not snooze).</summary>
        public const string AlarmDismissed = "AlarmDismissed";

        /// <summary>New or updated RSS item matched during refresh.</summary>
        public const string RssKeywordMatch = "RssKeywordMatch";

        /// <summary>Countdown timer finished.</summary>
        public const string TimerFinished = "TimerFinished";
    }

    /// <summary>Known automation action type names.</summary>
    public static class AutomationActionTypes
    {
        /// <summary>Activate a main tab (<see cref="AutomationRuleModel.ActionParam"/> = index or name).</summary>
        public const string OpenTab = "OpenTab";

        /// <summary>Append <see cref="AutomationRuleContext.AppendText"/> to a note.</summary>
        public const string AppendToNote = "AppendToNote";

        /// <summary>Create a note with a title prefix plus captured text.</summary>
        public const string CreateNote = "CreateNote";

        /// <summary>Open today's daily journal note.</summary>
        public const string OpenDailyNote = "OpenDailyNote";

        /// <summary>Open the RSS tab.</summary>
        public const string OpenRssTab = "OpenRssTab";
    }
}