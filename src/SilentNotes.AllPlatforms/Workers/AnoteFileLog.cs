// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>Severity markers written into <c>anote.log</c> (used as coloring cues in the Log page).</summary>
    public enum AnoteLogSeverity
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>One parsed line or day header from <c>/sdcard/anote/anote.log</c>.</summary>
    public sealed class AnoteLogEntry
    {
        public bool IsDayHeader { get; init; }

        public string Day { get; init; }

        public DateTime? Timestamp { get; init; }

        public AnoteLogSeverity Severity { get; init; }

        public string Page { get; init; }

        public string Message { get; init; }

        public string RawLine { get; init; }

        public string SeverityCode
        {
            get
            {
                return Severity switch
                {
                    AnoteLogSeverity.Debug => "D",
                    AnoteLogSeverity.Warn => "W",
                    AnoteLogSeverity.Error => "E",
                    _ => "I",
                };
            }
        }
    }

    /// <summary>
    /// Appends diagnostic lines to <see cref="AnotePaths.LogFile"/> (Android: <c>/sdcard/anote/anote.log</c>).
    /// Lines include a day header when the calendar day changes, a severity letter for coloring,
    /// and a page/feature tag for filtering.
    /// </summary>
    public static class AnoteFileLog
    {
        private static readonly object Gate = new object();
        private static string _lastDayHeader;

        private static readonly Regex EntryRegex = new Regex(
            @"^(?<time>\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)\s+(?<sev>[DIWE])\s+(?<page>[^\s]+)\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex DayHeaderRegex = new Regex(
            @"^========\s+DAY\s+(?<day>\d{4}-\d{2}-\d{2})\s+========\s*$",
            RegexOptions.Compiled);

        /// <summary>Appends an info-level line (legacy overload; page was previously the log file name).</summary>
        public static void Append(string page, string message)
        {
            Write(GuessSeverity(message), page, message);
        }

        public static void Debug(string page, string message)
        {
            Write(AnoteLogSeverity.Debug, page, message);
        }

        public static void Info(string page, string message)
        {
            Write(AnoteLogSeverity.Info, page, message);
        }

        public static void Warn(string page, string message)
        {
            Write(AnoteLogSeverity.Warn, page, message);
        }

        public static void Warn(string page, string message, Exception ex)
        {
            Write(AnoteLogSeverity.Warn, page, FormatWithException(message, ex));
        }

        public static void Error(string page, string message)
        {
            Write(AnoteLogSeverity.Error, page, message);
        }

        public static void Error(string page, string message, Exception ex)
        {
            Write(AnoteLogSeverity.Error, page, FormatWithException(message, ex));
        }

        private static string FormatWithException(string message, Exception ex)
        {
            string detail = message ?? string.Empty;
            if (ex != null)
                detail = string.IsNullOrEmpty(detail) ? ex.ToString() : detail + ": " + ex;
            return detail;
        }

        /// <summary>Writes a structured log line to <see cref="AnotePaths.LogFile"/>.</summary>
        public static void Write(AnoteLogSeverity severity, string page, string message)
        {
            try
            {
                AnotePaths.EnsureDirectories();
                string path = AnotePaths.LogFile;
                string pageTag = NormalizePage(page);
                string sev = SeverityToCode(severity);
                DateTime now = DateTime.Now;
                string day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string time = now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                string text = (message ?? string.Empty).Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ');

                lock (Gate)
                {
                    var sb = new StringBuilder(160);
                    if (!string.Equals(_lastDayHeader, day, StringComparison.Ordinal))
                    {
                        if (File.Exists(path) && new FileInfo(path).Length > 0)
                            sb.AppendLine();
                        sb.Append("======== DAY ").Append(day).AppendLine(" ========");
                        _lastDayHeader = day;
                    }

                    sb.Append(time).Append(' ')
                        .Append(sev).Append(' ')
                        .Append(pageTag).Append(' ')
                        .Append(text)
                        .AppendLine();

                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Reads and parses <see cref="AnotePaths.LogFile"/> (newest entries last).</summary>
        public static IReadOnlyList<AnoteLogEntry> ReadEntries(int maxLines = 5000)
        {
            string path = AnotePaths.LogFile;
            if (!File.Exists(path))
                return Array.Empty<AnoteLogEntry>();

            string[] lines;
            try
            {
                lock (Gate)
                {
                    lines = File.ReadAllLines(path, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                return Array.Empty<AnoteLogEntry>();
            }

            if (maxLines > 0 && lines.Length > maxLines)
            {
                int skip = lines.Length - maxLines;
                var trimmed = new string[maxLines];
                Array.Copy(lines, skip, trimmed, 0, maxLines);
                lines = trimmed;
            }

            string currentDay = null;
            var result = new List<AnoteLogEntry>(lines.Length);
            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                Match dayMatch = DayHeaderRegex.Match(raw.Trim());
                if (dayMatch.Success)
                {
                    currentDay = dayMatch.Groups["day"].Value;
                    result.Add(new AnoteLogEntry
                    {
                        IsDayHeader = true,
                        Day = currentDay,
                        RawLine = raw,
                        Page = string.Empty,
                        Message = currentDay,
                        Severity = AnoteLogSeverity.Info,
                    });
                    continue;
                }

                Match entryMatch = EntryRegex.Match(raw.Trim());
                if (entryMatch.Success)
                {
                    string dayPart = currentDay ?? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    DateTime? ts = null;
                    if (DateTime.TryParseExact(
                            dayPart + " " + entryMatch.Groups["time"].Value,
                            new[] { "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss" },
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateTime parsed))
                    {
                        ts = parsed;
                    }

                    result.Add(new AnoteLogEntry
                    {
                        IsDayHeader = false,
                        Day = dayPart,
                        Timestamp = ts,
                        Severity = CodeToSeverity(entryMatch.Groups["sev"].Value),
                        Page = entryMatch.Groups["page"].Value,
                        Message = entryMatch.Groups["msg"].Value,
                        RawLine = raw,
                    });
                    continue;
                }

                // Legacy / free-form lines (e.g. old ytdlp.log style).
                result.Add(new AnoteLogEntry
                {
                    IsDayHeader = false,
                    Day = currentDay,
                    Severity = GuessSeverity(raw),
                    Page = "Legacy",
                    Message = raw,
                    RawLine = raw,
                });
            }

            return result;
        }

        /// <summary>Distinct page tags found in the log (sorted).</summary>
        public static IReadOnlyList<string> ListPages(IEnumerable<AnoteLogEntry> entries = null)
        {
            IEnumerable<AnoteLogEntry> source = entries ?? ReadEntries();
            return source
                .Where(e => !e.IsDayHeader && !string.IsNullOrWhiteSpace(e.Page))
                .Select(e => e.Page)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizePage(string page)
        {
            if (string.IsNullOrWhiteSpace(page))
                return "App";

            string trimmed = page.Trim();
            // Legacy file-name style tags → page names.
            if (string.Equals(trimmed, "ytdlp", StringComparison.OrdinalIgnoreCase))
                return "YtDlp";
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "App";
            return trimmed.Length > 32 ? trimmed.Substring(0, 32) : trimmed;
        }

        private static string SeverityToCode(AnoteLogSeverity severity)
        {
            return severity switch
            {
                AnoteLogSeverity.Debug => "D",
                AnoteLogSeverity.Warn => "W",
                AnoteLogSeverity.Error => "E",
                _ => "I",
            };
        }

        private static AnoteLogSeverity CodeToSeverity(string code)
        {
            return code switch
            {
                "D" => AnoteLogSeverity.Debug,
                "W" => AnoteLogSeverity.Warn,
                "E" => AnoteLogSeverity.Error,
                _ => AnoteLogSeverity.Info,
            };
        }

        private static AnoteLogSeverity GuessSeverity(string message)
        {
            if (string.IsNullOrEmpty(message))
                return AnoteLogSeverity.Info;

            string m = message.ToLowerInvariant();
            if (m.Contains("exception") || m.Contains(" failed") || m.Contains("failure")
                || m.Contains("error") || m.Contains("crash") || m.Contains("nre")
                || m.StartsWith("fail", StringComparison.Ordinal))
            {
                return AnoteLogSeverity.Error;
            }

            if (m.Contains("warn") || m.Contains("fallback") || m.Contains("denied")
                || m.Contains("timeout") || m.Contains("missing"))
            {
                return AnoteLogSeverity.Warn;
            }

            return AnoteLogSeverity.Info;
        }
    }
}
