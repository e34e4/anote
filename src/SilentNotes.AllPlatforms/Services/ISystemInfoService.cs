// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Collects device system diagnostics for the System page.
    /// </summary>
    public interface ISystemInfoService
    {
        /// <summary>Gets whether detailed usage APIs are available on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Gets whether PACKAGE_USAGE_STATS (or equivalent) is granted.</summary>
        bool HasUsageAccess { get; }

        /// <summary>Opens the system screen to grant usage-access permission.</summary>
        void OpenUsageAccessSettings();

        /// <summary>Reads battery status and per-app usage estimates.</summary>
        Task<SystemBatteryInfo> GetBatteryInfoAsync(SystemBatterySortMode sortMode);

        /// <summary>Reads hardware / build / sensor information.</summary>
        Task<SystemHardwareInfo> GetHardwareInfoAsync();

        /// <summary>Lists installed apps ordered by total storage (app + data + cache).</summary>
        Task<SystemStorageAppInfo[]> GetStorageAppsAsync();

        /// <summary>Lists currently running processes.</summary>
        Task<SystemProcessInfo[]> GetRunningProcessesAsync();

        /// <summary>Attempts to kill a background process / package. Returns true on success.</summary>
        Task<bool> KillProcessAsync(SystemProcessInfo process);

        /// <summary>Reads memory / swap / zram and per-app RSS/PSS.</summary>
        Task<SystemMemoryInfo> GetMemoryInfoAsync();

        /// <summary>Reads networking status and per-app data usage.</summary>
        Task<SystemNetworkInfo> GetNetworkInfoAsync();
    }
}
