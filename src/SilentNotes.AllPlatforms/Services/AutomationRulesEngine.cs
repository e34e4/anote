// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using SilentNotes.Models;
using SilentNotes;
using SilentNotes.ViewModels;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Best-effort executor for local automation rules. Never throws to callers.
    /// </summary>
    public class AutomationRulesEngine : IAutomationRulesEngine
    {
        private readonly ISettingsService _settingsService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly IMessengerService _messengerService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutomationRulesEngine"/> class.
        /// </summary>
        public AutomationRulesEngine(
            ISettingsService settingsService,
            IRepositoryStorageService repositoryService,
            IMessengerService messengerService = null)
        {
            _settingsService = settingsService;
            _repositoryService = repositoryService;
            _messengerService = messengerService;
        }

        /// <inheritdoc/>
        public void Evaluate(string triggerType, AutomationRuleContext context)
        {
            if (string.IsNullOrWhiteSpace(triggerType))
                return;

            try
            {
                SettingsModel settings = _settingsService?.LoadSettingsOrDefault();
                if (settings?.AutomationRules == null || settings.AutomationRules.Count == 0)
                    return;

                string haystack = BuildHaystack(triggerType, context);
                foreach (AutomationRuleModel rule in settings.AutomationRules)
                {
                    if (rule == null || !rule.Enabled)
                        continue;
                    if (!string.Equals(rule.TriggerType, triggerType, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!MatchesKeyword(rule.MatchKeyword, haystack))
                        continue;

                    try
                    {
                        ExecuteRule(rule, context);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static string BuildHaystack(string triggerType, AutomationRuleContext context)
        {
            context ??= new AutomationRuleContext();
            if (string.Equals(triggerType, AutomationTriggerTypes.SmsReceived, StringComparison.OrdinalIgnoreCase))
                return (context.SmsBody ?? string.Empty).Trim();

            if (string.Equals(triggerType, AutomationTriggerTypes.RssKeywordMatch, StringComparison.OrdinalIgnoreCase))
            {
                return ((context.AppendText ?? string.Empty) + " " + (context.FeedTitle ?? string.Empty)).Trim();
            }

            return (context.AppendText ?? string.Empty).Trim();
        }

        private static bool MatchesKeyword(string keyword, string haystack)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return true;
            if (string.IsNullOrEmpty(haystack))
                return false;
            return haystack.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ExecuteRule(AutomationRuleModel rule, AutomationRuleContext context)
        {
            context ??= new AutomationRuleContext();
            string action = rule.ActionType ?? string.Empty;
            if (string.Equals(action, AutomationActionTypes.OpenTab, StringComparison.OrdinalIgnoreCase))
            {
                int? tabIndex = ResolveTabIndex(rule.ActionParam);
                if (tabIndex != null)
                    OpenTab(tabIndex.Value);
                return;
            }

            if (string.Equals(action, AutomationActionTypes.OpenRssTab, StringComparison.OrdinalIgnoreCase))
            {
                OpenTab(RssViewModel.TabIndex);
                return;
            }

            if (string.Equals(action, AutomationActionTypes.OpenDailyNote, StringComparison.OrdinalIgnoreCase))
            {
                OpenDailyNote();
                return;
            }

            if (string.Equals(action, AutomationActionTypes.AppendToNote, StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, AutomationActionTypes.CreateNote, StringComparison.OrdinalIgnoreCase))
            {
                string text = context.AppendText;
                if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(context.SmsBody))
                    text = FormatSmsCapture(context.SmsPhone, context.SmsBody);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                if (repository == null
                    || object.ReferenceEquals(repository, NoteRepositoryModel.InvalidRepository))
                    return;

                bool repoChanged = false;
                if (string.Equals(action, AutomationActionTypes.CreateNote, StringComparison.OrdinalIgnoreCase))
                {
                    string title = string.IsNullOrWhiteSpace(rule.ActionParam) ? "Automation" : rule.ActionParam.Trim();
                    CaptureNoteHelper.CreateTextNote(repository, title, text);
                    repoChanged = true;
                }
                else
                {
                    repoChanged = AppendToNoteTarget(repository, rule.ActionParam, text);
                }

                if (repoChanged)
                {
                    _repositoryService.TrySaveRepository(repository);
                    _messengerService?.Send(new NotesChangedMessage());
                }
            }
        }

        private static string FormatSmsCapture(string phone, string body)
        {
            string from = string.IsNullOrWhiteSpace(phone) ? "SMS" : phone.Trim();
            return from + "\n" + (body ?? string.Empty).Trim();
        }

        private static bool AppendToNoteTarget(NoteRepositoryModel repository, string param, string text)
        {
            string target = (param ?? string.Empty).Trim();
            if (string.Equals(target, "scratch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, "scratchpad", StringComparison.OrdinalIgnoreCase))
            {
                CaptureNoteHelper.AppendScratchpad(repository, text);
                return true;
            }

            if (string.Equals(target, "daily", StringComparison.OrdinalIgnoreCase))
            {
                NoteModel daily = DailyNoteHelper.EnsureTodayWithoutCalendar(repository, out _);
                AppendParagraph(daily, text);
                daily.RefreshModifiedAt();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                NoteModel note = repository.Notes.FirstOrDefault(candidate =>
                    string.Equals(NoteHtmlUtils.GetTitle(candidate.HtmlContent), target, StringComparison.OrdinalIgnoreCase));
                if (note != null)
                {
                    AppendParagraph(note, text);
                    note.RefreshModifiedAt();
                    return true;
                }
            }

            CaptureNoteHelper.AppendScratchpad(repository, text);
            return true;
        }

        private static void AppendParagraph(NoteModel note, string text)
        {
            if (note == null)
                return;

            string encoded = WebUtility.HtmlEncode((text ?? string.Empty).Trim()).Replace("\n", "<br/>");
            string block = "<p>" + encoded + "</p>";
            string html = note.HtmlContent ?? string.Empty;
            note.HtmlContent = string.IsNullOrEmpty(html) ? block : html + block;
        }

        private static int? ResolveTabIndex(string param)
        {
            string raw = (param ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
                return 0;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                && MainViewTabCatalog.TryGetTab(index, out _))
                return index;

            foreach (MainViewTabCatalog.TabDescriptor tab in MainViewTabCatalog.AllTabs)
            {
                if (string.Equals(tab.LanguageKey, raw, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tab.Emoji, raw, StringComparison.Ordinal))
                    return tab.Index;
            }

            string lowered = raw.ToLowerInvariant();
            foreach (MainViewTabCatalog.TabDescriptor tab in MainViewTabCatalog.AllTabs)
            {
                if (tab.LanguageKey != null && tab.LanguageKey.IndexOf(lowered, StringComparison.OrdinalIgnoreCase) >= 0)
                    return tab.Index;
            }

            return null;
        }

        private void OpenTab(int tabIndex)
        {
            TabIntentRequest.Pending = new TabIntentRequest { TabIndex = tabIndex };
            _messengerService?.Send(new TabIntentMessage());
        }

        private void OpenDailyNote()
        {
            _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            if (repository == null
                || object.ReferenceEquals(repository, NoteRepositoryModel.InvalidRepository))
            {
                OpenTab(0);
                return;
            }

            NoteModel note = DailyNoteHelper.EnsureTodayWithoutCalendar(repository, out bool changed);
            if (changed)
                _repositoryService.TrySaveRepository(repository);
            _messengerService?.Send(new NotesChangedMessage());
            if (note != null && note.Id != Guid.Empty)
            {
                RouteIntentRequest.Pending = new RouteIntentRequest
                {
                    Route = RouteNames.Combine(RouteNames.Note, note.Id),
                };
                _messengerService?.Send(new RouteIntentMessage());
            }
            else
            {
                OpenTab(0);
            }
        }
    }
}