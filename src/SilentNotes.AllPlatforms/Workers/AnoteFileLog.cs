// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

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

    /// <summary>Classifies the kind of error recorded in <c>anote.log</c>.</summary>
    public enum AnoteErrorKind
    {
        None = 0,
        Network = 1,
        Auth = 2,
        Storage = 3,
        Permission = 4,
        Timeout = 5,
        Parse = 6,
        Media = 7,
        Crash = 8,
        Database = 9,
        Unknown = 10,
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

        /// <summary>Classification of the error when <see cref="Severity"/> is <see cref="AnoteLogSeverity.Error"/>.</summary>
        public AnoteErrorKind ErrorKind { get; init; }

        /// <summary>Indicates whether this error entry (or an identical error description) has already been sent via email.</summary>
        public bool EmailSent { get; init; }

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
    /// a page/feature tag for filtering, and for errors an error-kind classification plus an
    /// email-sent flag with automatic deduplicated email reporting when a mail account is configured.
    /// </summary>
    public static class AnoteFileLog
    {
        private static readonly object Gate = new object();
        private static readonly object EmailGate = new object();
        private static string _lastDayHeader;
        private static HashSet<string> _sentErrorKeys;
        private static readonly HashSet<string> InFlightErrorKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<PendingErrorReport> PendingReports = new ConcurrentQueue<PendingErrorReport>();
        private static int _emailWorkerRunning;

        /// <summary>
        /// Optional platform hook returning the current device name (e.g. "Google Pixel 8").
        /// Falls back to <see cref="Environment.MachineName"/> when not configured.
        /// </summary>
        public static Func<string> DeviceNameProvider { get; set; }

        /// <summary>Result of a manual flush of unsent Crash/Error log emails.</summary>
        public sealed class ErrorEmailFlushResult
        {
            public int CandidateCount { get; init; }

            public int SentCount { get; init; }

            public int FailedCount { get; init; }

            public string Detail { get; init; } = string.Empty;
        }

        private sealed class PendingErrorReport
        {
            public string Signature { get; init; }

            public AnoteErrorKind ErrorKind { get; init; }

            public string PageTag { get; init; }

            public string Message { get; init; }

            public DateTime Timestamp { get; init; }

            public string WrittenLine { get; init; }

            public IReadOnlyList<string> RelatedLines { get; init; }
        }

        private static readonly Regex EntryRegex = new Regex(
            @"^(?<time>\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)\s+(?<sev>[DIWE])\s+(?<page>[^\s]+)\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex ErrorMetaRegex = new Regex(
            @"^\[kind=(?<kind>[A-Za-z]+)\s+emailed=(?<emailed>[01])\]\s*(?<msg>.*)$",
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
                // Normalize all newline styles to " | " so stack frames stay detectable for signatures.
                string text = (message ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Replace("\n", " | ");

                AnoteErrorKind errorKind = AnoteErrorKind.None;
                bool alreadySent = false;
                string errorSignature = null;
                string writtenLine = null;

                if (severity == AnoteLogSeverity.Error)
                {
                    errorKind = ClassifyError(pageTag, text);
                    errorSignature = BuildErrorSignature(pageTag, text);
                    alreadySent = IsErrorAlreadySent(errorSignature);
                }

                lock (Gate)
                {
                    var sb = new StringBuilder(180);
                    if (!string.Equals(_lastDayHeader, day, StringComparison.Ordinal))
                    {
                        if (File.Exists(path) && new FileInfo(path).Length > 0)
                            sb.AppendLine();
                        sb.Append("======== DAY ").Append(day).AppendLine(" ========");
                        _lastDayHeader = day;
                    }

                    var lineBuilder = new StringBuilder(160);
                    lineBuilder.Append(time).Append(' ')
                        .Append(sev).Append(' ')
                        .Append(pageTag).Append(' ');
                    if (severity == AnoteLogSeverity.Error)
                    {
                        lineBuilder.Append("[kind=").Append(errorKind)
                            .Append(" emailed=").Append(alreadySent ? '1' : '0')
                            .Append("] ");
                    }
                    lineBuilder.Append(text);
                    writtenLine = lineBuilder.ToString();

                    sb.AppendLine(writtenLine);
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }

                if (severity == AnoteLogSeverity.Error
                    && !alreadySent
                    && !string.IsNullOrEmpty(errorSignature)
                    && !string.Equals(pageTag, "ErrorReport", StringComparison.OrdinalIgnoreCase))
                {
                    QueueErrorEmailReport(errorSignature, errorKind, pageTag, text, now, writtenLine);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Classifies an error log message into a coarse <see cref="AnoteErrorKind"/>.</summary>
        public static AnoteErrorKind ClassifyError(string page, string message)
        {
            string combined = ((page ?? string.Empty) + " " + (message ?? string.Empty)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(combined))
                return AnoteErrorKind.Unknown;

            if (combined.Contains("nullreferenceexception")
                || combined.Contains("objectdisposedexception")
                || combined.Contains("stackoverflow")
                || combined.Contains("outofmemory")
                || combined.Contains("sigsegv")
                || combined.Contains("unhandled")
                || combined.Contains("fatal")
                || combined.Contains("indexoutofrange")
                || combined.Contains("argumentnullexception")
                || combined.Contains("invalidoperationexception")
                || combined.Contains("crash"))
                return AnoteErrorKind.Crash;

            if (combined.Contains("timeout")
                || combined.Contains("timed out")
                || combined.Contains("taskcanceledexception")
                || combined.Contains("operationcanceledexception"))
                return AnoteErrorKind.Timeout;

            if (combined.Contains("unauthorized")
                || combined.Contains(" 401")
                || combined.Contains(" 403")
                || combined.Contains("oauth")
                || combined.Contains("authentication")
                || combined.Contains("invalid credentials")
                || combined.Contains("token")
                || combined.Contains("login"))
                return AnoteErrorKind.Auth;

            if (combined.Contains("securityexception")
                || combined.Contains("permission")
                || combined.Contains("eacces")
                || combined.Contains("access denied")
                || combined.Contains("denied"))
                return AnoteErrorKind.Permission;

            if (combined.Contains("httprequestexception")
                || combined.Contains("socketexception")
                || combined.Contains("webexception")
                || combined.Contains("unknownhost")
                || combined.Contains("dns")
                || combined.Contains("ssl")
                || combined.Contains("tls")
                || combined.Contains("connection")
                || combined.Contains("network")
                || combined.Contains("smtp")
                || combined.Contains("imap")
                || combined.Contains("webdav"))
                return AnoteErrorKind.Network;

            if (combined.Contains("ioexception")
                || combined.Contains("filenotfound")
                || combined.Contains("directorynotfound")
                || combined.Contains("unauthorizedaccessexception")
                || combined.Contains("enospc")
                || combined.Contains("storage")
                || combined.Contains("disk"))
                return AnoteErrorKind.Storage;

            if (combined.Contains("jsonexception")
                || combined.Contains("xmlexception")
                || combined.Contains("formatexception")
                || combined.Contains("invalidcast")
                || combined.Contains("deserializ")
                || combined.Contains("serializ")
                || combined.Contains("parse"))
                return AnoteErrorKind.Parse;

            if (combined.Contains("sqlite")
                || combined.Contains("database")
                || combined.Contains("cursor"))
                return AnoteErrorKind.Database;

            if (combined.Contains("mediaplayer")
                || combined.Contains("ffmpeg")
                || combined.Contains("yt-dlp")
                || combined.Contains("ytdlp")
                || combined.Contains("codec")
                || combined.Contains("camera"))
                return AnoteErrorKind.Media;

            return AnoteErrorKind.Unknown;
        }

        /// <summary>
        /// Builds a normalized signature from the page and core error description so repeated
        /// occurrences of the same error are deduplicated before sending email reports.
        /// </summary>
        public static string BuildErrorSignature(string page, string message)
        {
            string normalizedPage = NormalizePage(page).ToLowerInvariant();
            string core = (message ?? string.Empty).Trim();

            // Strip flattened stack frames so identical errors with varying frames match.
            int stackIndex = core.IndexOf(" |    at ", StringComparison.Ordinal);
            if (stackIndex < 0)
                stackIndex = core.IndexOf("    at ", StringComparison.Ordinal);
            if (stackIndex > 0)
                core = core.Substring(0, stackIndex);

            core = Regex.Replace(core, @"\s+", " ").Trim();
            // Stabilize volatile counters common in Blazor WebView desync / native crashes.
            core = Regex.Replace(core, @"\brender batch \d+", "render batch #", RegexOptions.IgnoreCase);
            core = Regex.Replace(core, @"\bnext batch should be \d+", "next batch should be #", RegexOptions.IgnoreCase);
            core = Regex.Replace(core, @"\b0x[0-9a-fA-F]+\b", "0x#");
            if (core.Length > 400)
                core = core.Substring(0, 400);

            return normalizedPage + "|" + core;
        }

        private static bool IsErrorAlreadySent(string signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;

            lock (EmailGate)
            {
                EnsureSentErrorKeysLoadedLocked();
                return _sentErrorKeys.Contains(signature);
            }
        }

        private static void EnsureSentErrorKeysLoadedLocked()
        {
            if (_sentErrorKeys != null)
                return;

            _sentErrorKeys = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string indexPath = AnotePaths.SentErrorsFile;
                if (File.Exists(indexPath))
                {
                    foreach (string line in File.ReadAllLines(indexPath, Encoding.UTF8))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            _sentErrorKeys.Add(line.Trim());
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void RecordErrorAsSent(string signature, string writtenLine, IEnumerable<string> relatedLines = null)
        {
            if (string.IsNullOrEmpty(signature))
                return;

            lock (EmailGate)
            {
                EnsureSentErrorKeysLoadedLocked();
                if (_sentErrorKeys.Add(signature))
                {
                    try
                    {
                        File.AppendAllText(AnotePaths.SentErrorsFile, signature + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            var linesToMark = new List<string>();
            if (!string.IsNullOrEmpty(writtenLine))
                linesToMark.Add(writtenLine);
            if (relatedLines != null)
                linesToMark.AddRange(relatedLines.Where(l => !string.IsNullOrEmpty(l)));

            MarkLogLinesAsEmailed(linesToMark);
        }

        private static void MarkLogLinesAsEmailed(IEnumerable<string> originalLines)
        {
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string originalLine in originalLines ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(originalLine) || !originalLine.Contains(" emailed=0] ", StringComparison.Ordinal))
                    continue;

                string updatedLine = originalLine.Replace(" emailed=0] ", " emailed=1] ");
                if (!string.Equals(originalLine, updatedLine, StringComparison.Ordinal))
                    replacements[originalLine] = updatedLine;
            }

            if (replacements.Count == 0)
                return;

            try
            {
                lock (Gate)
                {
                    string path = AnotePaths.LogFile;
                    if (!File.Exists(path))
                        return;

                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    bool changed = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (replacements.TryGetValue(lines[i], out string updated))
                        {
                            lines[i] = updated;
                            changed = true;
                        }
                    }

                    if (changed)
                        File.WriteAllLines(path, lines, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void QueueErrorEmailReport(
            string signature,
            AnoteErrorKind errorKind,
            string pageTag,
            string message,
            DateTime timestamp,
            string writtenLine)
        {
            lock (EmailGate)
            {
                EnsureSentErrorKeysLoadedLocked();
                if (_sentErrorKeys.Contains(signature) || !InFlightErrorKeys.Add(signature))
                    return;
            }

            PendingReports.Enqueue(new PendingErrorReport
            {
                Signature = signature,
                ErrorKind = errorKind,
                PageTag = pageTag,
                Message = message,
                Timestamp = timestamp,
                WrittenLine = writtenLine,
            });
            EnsureEmailWorker();
        }

        private static void EnsureEmailWorker()
        {
            if (Interlocked.CompareExchange(ref _emailWorkerRunning, 1, 0) != 0)
                return;

            _ = Task.Run(ProcessErrorEmailQueueAsync);
        }

        private static async Task ProcessErrorEmailQueueAsync()
        {
            try
            {
                IMailService mailService = null;
                List<MailAccountModel> accounts = null;

                while (PendingReports.TryDequeue(out PendingErrorReport item))
                {
                    try
                    {
                        string recipient = ResolveErrorReportRecipient();
                        if (string.IsNullOrEmpty(recipient))
                        {
                            LogEmailIssue("Error report email is not configured in Settings; error email not sent.");
                            continue;
                        }

                        if (mailService == null)
                        {
                            mailService = Ioc.Instance.GetService<IMailService>();
                            if (mailService == null)
                            {
                                LogEmailIssue("IMailService unavailable; error email not sent.");
                                continue;
                            }

                            // Load once per worker burst — mail store can be multi-MB with cached messages.
                            MailStoreModel store = mailService.LoadStore();
                            accounts = SelectSmtpAccounts(store, recipient);
                            if (accounts.Count == 0)
                            {
                                LogEmailIssue("No SMTP mail account configured; error email not sent.");
                                continue;
                            }
                        }

                        if (accounts == null || accounts.Count == 0)
                            continue;

                        bool sent = await TrySendErrorReportAsync(mailService, accounts, item, recipient).ConfigureAwait(false);
                        if (sent)
                            RecordErrorAsSent(item.Signature, item.WrittenLine, item.RelatedLines);
                        else
                            LogEmailIssue("All SMTP accounts failed sending error report for " + item.PageTag + ".");
                    }
                    catch (Exception ex)
                    {
                        LogEmailIssue("Error email worker failed: " + ex.Message);
                    }
                    finally
                    {
                        lock (EmailGate)
                        {
                            InFlightErrorKeys.Remove(item.Signature);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _emailWorkerRunning, 0);
                if (!PendingReports.IsEmpty)
                    EnsureEmailWorker();
            }
        }

        /// <summary>
        /// Sends email reports for log entries that are still marked unsent and classified as
        /// <see cref="AnoteErrorKind.Crash"/> or any other Error-severity kind.
        /// </summary>
        public static async Task<ErrorEmailFlushResult> SendUnsentErrorEmailsAsync()
        {
            IReadOnlyList<AnoteLogEntry> entries = ReadEntries();
            // All Error-severity lines still marked emailed=0 (Crash and every other error kind).
            var candidates = entries
                .Where(e => !e.IsDayHeader
                    && e.Severity == AnoteLogSeverity.Error
                    && !e.EmailSent)
                .ToList();

            if (candidates.Count == 0)
            {
                return new ErrorEmailFlushResult
                {
                    CandidateCount = 0,
                    Detail = "No unsent Crash/Error events.",
                };
            }

            string recipient = ResolveErrorReportRecipient();
            if (string.IsNullOrEmpty(recipient))
            {
                return new ErrorEmailFlushResult
                {
                    CandidateCount = candidates.Count,
                    FailedCount = candidates.Count,
                    Detail = "Error report email is not configured in Settings.",
                };
            }

            var groups = candidates
                .GroupBy(e => BuildErrorSignature(e.Page, e.Message), StringComparer.Ordinal)
                .ToList();

            IMailService mailService;
            try
            {
                mailService = Ioc.Instance.GetService<IMailService>();
            }
            catch (Exception ex)
            {
                return new ErrorEmailFlushResult
                {
                    CandidateCount = candidates.Count,
                    FailedCount = groups.Count,
                    Detail = "IMailService unavailable: " + ex.Message,
                };
            }

            if (mailService == null)
            {
                return new ErrorEmailFlushResult
                {
                    CandidateCount = candidates.Count,
                    FailedCount = groups.Count,
                    Detail = "IMailService unavailable.",
                };
            }

            MailStoreModel store = mailService.LoadStore();
            List<MailAccountModel> accounts = SelectSmtpAccounts(store, recipient);
            if (accounts.Count == 0)
            {
                return new ErrorEmailFlushResult
                {
                    CandidateCount = candidates.Count,
                    FailedCount = groups.Count,
                    Detail = "No SMTP mail account configured.",
                };
            }

            int sent = 0;
            int failed = 0;
            string lastError = null;
            foreach (IGrouping<string, AnoteLogEntry> group in groups)
            {
                AnoteLogEntry sample = group.First();
                var report = new PendingErrorReport
                {
                    Signature = group.Key,
                    ErrorKind = sample.ErrorKind,
                    PageTag = sample.Page,
                    Message = sample.Message,
                    Timestamp = sample.Timestamp ?? DateTime.Now,
                    WrittenLine = sample.RawLine,
                    RelatedLines = group.Select(e => e.RawLine).ToList(),
                };

                try
                {
                    bool ok = await TrySendErrorReportAsync(mailService, accounts, report, recipient).ConfigureAwait(false);
                    if (ok)
                    {
                        RecordErrorAsSent(report.Signature, report.WrittenLine, report.RelatedLines);
                        sent++;
                    }
                    else
                    {
                        failed++;
                        lastError = "SMTP send failed for all accounts.";
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    lastError = ex.Message;
                }
            }

            string detail;
            if (sent > 0 && failed == 0)
                detail = "Sent " + sent + " error report(s).";
            else if (sent == 0)
                detail = lastError ?? "Failed to send error reports.";
            else
                detail = "Sent " + sent + ", failed " + failed + (string.IsNullOrEmpty(lastError) ? "." : ": " + lastError);

            return new ErrorEmailFlushResult
            {
                CandidateCount = candidates.Count,
                SentCount = sent,
                FailedCount = failed,
                Detail = detail,
            };
        }

        /// <summary>Reads the Crash/Error report destination from Settings (empty = disabled).</summary>
        private static string ResolveErrorReportRecipient()
        {
            try
            {
                ISettingsService settingsService = Ioc.Instance.GetService<ISettingsService>();
                string configured = settingsService?.LoadSettingsOrDefault()?.ErrorReportEmail;
                if (!string.IsNullOrWhiteSpace(configured) && configured.Contains('@'))
                    return configured.Trim();
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static List<MailAccountModel> SelectSmtpAccounts(MailStoreModel store, string preferredRecipient)
        {
            return (store?.Accounts ?? Enumerable.Empty<MailAccountModel>())
                .Where(a => a != null
                    && !string.IsNullOrWhiteSpace(a.SmtpHost)
                    && !string.IsNullOrWhiteSpace(a.EmailAddress))
                // Prefer sending from the same mailbox that receives reports when possible.
                .OrderByDescending(a => string.Equals(a.EmailAddress, preferredRecipient, StringComparison.OrdinalIgnoreCase))
                .ThenBy(a => a.EmailAddress, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<bool> TrySendErrorReportAsync(
            IMailService mailService,
            IReadOnlyList<MailAccountModel> accounts,
            PendingErrorReport item,
            string recipient)
        {
            if (mailService == null || accounts == null || accounts.Count == 0 || item == null)
                return false;
            if (string.IsNullOrWhiteSpace(recipient))
                return false;

            string deviceName = ResolveDeviceName();
            string subject = string.Format(
                CultureInfo.InvariantCulture,
                "[aNote Error][{0}][{1}] {2}",
                deviceName,
                item.ErrorKind,
                item.PageTag);

            var body = new StringBuilder();
            body.AppendLine("aNote automatic error report");
            body.AppendLine("----------------------------");
            body.Append("Device: ").AppendLine(deviceName);
            body.Append("Time: ").AppendLine(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            body.Append("Error kind: ").AppendLine(item.ErrorKind.ToString());
            body.Append("Page / Feature: ").AppendLine(item.PageTag);
            if (item.RelatedLines != null && item.RelatedLines.Count > 1)
                body.Append("Occurrences in log: ").AppendLine(item.RelatedLines.Count.ToString(CultureInfo.InvariantCulture));
            body.AppendLine();
            body.AppendLine("Error message:");
            body.AppendLine(item.Message);

            string lastError = null;
            foreach (MailAccountModel account in accounts)
            {
                try
                {
                    var mailMessage = new MailMessageModel
                    {
                        AccountId = account.Id,
                        Folder = MailFolders.Sent,
                        From = string.IsNullOrWhiteSpace(account.DisplayName)
                            ? account.EmailAddress
                            : $"{account.DisplayName} <{account.EmailAddress}>",
                        To = recipient,
                        Subject = subject,
                        BodyText = body.ToString(),
                        DateUtc = DateTime.UtcNow,
                        IsRead = true,
                    };

                    MailOperationResult result = await mailService.SendMessageAsync(account, mailMessage).ConfigureAwait(false);
                    if (result != null && result.Success)
                        return true;

                    lastError = result?.ErrorMessage ?? "unknown SMTP error";
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            if (!string.IsNullOrEmpty(lastError))
                LogEmailIssue("SMTP send failed (" + item.PageTag + "): " + lastError);

            return false;
        }

        private static void LogEmailIssue(string message)
        {
            try
            {
                // Warn-level so the reporter never re-enters the error-email pipeline.
                Warn("ErrorReport", message);
            }
            catch (Exception)
            {
            }
        }

        private static string ResolveDeviceName()
        {
            try
            {
                string provided = DeviceNameProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(provided))
                    return provided.Trim();
            }
            catch (Exception)
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(Environment.MachineName))
                    return Environment.MachineName.Trim();
            }
            catch (Exception)
            {
            }

            return "Unknown device";
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

                    AnoteLogSeverity sev = CodeToSeverity(entryMatch.Groups["sev"].Value);
                    string page = entryMatch.Groups["page"].Value;
                    string msg = entryMatch.Groups["msg"].Value;
                    AnoteErrorKind kind = AnoteErrorKind.None;
                    bool emailSent = false;

                    Match metaMatch = ErrorMetaRegex.Match(msg);
                    if (metaMatch.Success)
                    {
                        Enum.TryParse(metaMatch.Groups["kind"].Value, ignoreCase: true, out kind);
                        emailSent = metaMatch.Groups["emailed"].Value == "1";
                        msg = metaMatch.Groups["msg"].Value;
                    }
                    else if (sev == AnoteLogSeverity.Error)
                    {
                        kind = ClassifyError(page, msg);
                    }

                    result.Add(new AnoteLogEntry
                    {
                        IsDayHeader = false,
                        Day = dayPart,
                        Timestamp = ts,
                        Severity = sev,
                        Page = page,
                        Message = msg,
                        RawLine = raw,
                        ErrorKind = kind,
                        EmailSent = emailSent,
                    });
                    continue;
                }

                // Legacy / free-form lines (e.g. old ytdlp.log style).
                AnoteLogSeverity legacySev = GuessSeverity(raw);
                result.Add(new AnoteLogEntry
                {
                    IsDayHeader = false,
                    Day = currentDay,
                    Severity = legacySev,
                    Page = "Legacy",
                    Message = raw,
                    RawLine = raw,
                    ErrorKind = legacySev == AnoteLogSeverity.Error ? ClassifyError("Legacy", raw) : AnoteErrorKind.None,
                    EmailSent = false,
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
