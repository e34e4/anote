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

        /// <summary>Lists running processes plus autostart and accessibility apps.</summary>
        Task<SystemProcessesSnapshot> GetRunningProcessesAsync();

        /// <summary>Attempts to kill a background process / package. Returns true on success.</summary>
        Task<bool> KillProcessAsync(SystemProcessInfo process);

        /// <summary>Reads memory / swap / zram and per-app RSS/PSS.</summary>
        Task<SystemMemoryInfo> GetMemoryInfoAsync();

        /// <summary>Reads networking status and per-app data usage.</summary>
        Task<SystemNetworkInfo> GetNetworkInfoAsync();

        /// <summary>Reads a single sample of CPU load, temperature, free RAM and frequency.</summary>
        Task<SystemLiveCounters> GetLiveCountersAsync();

        /// <summary>Enables or disables outbound connection logging (requires root).</summary>
        Task<SystemActionResult> SetNetworkOutboundLoggingAsync(bool enabled);

        /// <summary>Gets whether the NET_OUT iptables LOG rule is currently enabled.</summary>
        Task<bool> IsNetworkOutboundLoggingEnabledAsync();

        /// <summary>Fetches the latest NET_OUT lines from dmesg (clears previous UI list).</summary>
        Task<SystemNetworkLogEntry[]> GetNetworkOutboundLogsAsync(int maxCount = 200);

        /// <summary>Returns persisted IP records saved from network logs.</summary>
        Task<SystemSavedIpEntry[]> GetSavedIpsAsync();

        /// <summary>Adds a log line's destination IP to the saved IP list.</summary>
        Task<SystemSavedIpEntry> SaveIpFromLogAsync(SystemNetworkLogEntry entry);

        /// <summary>Removes one saved IP record by id.</summary>
        Task RemoveSavedIpAsync(string id);

        /// <summary>Reverse-DNS resolves saved IPs that are not resolved yet. Returns how many were newly resolved.</summary>
        Task<int> ResolveSavedIpsAsync();

        /// <summary>
        /// Dumps the current logcat buffer to a local snapshot file and returns parsed lines
        /// for searching on the System → Logcat tab.
        /// </summary>
        Task<SystemLogcatSnapshot> CaptureLogcatAsync();
    }
}
