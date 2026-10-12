// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Abstract base implementation of the <see cref="IClockService"/> interface. It persists the
    /// clock data (alarms, cities, timer state) as a small XML file, the platform specific alarm
    /// scheduling is done by the sub class.
    /// </summary>
    public abstract class ClockServiceBase : IClockService
    {
        /// <summary>Gets the injected dependency to an xml file service.</summary>
        protected readonly IXmlFileService _xmlFileService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClockServiceBase"/> class.
        /// </summary>
        /// <param name="xmlFileService">The xml file service used to store the clock data.</param>
        public ClockServiceBase(IXmlFileService xmlFileService)
        {
            _xmlFileService = xmlFileService;
        }

        /// <summary>
        /// Sub classes should override this method to define the directory where the clock data is
        /// stored.
        /// </summary>
        /// <returns>The full directory path for storing the clock data.</returns>
        protected abstract string GetLocation();

        /// <inheritdoc/>
        public ClockDataModel LoadData()
        {
            try
            {
                if (_xmlFileService.TryLoad(FilePath, out XDocument xml))
                    return XmlUtils.DeserializeFromXmlDocument<ClockDataModel>(xml);
            }
            catch (Exception)
            {
            }
            return new ClockDataModel();
        }

        /// <inheritdoc/>
        public bool TrySaveData(ClockDataModel model)
        {
            try
            {
                return _xmlFileService.TrySerializeAndSave(FilePath, model);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string FilePath
        {
            get { return Path.Combine(GetLocation(), ClockDataModel.FileName); }
        }

        /// <inheritdoc/>
        public abstract bool IsSupported { get; }

        /// <inheritdoc/>
        public abstract bool HasNotificationPermission { get; }

        /// <inheritdoc/>
        public abstract Task<bool> RequestNotificationPermissionAsync();

        /// <inheritdoc/>
        public abstract bool CanScheduleExactAlarms { get; }

        /// <inheritdoc/>
        public abstract Task<bool> RequestExactAlarmPermissionAsync();

        /// <inheritdoc/>
        public abstract Task ScheduleAlarmAsync(AlarmModel alarm);

        /// <inheritdoc/>
        public abstract Task CancelAlarmAsync(Guid alarmId);

        /// <inheritdoc/>
        public abstract Task ScheduleTimerAsync(Guid timerId, DateTime fireAtUtc, int totalSeconds, string label);

        /// <inheritdoc/>
        public abstract Task CancelTimerAsync(Guid timerId);

        /// <inheritdoc/>
        public abstract Task RescheduleAllAsync();
    }
}
