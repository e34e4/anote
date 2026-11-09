// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub system info service for Windows.
    /// </summary>
    internal class SystemInfoService : ISystemInfoService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool HasUsageAccess => false;

        /// <inheritdoc/>
        public void OpenUsageAccessSettings()
        {
        }

        /// <inheritdoc/>
        public Task<SystemBatteryInfo> GetBatteryInfoAsync(SystemBatterySortMode sortMode)
        {
            return Task.FromResult(new SystemBatteryInfo
            {
                Notes = "System diagnostics are available on Android only.",
            });
        }

        /// <inheritdoc/>
        public Task<SystemHardwareInfo> GetHardwareInfoAsync()
        {
            return Task.FromResult(new SystemHardwareInfo
            {
                Manufacturer = Environment.OSVersion.Platform.ToString(),
                Model = Environment.MachineName,
                AndroidVersion = Environment.OSVersion.VersionString,
                CpuCores = Environment.ProcessorCount,
            });
        }

        /// <inheritdoc/>
        public Task<SystemStorageAppInfo[]> GetStorageAppsAsync()
        {
            return Task.FromResult(Array.Empty<SystemStorageAppInfo>());
        }

        /// <inheritdoc/>
        public Task<SystemProcessesSnapshot> GetRunningProcessesAsync()
        {
            return Task.FromResult(new SystemProcessesSnapshot
            {
                Notes = "System diagnostics are available on Android only.",
            });
        }

        /// <inheritdoc/>
        public Task<bool> KillProcessAsync(SystemProcessInfo process)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task<SystemMemoryInfo> GetMemoryInfoAsync()
        {
            return Task.FromResult(new SystemMemoryInfo
            {
                Notes = "System diagnostics are available on Android only.",
            });
        }

        /// <inheritdoc/>
        public Task<SystemNetworkInfo> GetNetworkInfoAsync()
        {
            return Task.FromResult(new SystemNetworkInfo
            {
                Notes = "System diagnostics are available on Android only.",
            });
        }

        /// <inheritdoc/>
        public Task<SystemLiveCounters> GetLiveCountersAsync()
        {
            return Task.FromResult(new SystemLiveCounters
            {
                Notes = "System diagnostics are available on Android only.",
            });
        }

        /// <inheritdoc/>
        public Task<SystemActionResult> SetNetworkOutboundLoggingAsync(bool enabled)
        {
            return Task.FromResult(SystemActionResult.Fail("System diagnostics are available on Android only."));
        }

        /// <inheritdoc/>
        public Task<bool> IsNetworkOutboundLoggingEnabledAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task<SystemNetworkLogEntry[]> GetNetworkOutboundLogsAsync(int maxCount = 200)
        {
            return Task.FromResult(Array.Empty<SystemNetworkLogEntry>());
        }

        /// <inheritdoc/>
        public Task<SystemSavedIpEntry[]> GetSavedIpsAsync()
        {
            return Task.FromResult(Array.Empty<SystemSavedIpEntry>());
        }

        /// <inheritdoc/>
        public Task<SystemSavedIpEntry> SaveIpFromLogAsync(SystemNetworkLogEntry entry)
        {
            return Task.FromResult<SystemSavedIpEntry>(null);
        }

        /// <inheritdoc/>
        public Task RemoveSavedIpAsync(string id)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<int> ResolveSavedIpsAsync()
        {
            return Task.FromResult(0);
        }

        /// <inheritdoc/>
        public Task<SystemLogcatSnapshot> CaptureLogcatAsync()
        {
            return Task.FromResult(new SystemLogcatSnapshot
            {
                Notes = "Logcat is available on Android only.",
            });
        }
    }
}
