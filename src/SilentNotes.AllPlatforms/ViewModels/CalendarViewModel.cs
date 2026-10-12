// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model of the calendar tab. It shows a month grid of the device calendars and allows
    /// to add, edit and delete events with their reminders.
    /// </summary>
    public class CalendarViewModel : ViewModelBase
    {
        /// <summary>Number of days shown in the month grid (6 weeks).</summary>
        private const int GridDayCount = 42;

        /// <summary>Maximum delay between two clicks to be treated as a double click.</summary>
        private static readonly TimeSpan DoubleClickDelay = TimeSpan.FromMilliseconds(450);

        private readonly IDeviceCalendarService _calendarService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;

        private DateTime _visibleMonth;
        private DateTime? _selectedDate;
        private List<CalendarDayViewModel> _days = new List<CalendarDayViewModel>();
        private List<CalendarEventViewModel> _dayEvents = new List<CalendarEventViewModel>();
        private List<DeviceCalendarModel> _calendars = new List<DeviceCalendarModel>();
        private bool _hasPermission;
        private bool _isDayListVisible;
        private bool _isEditorVisible;
        private string _dayListTitle = string.Empty;
        private bool _hasLastClick;
        private bool _permissionRequested;
        private bool _initialized;
        private CalendarIntentRequest _pendingIntent;
        private DateTime _lastClickDate;
        private DateTime _lastClickTime;

        private long _draftEventId;
        private string _draftTitle = string.Empty;
        private string _draftDescription = string.Empty;
        private string _draftLocation = string.Empty;
        private string _draftDate = string.Empty;
        private string _draftStart = "09:00";
        private string _draftEnd = "10:00";
        private bool _draftAllDay;
        private long _draftCalendarId;
        private string _draftColorHex;
        private readonly List<int> _draftReminders = new List<int>();
        private int _draftReminderAmount = 15;
        private int _draftReminderUnit;

        /// <summary>
        /// Initializes a new instance of the <see cref="CalendarViewModel"/> class.
        /// </summary>
        public CalendarViewModel(IDeviceCalendarService calendarService, ILanguageService languageService, IFeedbackService feedbackService)
        {
            _calendarService = calendarService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _visibleMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            Weekdays = BuildWeekdays();
            EventColors = new[]
            {
                "#e53935", "#fb8c00", "#fdd835", "#43a047",
                "#00acc1", "#1e88e5", "#8e24aa", "#6d4c41",
            };
        }

        /// <summary>Gets a value indicating whether the platform supports device calendars.</summary>
        public bool IsSupported
        {
            get { return _calendarService.IsSupported; }
        }

        /// <summary>Gets a value indicating whether the calendars may be read and written.</summary>
        public bool HasPermission
        {
            get { return _hasPermission; }
            private set { SetProperty(ref _hasPermission, value); }
        }

        /// <summary>Gets the localized weekday headers, starting with Monday.</summary>
        public IReadOnlyList<string> Weekdays { get; }

        /// <summary>Gets the cells of the month grid.</summary>
        public IReadOnlyList<CalendarDayViewModel> Days
        {
            get { return _days; }
        }

        /// <summary>Gets the title of the visible month.</summary>
        public string MonthTitle
        {
            get { return _visibleMonth.ToString("Y", CultureInfo.CurrentCulture); }
        }

        /// <summary>Gets the events of the opened day.</summary>
        public IReadOnlyList<CalendarEventViewModel> DayEvents
        {
            get { return _dayEvents; }
        }

        /// <summary>Gets a value indicating whether the opened day has events.</summary>
        public bool HasDayEvents
        {
            get { return _dayEvents.Count > 0; }
        }

        /// <summary>Gets the title of the opened day event list.</summary>
        public string DayListTitle
        {
            get { return _dayListTitle; }
        }

        /// <summary>Gets or sets a value indicating whether the day event list is shown.</summary>
        public bool IsDayListVisible
        {
            get { return _isDayListVisible; }
            private set { SetProperty(ref _isDayListVisible, value); }
        }

        /// <summary>Gets or sets a value indicating whether the event editor is shown.</summary>
        public bool IsEditorVisible
        {
            get { return _isEditorVisible; }
            private set { SetProperty(ref _isEditorVisible, value); }
        }

        /// <summary>Gets the calendars which can be selected for a new event.</summary>
        public IReadOnlyList<DeviceCalendarModel> Calendars
        {
            get { return _calendars; }
        }

        /// <summary>Gets the selectable event colors.</summary>
        public IReadOnlyList<string> EventColors { get; }

        /// <summary>Gets a value indicating whether the editor creates a new event.</summary>
        public bool IsNewEvent
        {
            get { return _draftEventId == 0; }
        }

        /// <summary>Gets or sets the title of the drafted event.</summary>
        public string DraftTitle
        {
            get { return _draftTitle; }
            set { SetProperty(ref _draftTitle, value); }
        }

        /// <summary>Gets or sets the description of the drafted event.</summary>
        public string DraftDescription
        {
            get { return _draftDescription; }
            set { SetProperty(ref _draftDescription, value); }
        }

        /// <summary>Gets or sets the location of the drafted event.</summary>
        public string DraftLocation
        {
            get { return _draftLocation; }
            set { SetProperty(ref _draftLocation, value); }
        }

        /// <summary>Gets or sets the date of the drafted event, as "yyyy-MM-dd".</summary>
        public string DraftDate
        {
            get { return _draftDate; }
            set { SetProperty(ref _draftDate, value); }
        }

        /// <summary>Gets or sets the start time of the drafted event, as "HH:mm".</summary>
        public string DraftStart
        {
            get { return _draftStart; }
            set { SetProperty(ref _draftStart, value); }
        }

        /// <summary>Gets or sets the end time of the drafted event, as "HH:mm".</summary>
        public string DraftEnd
        {
            get { return _draftEnd; }
            set { SetProperty(ref _draftEnd, value); }
        }

        /// <summary>Gets or sets a value indicating whether the drafted event lasts all day.</summary>
        public bool DraftAllDay
        {
            get { return _draftAllDay; }
            set { SetProperty(ref _draftAllDay, value); }
        }

        /// <summary>Gets or sets the calendar of the drafted event.</summary>
        public long DraftCalendarId
        {
            get { return _draftCalendarId; }
            set
            {
                if (SetProperty(ref _draftCalendarId, value))
                    OnPropertyChanged(nameof(DraftCalendarLabel));
            }
        }

        /// <summary>Gets the label of the selected calendar.</summary>
        public string DraftCalendarLabel
        {
            get
            {
                DeviceCalendarModel calendar = _calendars.Find(c => c.Id == _draftCalendarId);
                return calendar?.DisplayName ?? string.Empty;
            }
        }

        /// <summary>Gets or sets the color of the drafted event, null to use the calendar color.</summary>
        public string DraftColorHex
        {
            get { return _draftColorHex; }
            set { SetProperty(ref _draftColorHex, value); }
        }

        /// <summary>Gets the reminders of the drafted event, as minutes before it starts.</summary>
        public IReadOnlyList<int> DraftReminders
        {
            get { return _draftReminders; }
        }

        /// <summary>Gets a value indicating whether the drafted event has reminders.</summary>
        public bool HasDraftReminders
        {
            get { return _draftReminders.Count > 0; }
        }

        /// <summary>Gets or sets the amount of the reminder which is about to be added.</summary>
        public int DraftReminderAmount
        {
            get { return _draftReminderAmount; }
            set { SetProperty(ref _draftReminderAmount, value); }
        }

        /// <summary>Gets or sets the unit of the reminder which is about to be added (0=min, 1=h, 2=d).</summary>
        public int DraftReminderUnit
        {
            get { return _draftReminderUnit; }
            set { SetProperty(ref _draftReminderUnit, value); }
        }

        /// <summary>Gets the text of a reminder.</summary>
        public string FormatReminder(int minutes)
        {
            if (minutes <= 0)
                return _languageService["cal_reminder_at_time"];
            if (minutes % 1440 == 0)
                return string.Format(_languageService["cal_reminder_days_before"], minutes / 1440);
            if (minutes % 60 == 0)
                return string.Format(_languageService["cal_reminder_hours_before"], minutes / 60);
            return string.Format(_languageService["cal_reminder_min_before"], minutes);
        }

        /// <summary>Selects the color of the drafted event.</summary>
        public void SelectColor(string colorHex)
        {
            DraftColorHex = colorHex;
        }

        /// <summary>Adds the configured reminder to the drafted event.</summary>
        public void AddReminder()
        {
            int amount = _draftReminderAmount;
            if (amount <= 0)
                return;

            int minutes = _draftReminderUnit switch
            {
                1 => amount * 60,
                2 => amount * 1440,
                _ => amount,
            };
            if (!_draftReminders.Contains(minutes))
                _draftReminders.Add(minutes);
            _draftReminders.Sort();
            OnPropertyChanged(nameof(DraftReminders));
            OnPropertyChanged(nameof(HasDraftReminders));
        }

        /// <summary>Removes a reminder from the drafted event.</summary>
        public void RemoveReminder(int minutes)
        {
            if (_draftReminders.Remove(minutes))
            {
                OnPropertyChanged(nameof(DraftReminders));
                OnPropertyChanged(nameof(HasDraftReminders));
            }
        }

        /// <summary>
        /// Initializes the calendar, asks for the permission if necessary and loads the events.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!_calendarService.IsSupported)
                return;

            HasPermission = _calendarService.HasPermission;
            if (!HasPermission && !_permissionRequested)
            {
                _permissionRequested = true;
                await GrantPermissionAsync();
            }

            await ReloadCalendarsAsync();
            await RebuildMonthAsync();

            _initialized = true;
            if (_pendingIntent != null)
            {
                CalendarIntentRequest request = _pendingIntent;
                _pendingIntent = null;
                await ApplyIntentRequestAsync(request);
            }
        }

        /// <summary>Stores a calendar intent which should be applied after initialization.</summary>
        public void QueueIntent(CalendarIntentRequest request)
        {
            _pendingIntent = request;
        }

        /// <summary>
        /// Handles a calendar intent which was sent by another application. When the calendar is
        /// not loaded yet, the request is remembered and applied after initialization.
        /// </summary>
        public Task HandleIntentAsync(CalendarIntentRequest request)
        {
            if (request == null)
                return Task.CompletedTask;
            if (!_initialized)
            {
                _pendingIntent = request;
                return Task.CompletedTask;
            }
            return ApplyIntentRequestAsync(request);
        }

        private async Task ApplyIntentRequestAsync(CalendarIntentRequest request)
        {
            try
            {
                HasPermission = _calendarService.HasPermission;
                if (!HasPermission)
                    return;

                // A new request replaces whatever was open before.
                IsEditorVisible = false;
                IsDayListVisible = false;

                switch (request.Action)
                {
                    case CalendarIntentAction.OpenDate:
                        await GoToDateAsync(request.Date ?? DateTime.Today, true);
                        break;
                    case CalendarIntentAction.OpenEvent:
                        await OpenEventAsync(request.EventId, false);
                        break;
                    case CalendarIntentAction.EditEvent:
                        await OpenEventAsync(request.EventId, true);
                        break;
                    case CalendarIntentAction.NewEvent:
                        await PrepareNewEventAsync(request);
                        break;
                }
            }
            catch (Exception)
            {
            }
        }

        private async Task GoToDateAsync(DateTime date, bool openDayList)
        {
            _visibleMonth = new DateTime(date.Year, date.Month, 1);
            _selectedDate = date.Date;
            await RebuildMonthAsync();
            if (openDayList)
                await OpenDayAsync(date.Date);
        }

        private async Task OpenEventAsync(long eventId, bool openEditor)
        {
            DeviceEventModel model = await _calendarService.GetEventAsync(eventId);
            if (model == null)
                return;

            DateTime date = model.AllDay ? model.StartUtc.Date : model.StartUtc.ToLocalTime().Date;
            _visibleMonth = new DateTime(date.Year, date.Month, 1);
            _selectedDate = date;
            await RebuildMonthAsync();
            await OpenDayAsync(date);
            if (openEditor)
                EditEvent(new CalendarEventViewModel(model));
        }

        private async Task PrepareNewEventAsync(CalendarIntentRequest request)
        {
            DateTime date = request.Date ?? DateTime.Today;
            _visibleMonth = new DateTime(date.Year, date.Month, 1);
            _selectedDate = date;
            await RebuildMonthAsync();

            AddEvent();
            if (!IsEditorVisible)
                return;

            if (!string.IsNullOrEmpty(request.Title))
                DraftTitle = request.Title;
            if (!string.IsNullOrEmpty(request.Description))
                DraftDescription = request.Description;
            if (!string.IsNullOrEmpty(request.Location))
                DraftLocation = request.Location;
            DraftAllDay = request.AllDay;

            if (!request.AllDay && request.BeginUtc.HasValue)
            {
                DateTime localStart = request.BeginUtc.Value.ToLocalTime();
                DateTime localEnd = (request.EndUtc ?? request.BeginUtc.Value.AddHours(1)).ToLocalTime();
                _draftStart = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
                _draftEnd = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(DraftStart));
                OnPropertyChanged(nameof(DraftEnd));
            }
        }

        /// <summary>Requests the calendar permissions from the user.</summary>
        public async Task GrantPermissionAsync()
        {
            HasPermission = await _calendarService.RequestPermissionAsync();
            if (HasPermission)
            {
                await ReloadCalendarsAsync();
                await RebuildMonthAsync();
            }
        }

        private async Task ReloadCalendarsAsync()
        {
            if (!_calendarService.HasPermission)
                return;
            try
            {
                _calendars = (await _calendarService.GetCalendarsAsync()).ToList();
                OnPropertyChanged(nameof(Calendars));
            }
            catch (Exception)
            {
                _calendars = new List<DeviceCalendarModel>();
            }
        }

        /// <summary>Shows the previous month.</summary>
        public Task ShowPreviousMonthAsync()
        {
            return ChangeMonthAsync(-1);
        }

        /// <summary>Shows the next month.</summary>
        public Task ShowNextMonthAsync()
        {
            return ChangeMonthAsync(1);
        }

        /// <summary>Shows the current month and selects today.</summary>
        public async Task GoToTodayAsync()
        {
            _visibleMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _selectedDate = DateTime.Today;
            await RebuildMonthAsync();
        }

        private async Task ChangeMonthAsync(int offset)
        {
            _visibleMonth = _visibleMonth.AddMonths(offset);
            await RebuildMonthAsync();
        }

        private async Task RebuildMonthAsync()
        {
            DateTime first = new DateTime(_visibleMonth.Year, _visibleMonth.Month, 1);
            int offset = (((int)first.DayOfWeek) + 6) % 7; // Monday is the first column
            DateTime gridStart = first.AddDays(-offset);
            DateTime gridEnd = gridStart.AddDays(GridDayCount);

            Dictionary<DateTime, int> counts = await LoadDayCountsAsync(gridStart, gridEnd);

            var days = new List<CalendarDayViewModel>();
            for (int i = 0; i < GridDayCount; i++)
            {
                DateTime date = gridStart.AddDays(i);
                counts.TryGetValue(date.Date, out int count);
                days.Add(new CalendarDayViewModel(
                    date,
                    date.Month == first.Month,
                    date.Date == DateTime.Today,
                    _selectedDate.HasValue && (date.Date == _selectedDate.Value.Date),
                    count));
            }
            _days = days;

            OnPropertyChanged(nameof(Days));
            OnPropertyChanged(nameof(MonthTitle));
        }

        private async Task<Dictionary<DateTime, int>> LoadDayCountsAsync(DateTime fromLocal, DateTime toLocalExclusive)
        {
            var result = new Dictionary<DateTime, int>();
            if (!HasPermission)
                return result;

            IReadOnlyList<DeviceEventModel> events;
            try
            {
                events = await _calendarService.GetEventsAsync(fromLocal, toLocalExclusive);
            }
            catch (Exception)
            {
                return result;
            }

            foreach (DeviceEventModel item in events)
            {
                foreach (DateTime day in EnumerateLocalDays(item))
                {
                    result.TryGetValue(day, out int count);
                    result[day] = count + 1;
                }
            }
            return result;
        }

        private static IEnumerable<DateTime> EnumerateLocalDays(DeviceEventModel item)
        {
            if (item.AllDay)
            {
                DateTime start = item.StartUtc.Date;
                DateTime end = item.EndUtc.Date;
                if (end <= start)
                    end = start.AddDays(1);
                for (DateTime day = start; day < end; day = day.AddDays(1))
                    yield return day;
            }
            else
            {
                yield return item.StartUtc.ToLocalTime().Date;
            }
        }

        /// <summary>
        /// Handles a click on a day cell. A single click only selects the cell, a double click
        /// opens the event list of that day.
        /// </summary>
        public async Task DayClickedAsync(CalendarDayViewModel day)
        {
            if (day == null)
                return;

            DateTime now = DateTime.UtcNow;
            bool isDoubleClick = _hasLastClick
                && (day.Date.Date == _lastClickDate.Date)
                && ((now - _lastClickTime) <= DoubleClickDelay);

            if (isDoubleClick)
            {
                _hasLastClick = false;
                await OpenDayAsync(day.Date);
                return;
            }

            _hasLastClick = true;
            _lastClickDate = day.Date;
            _lastClickTime = now;
            _selectedDate = day.Date;
            await RebuildMonthAsync();
        }

        private async Task OpenDayAsync(DateTime date)
        {
            _selectedDate = date;
            _dayListTitle = date.ToString("D", CultureInfo.CurrentCulture);
            IsDayListVisible = true;
            await LoadDayEventsAsync(date);
            await RebuildMonthAsync();
        }

        private async Task LoadDayEventsAsync(DateTime date)
        {
            var result = new List<CalendarEventViewModel>();
            if (HasPermission)
            {
                try
                {
                    IReadOnlyList<DeviceEventModel> events = await _calendarService.GetEventsAsync(date.Date, date.Date.AddDays(1));
                    result = events
                        .OrderBy(e => e.AllDay ? 0 : 1)
                        .ThenBy(e => e.StartUtc)
                        .Select(e => new CalendarEventViewModel(e))
                        .ToList();
                }
                catch (Exception)
                {
                }
            }

            _dayEvents = result;
            OnPropertyChanged(nameof(DayEvents));
            OnPropertyChanged(nameof(HasDayEvents));
        }

        /// <summary>Opens the event editor for a new event.</summary>
        public void AddEvent()
        {
            if (_calendars.Count == 0)
            {
                _feedbackService?.ShowToast(_languageService["cal_no_calendar"], FeedbackSeverity.Warning);
                return;
            }

            DateTime date = _selectedDate ?? DateTime.Today;
            _draftEventId = 0;
            DraftTitle = string.Empty;
            DraftDescription = string.Empty;
            DraftLocation = string.Empty;
            _draftDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _draftStart = "09:00";
            _draftEnd = "10:00";
            DraftAllDay = false;
            _draftReminders.Clear();
            _draftReminderAmount = 15;
            _draftReminderUnit = 0;
            DeviceCalendarModel calendar = _calendars.Find(c => c.IsWritable) ?? _calendars[0];
            DraftCalendarId = calendar.Id;
            _draftColorHex = calendar.ColorHex;

            OnPropertyChanged(nameof(DraftDate));
            OnPropertyChanged(nameof(DraftStart));
            OnPropertyChanged(nameof(DraftEnd));
            OnPropertyChanged(nameof(DraftColorHex));
            OnPropertyChanged(nameof(DraftReminders));
            OnPropertyChanged(nameof(HasDraftReminders));
            OnPropertyChanged(nameof(DraftReminderAmount));
            OnPropertyChanged(nameof(DraftReminderUnit));
            OnPropertyChanged(nameof(IsNewEvent));
            IsEditorVisible = true;
        }

        /// <summary>Opens the event editor for an existing event.</summary>
        public void EditEvent(CalendarEventViewModel item)
        {
            if (item == null)
                return;

            DeviceEventModel model = item.Model;
            _draftEventId = model.Id;
            DraftTitle = model.Title;
            DraftDescription = model.Description;
            DraftLocation = model.Location;
            DraftAllDay = model.AllDay;
            _draftColorHex = model.ColorHex;
            _draftReminders.Clear();
            if (model.Reminders != null)
                _draftReminders.AddRange(model.Reminders);

            DateTime localStart = model.AllDay ? model.StartUtc.Date : model.StartUtc.ToLocalTime();
            DateTime localEnd = model.AllDay ? model.StartUtc.Date.AddHours(1) : model.EndUtc.ToLocalTime();
            _draftDate = localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _draftStart = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
            _draftEnd = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
            DraftCalendarId = model.CalendarId;

            OnPropertyChanged(nameof(DraftDate));
            OnPropertyChanged(nameof(DraftStart));
            OnPropertyChanged(nameof(DraftEnd));
            OnPropertyChanged(nameof(DraftColorHex));
            OnPropertyChanged(nameof(DraftReminders));
            OnPropertyChanged(nameof(HasDraftReminders));
            OnPropertyChanged(nameof(IsNewEvent));
            IsEditorVisible = true;
        }

        /// <summary>Stores the drafted event and closes the editor.</summary>
        public async Task SaveEventAsync()
        {
            if (!DateTime.TryParseExact(_draftDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                date = _selectedDate ?? DateTime.Today;

            var model = new DeviceEventModel
            {
                Id = _draftEventId,
                CalendarId = _draftCalendarId,
                Title = string.IsNullOrWhiteSpace(_draftTitle) ? _languageService["cal_event_default_title"] : _draftTitle.Trim(),
                Description = _draftDescription?.Trim(),
                Location = _draftLocation?.Trim(),
                AllDay = _draftAllDay,
                ColorHex = _draftColorHex,
                Reminders = _draftReminders.ToList(),
            };

            if (_draftAllDay)
            {
                model.StartUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
                model.EndUtc = DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Utc);
            }
            else
            {
                TimeSpan start = ParseTime(_draftStart, new TimeSpan(9, 0, 0));
                TimeSpan end = ParseTime(_draftEnd, start.Add(TimeSpan.FromHours(1)));
                if (end <= start)
                    end = start.Add(TimeSpan.FromHours(1));
                model.StartUtc = date.Date.Add(start).ToUniversalTime();
                model.EndUtc = date.Date.Add(end).ToUniversalTime();
            }

            try
            {
                await _calendarService.SaveEventAsync(model);
                IsEditorVisible = false;
                _selectedDate = date.Date;
                await RebuildMonthAsync();
                if (IsDayListVisible)
                    await LoadDayEventsAsync(date.Date);
            }
            catch (Exception)
            {
                _feedbackService?.ShowToast(_languageService["cal_save_failed"], FeedbackSeverity.Warning);
            }
        }

        /// <summary>Deletes an event from the device calendar.</summary>
        public async Task DeleteEventAsync(CalendarEventViewModel item)
        {
            if (item == null)
                return;

            try
            {
                await _calendarService.DeleteEventAsync(item.Model.Id);
                DateTime date = _selectedDate ?? DateTime.Today;
                await RebuildMonthAsync();
                if (IsDayListVisible)
                    await LoadDayEventsAsync(date.Date);
            }
            catch (Exception)
            {
                _feedbackService?.ShowToast(_languageService["cal_save_failed"], FeedbackSeverity.Warning);
            }
        }

        /// <summary>Closes the event editor without saving.</summary>
        public void CancelEditor()
        {
            IsEditorVisible = false;
        }

        /// <summary>Closes the day event list.</summary>
        public void CloseDayList()
        {
            IsDayListVisible = false;
        }

        private static TimeSpan ParseTime(string text, TimeSpan fallback)
        {
            return TimeSpan.TryParseExact(text, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan value) ? value : fallback;
        }

        private IReadOnlyList<string> BuildWeekdays()
        {
            var result = new List<string>();
            var monday = new DateTime(2024, 1, 1); // A Monday
            for (int i = 0; i < 7; i++)
                result.Add(monday.AddDays(i).ToString("ddd", CultureInfo.CurrentCulture));
            return result;
        }

    }

    /// <summary>
    /// A single cell of the month grid.
    /// </summary>
    public class CalendarDayViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CalendarDayViewModel"/> class.
        /// </summary>
        public CalendarDayViewModel(DateTime date, bool isCurrentMonth, bool isToday, bool isSelected, int eventCount)
        {
            Date = date;
            IsCurrentMonth = isCurrentMonth;
            IsToday = isToday;
            IsSelected = isSelected;
            EventCount = eventCount;
        }

        /// <summary>Gets the date of the cell.</summary>
        public DateTime Date { get; }

        /// <summary>Gets the day number.</summary>
        public int DayNumber
        {
            get { return Date.Day; }
        }

        /// <summary>Gets a value indicating whether the day is in the visible month.</summary>
        public bool IsCurrentMonth { get; }

        /// <summary>Gets a value indicating whether the day is today.</summary>
        public bool IsToday { get; }

        /// <summary>Gets a value indicating whether the day is selected.</summary>
        public bool IsSelected { get; }

        /// <summary>Gets the number of events of the day.</summary>
        public int EventCount { get; }

        /// <summary>Gets a value indicating whether the day has events.</summary>
        public bool HasEvents
        {
            get { return EventCount > 0; }
        }
    }

    /// <summary>
    /// An event of a day, prepared for the view.
    /// </summary>
    public class CalendarEventViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CalendarEventViewModel"/> class.
        /// </summary>
        public CalendarEventViewModel(DeviceEventModel model)
        {
            Model = model;
        }

        /// <summary>Gets the wrapped device event.</summary>
        public DeviceEventModel Model { get; }

        /// <summary>Gets the title of the event.</summary>
        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Model.Title) ? "—" : Model.Title; }
        }

        /// <summary>Gets the time range of the event.</summary>
        public string TimeText
        {
            get
            {
                if (Model.AllDay)
                    return string.Empty;
                DateTime start = Model.StartUtc.ToLocalTime();
                DateTime end = Model.EndUtc.ToLocalTime();
                return string.Format("{0:HH:mm} – {1:HH:mm}", start, end);
            }
        }

        /// <summary>Gets the location of the event.</summary>
        public string Location
        {
            get { return Model.Location; }
        }

        /// <summary>Gets the description of the event.</summary>
        public string Description
        {
            get { return Model.Description; }
        }

        /// <summary>Gets a value indicating whether the event has a location.</summary>
        public bool HasLocation
        {
            get { return !string.IsNullOrWhiteSpace(Model.Location); }
        }

        /// <summary>Gets a value indicating whether the event has a description.</summary>
        public bool HasDescription
        {
            get { return !string.IsNullOrWhiteSpace(Model.Description); }
        }

        /// <summary>Gets a value indicating whether the event is an all day event.</summary>
        public bool IsAllDay
        {
            get { return Model.AllDay; }
        }

        /// <summary>Gets the color of the event.</summary>
        public string ColorHex
        {
            get { return Model.ColorHex; }
        }
    }

}
