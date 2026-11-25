// Copyright © 2026 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Known values and helpers for note status, priority, and due date fields.
    /// </summary>
    public static class NoteMetadata
    {
        public const string StatusTodo = "todo";
        public const string StatusDoing = "doing";
        public const string StatusDone = "done";
        public const string StatusBlocked = "blocked";

        public const int PriorityLow = 0;
        public const int PriorityNormal = 1;
        public const int PriorityHigh = 2;
        public const int PriorityUrgent = 3;

        /// <summary>Filter sentinel: any priority.</summary>
        public const int FilterPriorityAny = -1;

        /// <summary>UI sentinel for "any status" (MudSelect cannot reliably bind empty string).</summary>
        public const string FilterStatusAny = "__any__";

        public static readonly IReadOnlyList<string> BoardColumnStatuses = new[]
        {
            StatusTodo,
            StatusDoing,
            StatusDone,
            StatusBlocked,
        };

        public static readonly IReadOnlyList<int> AllPriorities = new[]
        {
            PriorityLow,
            PriorityNormal,
            PriorityHigh,
            PriorityUrgent,
        };

        public static string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return string.Empty;
            return status.Trim().ToLowerInvariant();
        }

        public static bool IsValidStatus(string status)
        {
            status = NormalizeStatus(status);
            if (string.IsNullOrEmpty(status))
                return true;
            // FilterStatusAny is a UI sentinel, not a stored note status.
            if (status == FilterStatusAny)
                return false;
            return status == StatusTodo
                || status == StatusDoing
                || status == StatusDone
                || status == StatusBlocked;
        }

        public static bool IsValidPriority(int priority)
        {
            return priority >= PriorityLow && priority <= PriorityUrgent;
        }

        public static string GetBoardColumnKey(string status)
        {
            status = NormalizeStatus(status);
            return string.IsNullOrEmpty(status) ? string.Empty : status;
        }

        public static bool IsOverdue(DateTime? dueAtUtc, string status)
        {
            if (!dueAtUtc.HasValue)
                return false;
            if (string.Equals(NormalizeStatus(status), StatusDone, StringComparison.Ordinal))
                return false;
            DateTime dueDate = dueAtUtc.Value.Date;
            return dueDate < DateTime.UtcNow.Date;
        }

        public static string BuildSearchableSuffix(NoteModel note)
        {
            if (note == null)
                return string.Empty;
            string status = NormalizeStatus(note.Status);
            if (!string.IsNullOrEmpty(status))
                return status + " p" + note.Priority;
            if (note.Priority != PriorityLow)
                return "p" + note.Priority;
            return string.Empty;
        }

        public static string FormatDueDate(DateTime? dueAtUtc)
        {
            if (!dueAtUtc.HasValue)
                return string.Empty;
            return dueAtUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}