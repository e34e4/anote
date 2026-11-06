// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>How battery app list is sorted.</summary>
    public enum SystemBatterySortMode
    {
        ScreenTime = 0,
        CpuTime = 1,
        Wakelocks = 2,
    }

    /// <summary>Battery status snapshot.</summary>
    public class SystemBatteryInfo
    {
        public int LevelPercent { get; set; }
        public string Health { get; set; }
        public string Status { get; set; }
        public string Plugged { get; set; }
        public bool IsDoze { get; set; }
        public bool IsPowerSave { get; set; }
        public double? TemperatureCelsius { get; set; }
        public double? VoltageVolts { get; set; }
        public double? CurrentMilliAmps { get; set; }
        public long? ChargeCounterMicroAh { get; set; }
        public int? CycleCount { get; set; }
        public string Technology { get; set; }
        public DateTime? LastNearFullChargeLocal { get; set; }
        public bool HasUsageAccess { get; set; }
        public string Notes { get; set; }
        public List<SystemBatteryAppUsage> Apps { get; set; } = new List<SystemBatteryAppUsage>();
    }

    /// <summary>Per-app battery / usage estimate.</summary>
    public class SystemBatteryAppUsage
    {
        public string PackageName { get; set; }
        public string Label { get; set; }
        public TimeSpan ScreenTime { get; set; }
        public TimeSpan CpuTime { get; set; }
        public long WakelockCount { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>Device / OS hardware overview.</summary>
    public class SystemHardwareInfo
    {
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Device { get; set; }
        public string Brand { get; set; }
        public string Product { get; set; }
        public string Hardware { get; set; }
        public string Board { get; set; }
        public string AndroidVersion { get; set; }
        public int SdkInt { get; set; }
        public string BuildId { get; set; }
        public string BuildDisplay { get; set; }
        public string BuildNumber { get; set; }
        public string SecurityPatch { get; set; }
        public string Fingerprint { get; set; }
        public string CpuAbi { get; set; }
        public string CpuHardware { get; set; }
        public int CpuCores { get; set; }
        public string ThermalStatus { get; set; }
        public List<string> Sensors { get; set; } = new List<string>();
        public List<string> Cameras { get; set; } = new List<string>();
        public List<KeyValuePair<string, string>> Extra { get; set; } = new List<KeyValuePair<string, string>>();
    }

    /// <summary>Installed app storage usage.</summary>
    public class SystemStorageAppInfo
    {
        public string PackageName { get; set; }
        public string Label { get; set; }
        public long AppBytes { get; set; }
        public long DataBytes { get; set; }
        public long CacheBytes { get; set; }
        public long TotalBytes => AppBytes + DataBytes + CacheBytes;
    }

    /// <summary>Running process entry.</summary>
    public class SystemProcessInfo
    {
        public int Pid { get; set; }
        public int Uid { get; set; }
        public string ProcessName { get; set; }
        public string PackageName { get; set; }
        public string Label { get; set; }
        public string Importance { get; set; }
        public long PssKb { get; set; }
        public bool CanKill { get; set; }
    }

    /// <summary>Memory / swap / zram overview.</summary>
    public class SystemMemoryInfo
    {
        public long TotalRamBytes { get; set; }
        public long AvailableRamBytes { get; set; }
        public long ThresholdBytes { get; set; }
        public bool LowMemory { get; set; }
        public long CachedBytes { get; set; }
        public long SwapTotalBytes { get; set; }
        public long SwapFreeBytes { get; set; }
        public long ZramTotalBytes { get; set; }
        public List<SystemMemoryAppUsage> Apps { get; set; } = new List<SystemMemoryAppUsage>();
        public string Notes { get; set; }
    }

    /// <summary>Per-app RAM usage.</summary>
    public class SystemMemoryAppUsage
    {
        public string PackageName { get; set; }
        public string Label { get; set; }
        public long PssBytes { get; set; }
        public long PrivateDirtyBytes { get; set; }
    }

    /// <summary>Networking overview.</summary>
    public class SystemNetworkInfo
    {
        public string ActiveConnectionType { get; set; }
        public bool IsConnected { get; set; }
        public bool IsMetered { get; set; }
        public string WifiStatus { get; set; }
        public string WifiSsid { get; set; }
        public int? WifiRssi { get; set; }
        public string CellularStatus { get; set; }
        public string CellularNetworkType { get; set; }
        public int? CellularSignalDbm { get; set; }
        public List<string> IpAddresses { get; set; } = new List<string>();
        public List<string> DnsServers { get; set; } = new List<string>();
        public bool VpnActive { get; set; }
        public string ProxyHost { get; set; }
        public string Notes { get; set; }
        public bool HasUsageAccess { get; set; }
        public List<SystemNetworkAppUsage> Apps { get; set; } = new List<SystemNetworkAppUsage>();
    }

    /// <summary>Per-app data usage.</summary>
    public class SystemNetworkAppUsage
    {
        public string PackageName { get; set; }
        public string Label { get; set; }
        public long MobileRxBytes { get; set; }
        public long MobileTxBytes { get; set; }
        public long WifiRxBytes { get; set; }
        public long WifiTxBytes { get; set; }
        public long TotalBytes => MobileRxBytes + MobileTxBytes + WifiRxBytes + WifiTxBytes;
    }
}
