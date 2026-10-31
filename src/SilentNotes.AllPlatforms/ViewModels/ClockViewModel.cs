// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// The sections of the clock tab.
    /// </summary>
    public enum ClockSection
    {
        Alarm,
        Clock,
        Timer,
        Stopwatch,
        Compass,
    }

    /// <summary>
    /// View model of the clock tab. It offers the four standard sections of a clock app: alarms,
    /// a world clock, a timer and a stopwatch.
    /// </summary>
    public class ClockViewModel : ViewModelBase
    {
        private readonly IClockService _clockService;
        private readonly ILanguageService _languageService;
        private readonly IFeedbackService _feedbackService;
        private readonly ICompassService _compassService;
        private readonly IAbroBrowserService _abroBrowserService;

        private ClockDataModel _data;
        private ClockSection _section = ClockSection.Clock;
        private bool _permissionRequested;
        private List<AlarmItemViewModel> _alarms = new List<AlarmItemViewModel>();
        private List<WorldClockCityViewModel> _cities = new List<WorldClockCityViewModel>();
        private int _selectedCityIndex;

        private bool _isEditorVisible;
        private Guid _draftAlarmId;
        private int _draftHour;
        private int _draftMinute;
        private string _draftLabel;
        private readonly bool[] _draftDays = new bool[7];

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private List<string> _laps = new List<string>();

        private bool _timerRunning;
        private bool _timerPaused;
        private DateTime _timerEndUtc;
        private int _timerRemainingSeconds;
        private int _timerTotalSeconds;
        private string _timerLabel;
        private int _timerHours;
        private int _timerMinutes = 5;
        private int _timerSeconds;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClockViewModel"/> class.
        /// </summary>
        public ClockViewModel(
            IClockService clockService,
            ILanguageService languageService,
            IFeedbackService feedbackService,
            ICompassService compassService = null,
            IAbroBrowserService abroBrowserService = null)
        {
            _clockService = clockService;
            _languageService = languageService;
            _feedbackService = feedbackService;
            _compassService = compassService;
            _abroBrowserService = abroBrowserService;

            ShowAlarmCommand = new RelayCommand(() => Show(ClockSection.Alarm));
            ShowClockCommand = new RelayCommand(() => Show(ClockSection.Clock));
            ShowTimerCommand = new RelayCommand(() => Show(ClockSection.Timer));
            ShowStopwatchCommand = new RelayCommand(() => Show(ClockSection.Stopwatch));
            ShowCompassCommand = new RelayCommand(() => Show(ClockSection.Compass));
            OpenMapLinkCommand = new RelayCommand<string>(OpenMapLink);

            if (_compassService != null)
                _compassService.ReadingChanged += () => OnPropertyChanged(nameof(CompassHeadingText), nameof(CompassDegreeText), nameof(CompassAccuracyText), nameof(CompassRotation));

            AddAlarmCommand = new RelayCommand(AddAlarm);
            EditAlarmCommand = new RelayCommand<AlarmItemViewModel>(EditAlarm);
            DeleteAlarmCommand = new RelayCommand<AlarmItemViewModel>(DeleteAlarm);
            ToggleAlarmCommand = new RelayCommand<AlarmItemViewModel>(ToggleAlarm);
            SaveAlarmCommand = new RelayCommand(SaveAlarm);
            CancelEditorCommand = new RelayCommand(() => IsEditorVisible = false);
            ToggleDraftDayCommand = new RelayCommand<int>(ToggleDraftDay);

            AddCityCommand = new RelayCommand(AddCity);
            RemoveCityCommand = new RelayCommand<WorldClockCityViewModel>(RemoveCity);

            StartTimerCommand = new RelayCommand(StartTimer);
            PauseTimerCommand = new RelayCommand(PauseTimer);
            ResetTimerCommand = new RelayCommand(ResetTimer);

            StartPauseStopwatchCommand = new RelayCommand(StartPauseStopwatch);
            ResetStopwatchCommand = new RelayCommand(ResetStopwatch);
            LapCommand = new RelayCommand(AddLap);

            Load();
        }

        /// <summary>Gets a value indicating whether the platform can schedule system alarms.</summary>
        public bool IsSupported
        {
            get { return _clockService.IsSupported; }
        }

        /// <summary>Gets a value indicating whether exact alarms are allowed by the user.</summary>
        public bool CanScheduleExactAlarms
        {
            get { return _clockService.CanScheduleExactAlarms; }
        }

        /// <summary>Gets a value indicating whether notifications are allowed by the user.</summary>
        public bool HasNotificationPermission
        {
            get { return _clockService.IsSupported && _clockService.HasNotificationPermission; }
        }

        /// <summary>Gets a value indicating whether the exact alarm permission is missing.</summary>
        public bool NeedsExactAlarmPermission
        {
            get { return _clockService.IsSupported && !_clockService.CanScheduleExactAlarms; }
        }

        /// <summary>Gets a value indicating whether the notification permission is missing.</summary>
        public bool NeedsNotificationPermission
        {
            get { return _clockService.IsSupported && !_clockService.HasNotificationPermission; }
        }

        /// <summary>Gets or sets the currently visible section.</summary>
        public ClockSection Section
        {
            get { return _section; }
            private set
            {
                if (SetProperty(ref _section, value))
                {
                    OnPropertyChanged(
                        nameof(IsAlarmSection),
                        nameof(IsClockSection),
                        nameof(IsTimerSection),
                        nameof(IsStopwatchSection),
                        nameof(IsCompassSection));
                    UpdateCompassListening();
                }
            }
        }

        public bool IsAlarmSection { get { return _section == ClockSection.Alarm; } }
        public bool IsClockSection { get { return _section == ClockSection.Clock; } }
        public bool IsTimerSection { get { return _section == ClockSection.Timer; } }
        public bool IsStopwatchSection { get { return _section == ClockSection.Stopwatch; } }
        public bool IsCompassSection { get { return _section == ClockSection.Compass; } }

        public ICommand ShowAlarmCommand { get; }
        public ICommand ShowClockCommand { get; }
        public ICommand ShowTimerCommand { get; }
        public ICommand ShowStopwatchCommand { get; }
        public ICommand ShowCompassCommand { get; }
        public ICommand OpenMapLinkCommand { get; }

        public bool IsCompassSupported => _compassService?.IsSupported == true;

        public string CompassDegreeText
        {
            get
            {
                float? heading = _compassService?.HeadingDegrees;
                if (heading == null)
                    return "—";
                return ((int)Math.Round(heading.Value)).ToString(CultureInfo.InvariantCulture) + "°";
            }
        }

        public string CompassHeadingText
        {
            get
            {
                float? heading = _compassService?.HeadingDegrees;
                if (heading == null)
                    return _languageService?["clock_compass_waiting"] ?? "Waiting for sensors…";
                return CardinalDirection(heading.Value);
            }
        }

        public string CompassAccuracyText => _compassService?.AccuracyText ?? string.Empty;

        /// <summary>CSS rotation so the needle points to magnetic north (rotate opposite of heading).</summary>
        public string CompassRotation
        {
            get
            {
                float heading = _compassService?.HeadingDegrees ?? 0f;
                return (-heading).ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Gets the configured alarms.</summary>
        public IReadOnlyList<AlarmItemViewModel> Alarms
        {
            get { return _alarms; }
        }

        /// <summary>Gets a value indicating whether there are no alarms.</summary>
        public bool HasNoAlarms
        {
            get { return _alarms.Count == 0; }
        }

        // ---------------------------------------------------------------------------------------
        // Alarm editor
        // ---------------------------------------------------------------------------------------

        /// <summary>Gets or sets a value indicating whether the alarm editor is open.</summary>
        public bool IsEditorVisible
        {
            get { return _isEditorVisible; }
            private set { SetProperty(ref _isEditorVisible, value); }
        }

        /// <summary>Gets a value indicating whether the editor creates a new alarm.</summary>
        public bool IsNewAlarm
        {
            get { return _draftAlarmId == Guid.Empty; }
        }

        /// <summary>Gets or sets the hour of the drafted alarm.</summary>
        public int DraftHour
        {
            get { return _draftHour; }
            set { if (SetProperty(ref _draftHour, value)) OnPropertyChanged(nameof(DraftTimeText)); }
        }

        /// <summary>Gets or sets the minute of the drafted alarm.</summary>
        public int DraftMinute
        {
            get { return _draftMinute; }
            set { if (SetProperty(ref _draftMinute, value)) OnPropertyChanged(nameof(DraftTimeText)); }
        }

        /// <summary>Gets or sets the label of the drafted alarm.</summary>
        public string DraftLabel
        {
            get { return _draftLabel; }
            set { SetProperty(ref _draftLabel, value); }
        }

        /// <summary>Gets the time of the drafted alarm as "HH:mm".</summary>
        public string DraftTimeText
        {
            get { return new TimeSpan(_draftHour, _draftMinute, 0).ToString(@"hh\:mm", CultureInfo.InvariantCulture); }
        }

        public ICommand AddAlarmCommand { get; }
        public ICommand EditAlarmCommand { get; }
        public ICommand DeleteAlarmCommand { get; }
        public ICommand ToggleAlarmCommand { get; }
        public ICommand SaveAlarmCommand { get; }
        public ICommand CancelEditorCommand { get; }
        public ICommand ToggleDraftDayCommand { get; }

        /// <summary>Gets the localized day names, starting with Monday.</summary>
        public IReadOnlyList<string> DayNames
        {
            get
            {
                var result = new List<string>();
                var monday = new DateTime(2024, 1, 1);
                for (int i = 0; i < 7; i++)
                    result.Add(monday.AddDays(i).ToString("ddd", CultureInfo.CurrentCulture));
                return result;
            }
        }

        /// <summary>Determines whether a weekday is selected in the drafted alarm.</summary>
        public bool IsDraftDay(int index)
        {
            return _draftDays[index];
        }

        private void ToggleDraftDay(int index)
        {
            _draftDays[index] = !_draftDays[index];
            OnPropertyChanged(nameof(DraftDaysText));
        }

        /// <summary>Gets a summary of the repeated days of the drafted alarm.</summary>
        public string DraftDaysText
        {
            get
            {
                if (_draftDays.All(d => !d))
                    return _languageService["clock_once"];
                if (_draftDays.All(d => d))
                    return _languageService["clock_every_day"];

                var names = DayNames;
                var used = new List<string>();
                for (int i = 0; i < 7; i++)
                {
                    if (_draftDays[i])
                        used.Add(names[i]);
                }
                return string.Join(", ", used);
            }
        }

        // ---------------------------------------------------------------------------------------
        // World clock
        // ---------------------------------------------------------------------------------------

        /// <summary>Gets the cities of the world clock with their current time.</summary>
        public IReadOnlyList<WorldClockCityViewModel> CityClocks
        {
            get { return _cities; }
        }

        /// <summary>Gets the list of cities which can be added to the world clock.</summary>
        public IReadOnlyList<WorldClockCityModel> AvailableCities
        {
            get { return WorldClockCatalog.Cities; }
        }

        /// <summary>Gets or sets the index of the city selected for adding.</summary>
        public int SelectedCityIndex
        {
            get { return _selectedCityIndex; }
            set { SetProperty(ref _selectedCityIndex, value); }
        }

        public ICommand AddCityCommand { get; }
        public ICommand RemoveCityCommand { get; }

        // ---------------------------------------------------------------------------------------
        // Timer
        // ---------------------------------------------------------------------------------------

        /// <summary>Gets or sets the hours of the timer input.</summary>
        public int TimerHours
        {
            get { return _timerHours; }
            set { if (SetProperty(ref _timerHours, Clamp(value, 0, 23))) OnPropertyChanged(nameof(TimerDisplay)); }
        }

        /// <summary>Gets or sets the minutes of the timer input.</summary>
        public int TimerMinutes
        {
            get { return _timerMinutes; }
            set { if (SetProperty(ref _timerMinutes, Clamp(value, 0, 59))) OnPropertyChanged(nameof(TimerDisplay)); }
        }

        /// <summary>Gets or sets the seconds of the timer input.</summary>
        public int TimerSeconds
        {
            get { return _timerSeconds; }
            set { if (SetProperty(ref _timerSeconds, Clamp(value, 0, 59))) OnPropertyChanged(nameof(TimerDisplay)); }
        }

        /// <summary>Gets a value indicating whether the timer is currently counting down.</summary>
        public bool IsTimerRunning
        {
            get { return _timerRunning; }
        }

        /// <summary>Gets the remaining time of the timer as "HH:mm:ss".</summary>
        public string TimerDisplay
        {
            get { return FormatHms(CurrentTimerSeconds); }
        }

        public ICommand StartTimerCommand { get; }
        public ICommand PauseTimerCommand { get; }
        public ICommand ResetTimerCommand { get; }

        // ---------------------------------------------------------------------------------------
        // Stopwatch
        // ---------------------------------------------------------------------------------------

        /// <summary>Gets a value indicating whether the stopwatch is running.</summary>
        public bool IsStopwatchRunning
        {
            get { return _stopwatch.IsRunning; }
        }

        /// <summary>Gets the elapsed time of the stopwatch as "mm:ss.ff".</summary>
        public string StopwatchDisplay
        {
            get { return FormatStopwatch(_stopwatch.Elapsed); }
        }

        /// <summary>Gets the recorded laps.</summary>
        public IReadOnlyList<string> Laps
        {
            get { return _laps; }
        }

        public ICommand StartPauseStopwatchCommand { get; }
        public ICommand ResetStopwatchCommand { get; }
        public ICommand LapCommand { get; }

        // ---------------------------------------------------------------------------------------
        // Behaviour
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Initializes the clock when the tab is shown, requests the permissions if necessary.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!_clockService.IsSupported)
                return;

            if (!_clockService.HasNotificationPermission && !_permissionRequested)
            {
                _permissionRequested = true;
                await _clockService.RequestNotificationPermissionAsync();
            }
        }

        /// <summary>
        /// Reschedules all enabled alarms and a still running timer. It is called when the app
        /// starts, so the alarms survive a restart of the app or the device.
        /// </summary>
        public Task RescheduleAsync()
        {
            if (!_clockService.IsSupported)
                return Task.CompletedTask;
            return RescheduleSafeAsync();
        }

        private async Task RescheduleSafeAsync()
        {
            try
            {
                await _clockService.RescheduleAllAsync();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Asks the user for the notification permission.</summary>
        public Task RequestNotificationPermissionAsync()
        {
            return _clockService.RequestNotificationPermissionAsync();
        }

        /// <summary>Opens the system settings where the user can allow exact alarms.</summary>
        public Task RequestExactAlarmPermissionAsync()
        {
            return _clockService.RequestExactAlarmPermissionAsync();
        }

        /// <summary>
        /// Applies a clock intent which was triggered by a fired alarm.
        /// </summary>
        public Task HandleIntentAsync(ClockIntentRequest request)
        {
            QueueIntent(request);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Shows the section which is requested by a clock intent (a ringing alarm or a timer).
        /// </summary>
        public void QueueIntent(ClockIntentRequest request)
        {
            if (request == null)
                return;

            Section = (request.Action == ClockIntentAction.OpenTimer) ? ClockSection.Timer : ClockSection.Alarm;
            Show(Section);
        }

        /// <summary>
        /// Called by the view roughly once per second, to update the displayed times.
        /// </summary>
        public void Tick()
        {
            if (_section == ClockSection.Clock)
                RefreshCityTimes();

            if (_section == ClockSection.Timer)
                RefreshTimer();

            if (_section == ClockSection.Stopwatch)
                OnPropertyChanged(nameof(StopwatchDisplay));

            OnPropertyChanged(nameof(Alarms));
        }

        private void Show(ClockSection section)
        {
            Section = section;
            if (section == ClockSection.Clock)
                RefreshCityTimes();
        }

        private void UpdateCompassListening()
        {
            if (_compassService == null)
                return;
            if (_section == ClockSection.Compass)
                _compassService.Start();
            else
                _compassService.Stop();
        }

        private void OpenMapLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            if (_abroBrowserService?.IsSupported == true)
                _ = _abroBrowserService.PresentUrlAsync(url);
        }

        private static string CardinalDirection(float degrees)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Floor(((degrees % 360) + 22.5) / 45.0) % 8;
            return dirs[index];
        }

        private void Load()
        {
            _data = _clockService.LoadData();
            if (_data.Cities.Count == 0)
                _data.Cities.AddRange(WorldClockCatalog.CreateDefaults());

            RestoreTimer();

            ReloadAlarms();
            RefreshCityTimes();
        }

        private void ReloadAlarms()
        {
            _alarms = _data.Alarms
                .OrderBy(a => a.Hour)
                .ThenBy(a => a.Minute)
                .Select(a => new AlarmItemViewModel(a) { NextText = GetNextText(a) })
                .ToList();
            OnPropertyChanged(nameof(Alarms));
            OnPropertyChanged(nameof(HasNoAlarms));
        }

        private string GetNextText(AlarmModel alarm)
        {
            if (!alarm.Enabled)
                return _languageService["clock_off"];
            DateTime next = alarm.GetNextOccurrence(DateTime.Now);
            TimeSpan diff = next - DateTime.Now;
            string inText = (diff.TotalHours < 24)
                ? string.Format(_languageService["clock_in_hours_min"], (int)diff.TotalHours, diff.Minutes)
                : next.ToString("ddd", CultureInfo.CurrentCulture);
            return inText;
        }

        private void AddAlarm()
        {
            _draftAlarmId = Guid.Empty;
            _draftHour = 7;
            _draftMinute = 0;
            _draftLabel = string.Empty;
            for (int i = 0; i < 7; i++)
                _draftDays[i] = false;
            RaiseDraftChanged();
            IsEditorVisible = true;
        }

        private void EditAlarm(AlarmItemViewModel item)
        {
            if (item == null)
                return;
            AlarmModel model = item.Model;
            _draftAlarmId = model.Id;
            _draftHour = model.Hour;
            _draftMinute = model.Minute;
            _draftLabel = model.Label ?? string.Empty;
            for (int i = 0; i < 7; i++)
                _draftDays[i] = (model.Days & (1 << i)) != 0;
            RaiseDraftChanged();
            IsEditorVisible = true;
        }

        private void SaveAlarm()
        {
            int days = 0;
            for (int i = 0; i < 7; i++)
            {
                if (_draftDays[i])
                    days |= (1 << i);
            }

            AlarmModel model = _data.Alarms.Find(a => a.Id == _draftAlarmId);
            bool isNew = model == null;
            if (isNew)
            {
                model = new AlarmModel { Id = Guid.NewGuid() };
                _data.Alarms.Add(model);
            }
            model.Hour = _draftHour;
            model.Minute = _draftMinute;
            model.Label = _draftLabel?.Trim();
            model.Days = days;
            model.Enabled = true;

            _clockService.TrySaveData(_data);
            _ = ScheduleAlarmSafeAsync(model);

            IsEditorVisible = false;
            ReloadAlarms();
        }

        private void DeleteAlarm(AlarmItemViewModel item)
        {
            if (item == null)
                return;
            _data.Alarms.RemoveAll(a => a.Id == item.Model.Id);
            _clockService.TrySaveData(_data);
            _ = CancelAlarmSafeAsync(item.Model.Id);
            ReloadAlarms();
        }

        private void ToggleAlarm(AlarmItemViewModel item)
        {
            if (item == null)
                return;
            AlarmModel model = item.Model;
            model.Enabled = !model.Enabled;
            _clockService.TrySaveData(_data);
            if (model.Enabled)
                _ = ScheduleAlarmSafeAsync(model);
            else
                _ = CancelAlarmSafeAsync(model.Id);
            ReloadAlarms();
        }

        private async Task ScheduleAlarmSafeAsync(AlarmModel alarm)
        {
            try
            {
                await _clockService.ScheduleAlarmAsync(alarm);
            }
            catch (Exception)
            {
            }
        }

        private async Task CancelAlarmSafeAsync(Guid alarmId)
        {
            try
            {
                await _clockService.CancelAlarmAsync(alarmId);
            }
            catch (Exception)
            {
            }
        }

        private void AddCity()
        {
            IReadOnlyList<WorldClockCityModel> available = AvailableCities;
            if ((_selectedCityIndex < 0) || (_selectedCityIndex >= available.Count))
                return;

            WorldClockCityModel city = available[_selectedCityIndex];
            if (_data.Cities.Any(c => string.Equals(c.TimeZoneId, city.TimeZoneId, StringComparison.OrdinalIgnoreCase)))
                return;

            _data.Cities.Add(new WorldClockCityModel { Name = city.Name, TimeZoneId = city.TimeZoneId });
            _clockService.TrySaveData(_data);
            RefreshCityTimes();
        }

        private void RemoveCity(WorldClockCityViewModel city)
        {
            if (city == null)
                return;
            _data.Cities.RemoveAll(c => string.Equals(c.TimeZoneId, city.Model.TimeZoneId, StringComparison.OrdinalIgnoreCase));
            _clockService.TrySaveData(_data);
            RefreshCityTimes();
        }

        private void RefreshCityTimes()
        {
            var result = new List<WorldClockCityViewModel>();
            foreach (WorldClockCityModel city in _data.Cities)
            {
                result.Add(new WorldClockCityViewModel(city)
                {
                    TimeText = GetCityTime(city, out string dayText),
                    DayText = dayText,
                });
            }
            _cities = result;
            OnPropertyChanged(nameof(CityClocks));
        }

        private static string GetCityTime(WorldClockCityModel city, out string dayText)
        {
            try
            {
                TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
                dayText = local.ToString("ddd d MMM", CultureInfo.CurrentCulture);
                return local.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                dayText = string.Empty;
                return "--:--";
            }
        }

        // ---------------------------------------------------------------------------------------
        // Timer behaviour
        // ---------------------------------------------------------------------------------------

        private int InputTotalSeconds
        {
            get { return (_timerHours * 3600) + (_timerMinutes * 60) + _timerSeconds; }
        }

        private int CurrentTimerSeconds
        {
            get
            {
                if (_timerRunning)
                {
                    int remaining = (int)Math.Ceiling((_timerEndUtc - DateTime.UtcNow).TotalSeconds);
                    return Math.Max(0, remaining);
                }
                if (_timerPaused && (_timerRemainingSeconds > 0))
                    return _timerRemainingSeconds;
                return InputTotalSeconds;
            }
        }

        private void StartTimer()
        {
            int total = (CurrentTimerSeconds > 0) ? CurrentTimerSeconds : InputTotalSeconds;
            if (total <= 0)
                return;

            if (_timerTotalSeconds != total)
            {
                _timerLabel = _languageService["clock_timer"];
                _timerTotalSeconds = total;
            }

            _timerRunning = true;
            _timerPaused = false;
            _timerEndUtc = DateTime.UtcNow.AddSeconds(total);
            PersistTimer();
            ScheduleTimerSafe();
            OnPropertyChanged(nameof(IsTimerRunning), nameof(TimerDisplay));
        }

        private void PauseTimer()
        {
            if (!_timerRunning)
                return;

            _timerRemainingSeconds = CurrentTimerSeconds;
            _timerRunning = false;
            _timerPaused = true;
            PersistTimer();
            CancelTimerSafe();
            OnPropertyChanged(nameof(IsTimerRunning), nameof(TimerDisplay));
        }

        private void ResetTimer()
        {
            _timerRunning = false;
            _timerPaused = false;
            _timerRemainingSeconds = 0;
            _timerTotalSeconds = 0;
            PersistTimer();
            CancelTimerSafe();
            OnPropertyChanged(nameof(IsTimerRunning), nameof(TimerDisplay));
        }

        private void RefreshTimer()
        {
            if (_timerRunning && (CurrentTimerSeconds <= 0))
            {
                _timerRunning = false;
                _timerPaused = false;
                _timerRemainingSeconds = 0;
                _timerTotalSeconds = 0;
                PersistTimer();
                _feedbackService?.ShowToast(_languageService["clock_timer_finished"]);
            }
            OnPropertyChanged(nameof(IsTimerRunning), nameof(TimerDisplay));
        }

        private void RestoreTimer()
        {
            if ((_data.TimerId == Guid.Empty) || (_data.TimerEndUtc <= DateTime.UtcNow))
            {
                if (_data.TimerId != Guid.Empty)
                {
                    _data.TimerId = Guid.Empty;
                    _clockService.TrySaveData(_data);
                }
                return;
            }

            _timerRunning = true;
            _timerEndUtc = _data.TimerEndUtc;
            _timerTotalSeconds = _data.TimerSeconds;
            _timerLabel = _data.TimerLabel;
        }

        private void PersistTimer()
        {
            if (_timerRunning)
            {
                _data.TimerId = Guid.NewGuid();
                _data.TimerEndUtc = _timerEndUtc;
                _data.TimerSeconds = _timerTotalSeconds;
                _data.TimerLabel = _timerLabel;
            }
            else
            {
                _data.TimerId = Guid.Empty;
            }
            _clockService.TrySaveData(_data);
        }

        private void ScheduleTimerSafe()
        {
            try
            {
                _ = _clockService.ScheduleTimerAsync(_data.TimerId, _timerEndUtc, _timerTotalSeconds, _timerLabel);
            }
            catch (Exception)
            {
            }
        }

        private void CancelTimerSafe()
        {
            try
            {
                _ = _clockService.CancelTimerAsync(_data.TimerId);
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------------------------------
        // Stopwatch behaviour
        // ---------------------------------------------------------------------------------------

        private void StartPauseStopwatch()
        {
            if (_stopwatch.IsRunning)
                _stopwatch.Stop();
            else
                _stopwatch.Start();
            OnPropertyChanged(nameof(IsStopwatchRunning), nameof(StopwatchDisplay));
        }

        private void ResetStopwatch()
        {
            _stopwatch.Reset();
            _laps.Clear();
            OnPropertyChanged(nameof(IsStopwatchRunning), nameof(StopwatchDisplay), nameof(Laps));
        }

        private void AddLap()
        {
            if (_stopwatch.Elapsed == TimeSpan.Zero)
                return;
            _laps.Insert(0, string.Format("{0}  {1}", _laps.Count + 1, FormatStopwatch(_stopwatch.Elapsed)));
            OnPropertyChanged(nameof(Laps));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------

        private void RaiseDraftChanged()
        {
            OnPropertyChanged(nameof(DraftHour), nameof(DraftMinute), nameof(DraftLabel));
            OnPropertyChanged(nameof(DraftTimeText), nameof(DraftDaysText), nameof(IsNewAlarm));
        }

        private static string FormatHms(int totalSeconds)
        {
            totalSeconds = Math.Max(0, totalSeconds);
            var span = TimeSpan.FromSeconds(totalSeconds);
            return ((int)span.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatStopwatch(TimeSpan elapsed)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:00}",
                (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds / 10);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }

    /// <summary>
    /// View model for a single alarm.
    /// </summary>
    public class AlarmItemViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AlarmItemViewModel"/> class.
        /// </summary>
        public AlarmItemViewModel(AlarmModel model)
        {
            Model = model;
        }

        /// <summary>Gets the wrapped alarm.</summary>
        public AlarmModel Model { get; }

        /// <summary>Gets or sets the text showing when the alarm will fire next.</summary>
        public string NextText { get; set; }

        /// <summary>Gets the time of the alarm as "HH:mm".</summary>
        public string TimeText
        {
            get { return new TimeSpan(Model.Hour, Model.Minute, 0).ToString(@"hh\:mm", CultureInfo.InvariantCulture); }
        }

        /// <summary>Gets the label of the alarm.</summary>
        public string Label
        {
            get { return Model.Label; }
        }

        /// <summary>Gets a value indicating whether the alarm is enabled.</summary>
        public bool Enabled
        {
            get { return Model.Enabled; }
        }
    }

    /// <summary>
    /// View model for a city on the world clock.
    /// </summary>
    public class WorldClockCityViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorldClockCityViewModel"/> class.
        /// </summary>
        public WorldClockCityViewModel(WorldClockCityModel model)
        {
            Model = model;
        }

        /// <summary>Gets the wrapped city.</summary>
        public WorldClockCityModel Model { get; }

        /// <summary>Gets the display name of the city.</summary>
        public string Name
        {
            get { return Model.Name; }
        }

        /// <summary>Gets or sets the current time in the city.</summary>
        public string TimeText { get; set; }

        /// <summary>Gets or sets the current day in the city.</summary>
        public string DayText { get; set; }
    }
}
