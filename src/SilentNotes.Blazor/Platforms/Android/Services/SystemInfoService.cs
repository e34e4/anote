// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.Hardware.Camera2;
using Android.OS;
using Android.OS.Storage;
using Android.Provider;
using Android.Telephony;
using Java.Util;
using SilentNotes.Models;
using SilentNotes.Services;
using InetAddress = Java.Net.InetAddress;
using NetNetworkInterface = System.Net.NetworkInformation.NetworkInterface;
using AConnectivityManager = global::Android.Net.ConnectivityManager;
using ALinkAddress = global::Android.Net.LinkAddress;
using ALinkProperties = global::Android.Net.LinkProperties;
using ANetwork = global::Android.Net.Network;
using ANetworkCapabilities = global::Android.Net.NetworkCapabilities;
using ANetworkStats = global::Android.App.Usage.NetworkStats;
using ANetworkStatsManager = global::Android.App.Usage.NetworkStatsManager;
using AProxyInfo = global::Android.Net.ProxyInfo;
using ATransportType = global::Android.Net.TransportType;
using AUri = global::Android.Net.Uri;
using AWifiInfo = global::Android.Net.Wifi.WifiInfo;
using AWifiManager = global::Android.Net.Wifi.WifiManager;
using BatteryStatus = global::Android.OS.BatteryStatus;
using ConnectivityType = global::Android.Net.ConnectivityType;
using Debug = global::Android.OS.Debug;
using Process = global::Android.OS.Process;
using SysEnvironment = System.Environment;
using ThermalStatus = global::Android.OS.ThermalStatus;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="ISystemInfoService"/>.
    /// </summary>
    internal class SystemInfoService : ISystemInfoService
    {
        private const string PrefsName = "anote_system_info";
        private const string PrefLastNearFull = "last_near_full_charge_utc_ticks";
        private readonly IAppContextService _appContext;

        public SystemInfoService(IAppContextService appContext)
        {
            _appContext = appContext;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasUsageAccess
        {
            get
            {
                try
                {
                    Context context = _appContext.Context;
                    var appOps = context.GetSystemService(Context.AppOpsService) as AppOpsManager;
                    if (appOps == null)
                        return false;
                    int mode;
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        mode = (int)appOps.UnsafeCheckOpNoThrow(
                            AppOpsManager.OpstrGetUsageStats,
                            Process.MyUid(),
                            context.PackageName);
                    }
                    else
                    {
#pragma warning disable CS0618
                        mode = (int)appOps.CheckOpNoThrow(
                            AppOpsManager.OpstrGetUsageStats,
                            Process.MyUid(),
                            context.PackageName);
#pragma warning restore CS0618
                    }
                    return mode == (int)AppOpsManagerMode.Allowed;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public void OpenUsageAccessSettings()
        {
            try
            {
                Context context = _appContext.Context;
                Intent intent = new Intent(Settings.ActionUsageAccessSettings);
                intent.AddFlags(ActivityFlags.NewTask);
                intent.SetData(AUri.Parse("package:" + context.PackageName));
                try
                {
                    context.StartActivity(intent);
                }
                catch
                {
                    Intent fallback = new Intent(Settings.ActionUsageAccessSettings);
                    fallback.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(fallback);
                }
            }
            catch
            {
                // ignored
            }
        }

        /// <inheritdoc/>
        public Task<SystemBatteryInfo> GetBatteryInfoAsync(SystemBatterySortMode sortMode)
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                IntentFilter filter = new IntentFilter(Intent.ActionBatteryChanged);
                Intent battery = context.RegisterReceiver(null, filter);
                var info = new SystemBatteryInfo
                {
                    HasUsageAccess = HasUsageAccess,
                };

                if (battery != null)
                {
                    int level = battery.GetIntExtra(BatteryManager.ExtraLevel, -1);
                    int scale = battery.GetIntExtra(BatteryManager.ExtraScale, -1);
                    info.LevelPercent = (level >= 0 && scale > 0) ? (int)Math.Round(100.0 * level / scale) : -1;
                    info.Health = BatteryHealthToString(battery.GetIntExtra(BatteryManager.ExtraHealth, (int)BatteryHealth.Unknown));
                    info.Status = BatteryStatusToString(battery.GetIntExtra(BatteryManager.ExtraStatus, (int)BatteryStatus.Unknown));
                    info.Plugged = BatteryPluggedToString(battery.GetIntExtra(BatteryManager.ExtraPlugged, 0));
                    int tempTenths = battery.GetIntExtra(BatteryManager.ExtraTemperature, int.MinValue);
                    if (tempTenths != int.MinValue)
                        info.TemperatureCelsius = tempTenths / 10.0;
                    int voltageMv = battery.GetIntExtra(BatteryManager.ExtraVoltage, -1);
                    if (voltageMv > 0)
                        info.VoltageVolts = voltageMv / 1000.0;
                    info.Technology = battery.GetStringExtra(BatteryManager.ExtraTechnology);
                }

                try
                {
                    var power = context.GetSystemService(Context.PowerService) as PowerManager;
                    if (power != null)
                    {
                        info.IsDoze = power.IsDeviceIdleMode;
                        info.IsPowerSave = power.IsPowerSaveMode;
                    }
                }
                catch
                {
                    // ignored
                }

                try
                {
                    var bm = context.GetSystemService(Context.BatteryService) as BatteryManager;
                    if (bm != null)
                    {
                        long currentUa = bm.GetLongProperty((int)BatteryProperty.CurrentNow);
                        if (currentUa != long.MinValue && currentUa != 0)
                            info.CurrentMilliAmps = currentUa / 1000.0;
                        long counter = bm.GetLongProperty((int)BatteryProperty.ChargeCounter);
                        if (counter > 0)
                            info.ChargeCounterMicroAh = counter;
                        if ((int)Build.VERSION.SdkInt >= 34)
                        {
                            int cycles = -1;
                            try
                            {
                                cycles = battery?.GetIntExtra(BatteryManager.ExtraCycleCount, -1) ?? -1;
                            }
                            catch
                            {
                                cycles = battery?.GetIntExtra("android.os.extra.CYCLE_COUNT", -1) ?? -1;
                            }
                            if (cycles < 0)
                            {
                                try
                                {
                                    // BatteryProperty.CycleCount == 6 on API 34+
                                    long cycleProp = bm.GetLongProperty(6);
                                    if (cycleProp >= 0)
                                        cycles = (int)cycleProp;
                                }
                                catch
                                {
                                    // ignored
                                }
                            }
                            if (cycles >= 0)
                                info.CycleCount = cycles;
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                RememberNearFullCharge(context, info.LevelPercent);
                info.LastNearFullChargeLocal = ReadLastNearFullCharge(context);

                if (!info.HasUsageAccess)
                {
                    info.Notes = "Usage access is required for per-app battery estimates. Tap “Grant usage access”.";
                }
                else
                {
                    info.Apps = QueryBatteryApps(context, sortMode);
                    info.Notes = "Per-app values are usage estimates (screen / CPU / wakelock events). Android does not expose exact mAh to third-party apps.";
                }

                return info;
            });
        }

        /// <inheritdoc/>
        public Task<SystemHardwareInfo> GetHardwareInfoAsync()
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                var info = new SystemHardwareInfo
                {
                    Manufacturer = Build.Manufacturer,
                    Model = Build.Model,
                    Device = Build.Device,
                    Brand = Build.Brand,
                    Product = Build.Product,
                    Hardware = Build.Hardware,
                    Board = Build.Board,
                    AndroidVersion = Build.VERSION.Release,
                    SdkInt = (int)Build.VERSION.SdkInt,
                    BuildId = Build.Id,
                    BuildDisplay = Build.Display,
                    BuildNumber = Build.Display,
                    SecurityPatch = Build.VERSION.SecurityPatch,
                    Fingerprint = Build.Fingerprint,
                    CpuAbi = string.Join(", ", Build.SupportedAbis ?? new string[0]),
                    CpuCores = Java.Lang.Runtime.GetRuntime()?.AvailableProcessors() ?? SysEnvironment.ProcessorCount,
                };

                try
                {
                    string cpuInfo = TryReadFirstMatchingLine("/proc/cpuinfo", "Hardware", "model name", "Processor");
                    if (!string.IsNullOrWhiteSpace(cpuInfo))
                        info.CpuHardware = cpuInfo;
                }
                catch
                {
                    // ignored
                }

                try
                {
                    var power = context.GetSystemService(Context.PowerService) as PowerManager;
                    if (power != null && Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                        info.ThermalStatus = ThermalStatusToString(power.CurrentThermalStatus);
                }
                catch
                {
                    // ignored
                }

                try
                {
                    var sensors = context.GetSystemService(Context.SensorService) as SensorManager;
                    if (sensors != null)
                    {
                        foreach (Sensor sensor in sensors.GetSensorList(SensorType.All))
                        {
                            if (sensor == null)
                                continue;
                            info.Sensors.Add($"{sensor.Name} ({sensor.Vendor}, type={sensor.StringType ?? sensor.Type.ToString()})");
                        }
                        info.Sensors.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    info.Extra.Add(new KeyValuePair<string, string>("Sensors error", ex.Message));
                }

                try
                {
                    var cameraManager = context.GetSystemService(Context.CameraService) as CameraManager;
                    if (cameraManager != null)
                    {
                        foreach (string id in cameraManager.GetCameraIdList())
                        {
                            var chars = cameraManager.GetCameraCharacteristics(id);
                            var facing = chars.Get(CameraCharacteristics.LensFacing);
                            string facingText = facing?.ToString() ?? "?";
                            var size = chars.Get(CameraCharacteristics.SensorInfoPixelArraySize) as global::Android.Util.Size;
                            string res = size != null ? $"{size.Width}x{size.Height}" : "?";
                            info.Cameras.Add($"Camera {id}: facing={facingText}, sensor={res}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    info.Extra.Add(new KeyValuePair<string, string>("Camera error", ex.Message));
                }

                info.Extra.Add(new KeyValuePair<string, string>("Host", Build.Host));
                info.Extra.Add(new KeyValuePair<string, string>("Tags", Build.Tags));
                info.Extra.Add(new KeyValuePair<string, string>("Type", Build.Type));
                info.Extra.Add(new KeyValuePair<string, string>("User", Build.User));
                if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                {
                    try
                    {
                        info.Extra.Add(new KeyValuePair<string, string>("SKU", Build.Sku));
                        info.Extra.Add(new KeyValuePair<string, string>("SoC manufacturer", Build.SocManufacturer));
                        info.Extra.Add(new KeyValuePair<string, string>("SoC model", Build.SocModel));
                    }
                    catch
                    {
                        // ignored
                    }
                }

                return info;
            });
        }

        /// <inheritdoc/>
        public Task<SystemStorageAppInfo[]> GetStorageAppsAsync()
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                PackageManager pm = context.PackageManager;
                var result = new List<SystemStorageAppInfo>();
                IList<ApplicationInfo> apps;
                try
                {
                    apps = pm.GetInstalledApplications(PackageInfoFlags.MatchAll);
                }
                catch
                {
                    apps = pm.GetInstalledApplications(PackageInfoFlags.MetaData);
                }

                StorageStatsManager statsManager = null;
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                        statsManager = context.GetSystemService(Context.StorageStatsService) as StorageStatsManager;
                }
                catch
                {
                    // ignored
                }

                foreach (ApplicationInfo app in apps)
                {
                    if (app == null || string.IsNullOrEmpty(app.PackageName))
                        continue;
                    if ((app.Flags & ApplicationInfoFlags.System) != 0 && (app.Flags & ApplicationInfoFlags.UpdatedSystemApp) == 0)
                        continue;

                    var entry = new SystemStorageAppInfo
                    {
                        PackageName = app.PackageName,
                        Label = SafeLabel(pm, app),
                    };

                    try
                    {
                        if (statsManager != null && Build.VERSION.SdkInt >= BuildVersionCodes.O)
                        {
                            UUID uuid = StorageManager.UuidDefault;
                            StorageStats stats = statsManager.QueryStatsForPackage(uuid, app.PackageName, Process.MyUserHandle());
                            entry.AppBytes = stats.AppBytes;
                            entry.DataBytes = stats.DataBytes;
                            entry.CacheBytes = stats.CacheBytes;
                        }
                        else
                        {
#pragma warning disable CS0618
                            PackageInfo pkg = pm.GetPackageInfo(app.PackageName, 0);
                            string sourceDir = pkg?.ApplicationInfo?.SourceDir;
#pragma warning restore CS0618
                            if (!string.IsNullOrEmpty(sourceDir) && File.Exists(sourceDir))
                                entry.AppBytes = new FileInfo(sourceDir).Length;
                        }
                    }
                    catch
                    {
                        // Skip apps we cannot query (permission / cross-user).
                        continue;
                    }

                    if (entry.TotalBytes <= 0)
                        continue;
                    result.Add(entry);
                }

                return result
                    .OrderByDescending(a => a.TotalBytes)
                    .ToArray();
            });
        }

        /// <inheritdoc/>
        public Task<SystemProcessesSnapshot> GetRunningProcessesAsync()
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                var am = context.GetSystemService(Context.ActivityService) as ActivityManager;
                PackageManager pm = context.PackageManager;
                var byPid = new Dictionary<int, SystemProcessInfo>();

                void Upsert(SystemProcessInfo entry)
                {
                    if (entry == null || entry.Pid <= 0)
                        return;
                    if (byPid.TryGetValue(entry.Pid, out SystemProcessInfo existing))
                    {
                        if (entry.PssKb > existing.PssKb)
                            existing.PssKb = entry.PssKb;
                        if (string.IsNullOrEmpty(existing.PackageName) && !string.IsNullOrEmpty(entry.PackageName))
                            existing.PackageName = entry.PackageName;
                        if (string.IsNullOrEmpty(existing.Label) || existing.Label == existing.ProcessName)
                            existing.Label = entry.Label ?? existing.Label;
                        if (entry.SortRank < existing.SortRank)
                        {
                            existing.SortRank = entry.SortRank;
                            existing.Importance = entry.Importance;
                        }
                        if (!string.IsNullOrEmpty(entry.Source) && existing.Source != entry.Source)
                            existing.Source = existing.Source + "+" + entry.Source;
                        existing.CanKill = existing.CanKill || entry.CanKill;
                    }
                    else
                    {
                        byPid[entry.Pid] = entry;
                    }
                }

                // 1) ActivityManager running processes (often only same-UID / visible on modern Android).
                try
                {
                    IList<ActivityManager.RunningAppProcessInfo> processes = am?.RunningAppProcesses;
                    if (processes != null)
                    {
                        foreach (ActivityManager.RunningAppProcessInfo proc in processes)
                        {
                            if (proc == null)
                                continue;
                            string package = proc.ProcessName;
                            if (proc.PkgList != null && proc.PkgList.Count > 0)
                                package = proc.PkgList[0];

                            long pssKb = TryGetPssKb(am, proc.Pid);
                            int rank = ImportanceToRank(proc.Importance);
                            Upsert(new SystemProcessInfo
                            {
                                Pid = proc.Pid,
                                Uid = proc.Uid,
                                ProcessName = proc.ProcessName,
                                PackageName = package,
                                Label = SafeLabelByPackage(pm, package) ?? proc.ProcessName,
                                Importance = FormatImportance(proc.Importance),
                                PssKb = pssKb,
                                SortRank = rank,
                                Source = "am",
                                CanKill = !string.Equals(package, context.PackageName, StringComparison.Ordinal)
                                    && rank >= ImportanceToRank(Importance.Service),
                            });
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                // 2) Running services (background / FGS) — deprecated but still useful when visible.
                try
                {
#pragma warning disable CS0618
                    IList<ActivityManager.RunningServiceInfo> services = am?.GetRunningServices(int.MaxValue);
#pragma warning restore CS0618
                    if (services != null)
                    {
                        foreach (ActivityManager.RunningServiceInfo svc in services)
                        {
                            if (svc == null || svc.Pid <= 0)
                                continue;
                            string package = svc.Service?.PackageName;
                            if (string.IsNullOrEmpty(package))
                                package = svc.Process;
                            string detail = svc.Service?.ClassName;
                            Upsert(new SystemProcessInfo
                            {
                                Pid = svc.Pid,
                                Uid = svc.Uid,
                                ProcessName = svc.Process ?? package,
                                PackageName = package,
                                Label = SafeLabelByPackage(pm, package) ?? package,
                                Importance = svc.Foreground ? "Foreground service" : "Service",
                                PssKb = TryGetPssKb(am, svc.Pid),
                                SortRank = svc.Foreground ? ImportanceToRank(Importance.Foreground) : ImportanceToRank(Importance.Service),
                                Source = "service",
                                CanKill = !string.Equals(package, context.PackageName, StringComparison.Ordinal),
                            });
                            if (!string.IsNullOrEmpty(detail) && byPid.TryGetValue(svc.Pid, out SystemProcessInfo existing))
                            {
                                if (string.IsNullOrEmpty(existing.Importance) || existing.Importance == "Service")
                                    existing.Importance = (svc.Foreground ? "FGS " : "Service ") + ShortClassName(detail);
                            }
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                // 3) /proc scan for PIDs ActivityManager hides (best-effort; SELinux may block cmdline).
                try
                {
                    foreach (string dir in Directory.GetDirectories("/proc"))
                    {
                        string name = Path.GetFileName(dir);
                        if (!int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
                            continue;
                        if (byPid.ContainsKey(pid))
                            continue;

                        string cmdline = TryReadProcCmdline(dir);
                        int uid = TryReadProcUid(dir);
                        string processName = cmdline;
                        if (string.IsNullOrWhiteSpace(processName))
                            processName = TryReadProcComm(dir) ?? ("pid:" + pid);

                        string package = null;
                        if (uid > 0)
                        {
                            try
                            {
                                string[] pkgs = pm.GetPackagesForUid(uid);
                                if (pkgs != null && pkgs.Length > 0)
                                    package = pkgs[0];
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                        if (string.IsNullOrEmpty(package) && processName.Contains('.', StringComparison.Ordinal) && !processName.Contains(' ', StringComparison.Ordinal))
                            package = processName.Split(':')[0];

                        // Skip pure kernel threads (empty cmdline + bracketed comm).
                        if (string.IsNullOrEmpty(cmdline) && processName.StartsWith('[') && processName.EndsWith(']'))
                            continue;

                        Upsert(new SystemProcessInfo
                        {
                            Pid = pid,
                            Uid = uid,
                            ProcessName = processName,
                            PackageName = package ?? processName,
                            Label = SafeLabelByPackage(pm, package) ?? processName,
                            Importance = "Background (/proc)",
                            PssKb = TryGetPssKb(am, pid),
                            SortRank = ImportanceToRank(Importance.Background),
                            Source = "proc",
                            CanKill = !string.IsNullOrEmpty(package)
                                && !string.Equals(package, context.PackageName, StringComparison.Ordinal)
                                && uid >= 10000,
                        });
                    }
                }
                catch
                {
                    // ignored
                }

                var snapshot = new SystemProcessesSnapshot
                {
                    Processes = byPid.Values
                        .OrderBy(p => p.SortRank)
                        .ThenByDescending(p => p.PssKb)
                        .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    AutostartApps = QueryAutostartApps(context, pm),
                    AccessibilityApps = QueryAccessibilityApps(context, pm),
                    Notes = "Android hides most other apps from ActivityManager. Background rows also come from running services and /proc when readable.",
                };
                return snapshot;
            });
        }

        /// <inheritdoc/>
        public Task<bool> KillProcessAsync(SystemProcessInfo process)
        {
            return Task.Run(() =>
            {
                if (process == null || string.IsNullOrEmpty(process.PackageName))
                    return false;
                try
                {
                    Context context = _appContext.Context;
                    if (string.Equals(process.PackageName, context.PackageName, StringComparison.Ordinal))
                        return false;
                    var am = context.GetSystemService(Context.ActivityService) as ActivityManager;
                    am?.KillBackgroundProcesses(process.PackageName);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public Task<SystemMemoryInfo> GetMemoryInfoAsync()
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                var am = context.GetSystemService(Context.ActivityService) as ActivityManager;
                var info = new SystemMemoryInfo();
                if (am != null)
                {
                    var mem = new ActivityManager.MemoryInfo();
                    am.GetMemoryInfo(mem);
                    info.TotalRamBytes = mem.TotalMem;
                    info.AvailableRamBytes = mem.AvailMem;
                    info.ThresholdBytes = mem.Threshold;
                    info.LowMemory = mem.LowMemory;
                }

                try
                {
                    var meminfo = ParseMemInfo();
                    if (meminfo.TryGetValue("Cached", out long cachedKb))
                        info.CachedBytes = cachedKb * 1024;
                    if (meminfo.TryGetValue("SwapTotal", out long swapTotalKb))
                        info.SwapTotalBytes = swapTotalKb * 1024;
                    if (meminfo.TryGetValue("SwapFree", out long swapFreeKb))
                        info.SwapFreeBytes = swapFreeKb * 1024;
                    if (meminfo.TryGetValue("VmallocTotal", out _))
                    {
                        // ignore
                    }
                }
                catch
                {
                    // ignored
                }

                info.ZramTotalBytes = TryReadZramTotal();

                PackageManager pm = context.PackageManager;
                var apps = new List<SystemMemoryAppUsage>();
                try
                {
                    IList<ActivityManager.RunningAppProcessInfo> processes = am?.RunningAppProcesses;
                    if (processes != null)
                    {
                        foreach (ActivityManager.RunningAppProcessInfo proc in processes)
                        {
                            if (proc?.PkgList == null || proc.PkgList.Count == 0)
                                continue;
                            string package = proc.PkgList[0];
                            try
                            {
                                Debug.MemoryInfo[] memInfos = am.GetProcessMemoryInfo(new[] { proc.Pid });
                                if (memInfos == null || memInfos.Length == 0)
                                    continue;
                                Debug.MemoryInfo mi = memInfos[0];
                                apps.Add(new SystemMemoryAppUsage
                                {
                                    PackageName = package,
                                    Label = SafeLabelByPackage(pm, package) ?? package,
                                    PssBytes = mi.TotalPss * 1024L,
                                    PrivateDirtyBytes = mi.TotalPrivateDirty * 1024L,
                                });
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                info.Apps = apps
                    .GroupBy(a => a.PackageName)
                    .Select(g => new SystemMemoryAppUsage
                    {
                        PackageName = g.Key,
                        Label = g.First().Label,
                        PssBytes = g.Sum(x => x.PssBytes),
                        PrivateDirtyBytes = g.Sum(x => x.PrivateDirtyBytes),
                    })
                    .OrderByDescending(a => a.PssBytes)
                    .ToList();

                info.Notes = "PSS is an estimate; Android limits visibility of other apps' memory on newer versions.";
                return info;
            });
        }

        /// <inheritdoc/>
        public Task<SystemNetworkInfo> GetNetworkInfoAsync()
        {
            return Task.Run(() =>
            {
                Context context = _appContext.Context;
                var info = new SystemNetworkInfo
                {
                    HasUsageAccess = HasUsageAccess,
                };

                try
                {
                    var cm = context.GetSystemService(Context.ConnectivityService) as AConnectivityManager;
                    if (cm != null)
                    {
                        info.IsMetered = cm.IsActiveNetworkMetered;
                        ANetwork active = cm.ActiveNetwork;
                        ANetworkCapabilities caps = active != null ? cm.GetNetworkCapabilities(active) : null;
                        info.IsConnected = caps != null;
                        if (caps != null)
                        {
                            if (caps.HasTransport(ATransportType.Wifi))
                                info.ActiveConnectionType = "Wi‑Fi";
                            else if (caps.HasTransport(ATransportType.Cellular))
                                info.ActiveConnectionType = "Cellular";
                            else if (caps.HasTransport(ATransportType.Ethernet))
                                info.ActiveConnectionType = "Ethernet";
                            else if (caps.HasTransport(ATransportType.Bluetooth))
                                info.ActiveConnectionType = "Bluetooth";
                            else if (caps.HasTransport(ATransportType.Vpn))
                                info.ActiveConnectionType = "VPN";
                            else
                                info.ActiveConnectionType = "Other";

                            info.VpnActive = caps.HasTransport(ATransportType.Vpn);
                        }
                        else
                        {
                            info.ActiveConnectionType = "None";
                        }

                        ALinkProperties link = active != null ? cm.GetLinkProperties(active) : null;
                        if (link != null)
                        {
                            foreach (ALinkAddress addr in link.LinkAddresses)
                            {
                                string text = addr?.Address?.HostAddress;
                                if (!string.IsNullOrEmpty(text))
                                    info.IpAddresses.Add(text);
                            }
                            foreach (InetAddress dns in link.DnsServers)
                            {
                                string text = dns?.HostAddress;
                                if (!string.IsNullOrEmpty(text))
                                    info.DnsServers.Add(text);
                            }
                            try
                            {
                                AProxyInfo proxy = link.HttpProxy;
                                if (proxy != null && !string.IsNullOrEmpty(proxy.Host))
                                    info.ProxyHost = proxy.Host + ":" + proxy.Port;
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    info.Notes = "Connectivity: " + ex.Message;
                }

                try
                {
                    var wifi = context.GetSystemService(Context.WifiService) as AWifiManager;
                    if (wifi != null)
                    {
#pragma warning disable CS0618
                        info.WifiStatus = wifi.IsWifiEnabled ? "Enabled" : "Disabled";
                        AWifiInfo wi = wifi.ConnectionInfo;
#pragma warning restore CS0618
                        if (wi != null)
                        {
                            string ssid = wi.SSID;
                            if (!string.IsNullOrEmpty(ssid) && ssid != "<unknown ssid>")
                                info.WifiSsid = ssid.Trim('"');
                            info.WifiRssi = wi.Rssi;
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                try
                {
                    var tm = context.GetSystemService(Context.TelephonyService) as TelephonyManager;
                    if (tm != null)
                    {
                        info.CellularStatus = tm.SimState.ToString();
#pragma warning disable CS0618
                        info.CellularNetworkType = tm.NetworkOperatorName;
                        if (string.IsNullOrEmpty(info.CellularNetworkType))
                            info.CellularNetworkType = tm.NetworkType.ToString();
#pragma warning restore CS0618
                    }
                }
                catch
                {
                    // ignored
                }

                if (info.IpAddresses.Count == 0)
                {
                    try
                    {
                        foreach (NetNetworkInterface ni in NetNetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus != OperationalStatus.Up)
                                continue;
                            foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                            {
                                if (ip.Address.AddressFamily == AddressFamily.InterNetwork
                                    || ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    info.IpAddresses.Add(ip.Address.ToString());
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                string httpProxy = Java.Lang.JavaSystem.GetProperty("http.proxyHost");
                string httpPort = Java.Lang.JavaSystem.GetProperty("http.proxyPort");
                if (!string.IsNullOrEmpty(httpProxy) && string.IsNullOrEmpty(info.ProxyHost))
                    info.ProxyHost = httpProxy + (string.IsNullOrEmpty(httpPort) ? "" : ":" + httpPort);

                if (!info.HasUsageAccess)
                {
                    info.Notes = (info.Notes == null ? "" : info.Notes + " ")
                        + "Usage access is required for per-app data usage.";
                }
                else
                {
                    info.Apps = QueryNetworkApps(context);
                }

                return info;
            });
        }

        private List<SystemBatteryAppUsage> QueryBatteryApps(Context context, SystemBatterySortMode sortMode)
        {
            var result = new List<SystemBatteryAppUsage>();
            try
            {
                var usm = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usm == null)
                    return result;

                long end = Java.Lang.JavaSystem.CurrentTimeMillis();
                long start = end - 24L * 60L * 60L * 1000L;
                IDictionary<string, UsageStats> stats = usm.QueryAndAggregateUsageStats(start, end);
                PackageManager pm = context.PackageManager;

                // Usage events for wakelock-ish / foreground service counts.
                Dictionary<string, long> wakeApprox = new Dictionary<string, long>(StringComparer.Ordinal);
                Dictionary<string, long> cpuApproxMs = new Dictionary<string, long>(StringComparer.Ordinal);
                try
                {
                    UsageEvents events = usm.QueryEvents(start, end);
                    var evt = new UsageEvents.Event();
                    while (events != null && events.HasNextEvent)
                    {
                        events.GetNextEvent(evt);
                        string pkg = evt.PackageName;
                        if (string.IsNullOrEmpty(pkg))
                            continue;
                        if (evt.EventType == UsageEventType.ForegroundServiceStart
                            || evt.EventType == UsageEventType.UserInteraction
                            || evt.EventType == UsageEventType.ShortcutInvocation)
                        {
                            wakeApprox.TryGetValue(pkg, out long c);
                            wakeApprox[pkg] = c + 1;
                        }
                        if (evt.EventType == UsageEventType.ActivityResumed
                            || evt.EventType == UsageEventType.ActivityPaused
                            || evt.EventType == UsageEventType.ForegroundServiceStart
                            || evt.EventType == UsageEventType.ForegroundServiceStop)
                        {
                            cpuApproxMs.TryGetValue(pkg, out long c);
                            cpuApproxMs[pkg] = c + 1;
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                if (stats != null)
                {
                    foreach (KeyValuePair<string, UsageStats> pair in stats)
                    {
                        UsageStats st = pair.Value;
                        if (st == null)
                            continue;
                        long screenMs = st.TotalTimeInForeground;
                        wakeApprox.TryGetValue(pair.Key, out long wakes);
                        cpuApproxMs.TryGetValue(pair.Key, out long cpuEvents);
                        // Prefer TotalTimeVisible when available (API 29+).
                        long cpuMs = screenMs;
                        try
                        {
                            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                            {
                                long visible = st.TotalTimeVisible;
                                cpuMs = Math.Max(screenMs, visible);
                            }
                        }
                        catch
                        {
                            // ignored
                        }

                        if (screenMs <= 0 && wakes <= 0 && cpuMs <= 0)
                            continue;

                        result.Add(new SystemBatteryAppUsage
                        {
                            PackageName = pair.Key,
                            Label = SafeLabelByPackage(pm, pair.Key) ?? pair.Key,
                            ScreenTime = TimeSpan.FromMilliseconds(Math.Max(0, screenMs)),
                            CpuTime = TimeSpan.FromMilliseconds(Math.Max(0, cpuMs)),
                            WakelockCount = wakes,
                            Detail = cpuEvents > 0 ? $"{cpuEvents} usage events" : null,
                        });
                    }
                }

                IOrderedEnumerable<SystemBatteryAppUsage> ordered = sortMode switch
                {
                    SystemBatterySortMode.CpuTime => result.OrderByDescending(a => a.CpuTime),
                    SystemBatterySortMode.Wakelocks => result.OrderByDescending(a => a.WakelockCount).ThenByDescending(a => a.ScreenTime),
                    _ => result.OrderByDescending(a => a.ScreenTime),
                };
                return ordered.Take(200).ToList();
            }
            catch
            {
                return result;
            }
        }

        private List<SystemNetworkAppUsage> QueryNetworkApps(Context context)
        {
            var result = new List<SystemNetworkAppUsage>();
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.M)
                    return result;

                var nsm = context.GetSystemService(Context.NetworkStatsService) as ANetworkStatsManager;
                if (nsm == null)
                    return result;

                PackageManager pm = context.PackageManager;
                long end = Java.Lang.JavaSystem.CurrentTimeMillis();
                long start = end - 30L * 24L * 60L * 60L * 1000L;
                var byUid = new Dictionary<int, SystemNetworkAppUsage>();

                void Accumulate(ANetworkStats stats, bool mobile)
                {
                    if (stats == null)
                        return;
                    try
                    {
                        var bucket = new ANetworkStats.Bucket();
                        while (stats.HasNextBucket)
                        {
                            stats.GetNextBucket(bucket);
                            int uid = bucket.Uid;
                            if (!byUid.TryGetValue(uid, out SystemNetworkAppUsage entry))
                            {
                                string[] packages = pm.GetPackagesForUid(uid);
                                string package = packages != null && packages.Length > 0 ? packages[0] : ("uid:" + uid);
                                entry = new SystemNetworkAppUsage
                                {
                                    PackageName = package,
                                    Label = SafeLabelByPackage(pm, package) ?? package,
                                };
                                byUid[uid] = entry;
                            }
                            if (mobile)
                            {
                                entry.MobileRxBytes += bucket.RxBytes;
                                entry.MobileTxBytes += bucket.TxBytes;
                            }
                            else
                            {
                                entry.WifiRxBytes += bucket.RxBytes;
                                entry.WifiTxBytes += bucket.TxBytes;
                            }
                        }
                    }
                    finally
                    {
                        try { stats.Close(); } catch { /* ignored */ }
                    }
                }

                try
                {
                    Accumulate(nsm.QuerySummary(ConnectivityType.Mobile, null, start, end), true);
                }
                catch
                {
                    // ignored
                }
                try
                {
                    Accumulate(nsm.QuerySummary(ConnectivityType.Wifi, null, start, end), false);
                }
                catch
                {
                    // ignored
                }

                return byUid.Values
                    .Where(a => a.TotalBytes > 0)
                    .OrderByDescending(a => a.TotalBytes)
                    .Take(200)
                    .ToList();
            }
            catch
            {
                return result;
            }
        }

        private static List<SystemSpecialAppInfo> QueryAutostartApps(Context context, PackageManager pm)
        {
            var byPackage = new Dictionary<string, SystemSpecialAppInfo>(StringComparer.Ordinal);
            string[] actions =
            {
                Intent.ActionBootCompleted,
                Intent.ActionLockedBootCompleted,
                "android.intent.action.QUICKBOOT_POWERON",
                "com.htc.intent.action.QUICKBOOT_POWERON",
            };

            foreach (string action in actions)
            {
                try
                {
                    var intent = new Intent(action);
                    IList<ResolveInfo> receivers = pm.QueryBroadcastReceivers(intent, PackageInfoFlags.MatchAll);
                    if (receivers == null)
                        continue;
                    foreach (ResolveInfo ri in receivers)
                    {
                        string package = ri.ActivityInfo?.PackageName ?? ri.ResolvePackageName;
                        if (string.IsNullOrEmpty(package))
                            continue;
                        string component = ri.ActivityInfo?.Name;
                        if (!byPackage.TryGetValue(package, out SystemSpecialAppInfo entry))
                        {
                            entry = new SystemSpecialAppInfo
                            {
                                PackageName = package,
                                Label = SafeLabelByPackage(pm, package) ?? package,
                                Detail = ShortClassName(component),
                                IsEnabled = ri.ActivityInfo == null || ri.ActivityInfo.Enabled,
                            };
                            byPackage[package] = entry;
                        }
                        else if (!string.IsNullOrEmpty(component))
                        {
                            string shortName = ShortClassName(component);
                            if (!string.IsNullOrEmpty(shortName) && (entry.Detail == null || !entry.Detail.Contains(shortName, StringComparison.Ordinal)))
                                entry.Detail = string.IsNullOrEmpty(entry.Detail) ? shortName : entry.Detail + ", " + shortName;
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return byPackage.Values
                .OrderBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<SystemSpecialAppInfo> QueryAccessibilityApps(Context context, PackageManager pm)
        {
            var result = new List<SystemSpecialAppInfo>();
            var enabledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string enabled = Settings.Secure.GetString(context.ContentResolver, Settings.Secure.EnabledAccessibilityServices);
                if (!string.IsNullOrEmpty(enabled))
                {
                    foreach (string part in enabled.Split(':', StringSplitOptions.RemoveEmptyEntries))
                        enabledIds.Add(part.Trim());
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                // Use global:: — namespace SilentNotes.Platforms.Android would otherwise shadow Android.*.
                var accessibility = context.GetSystemService(Context.AccessibilityService) as global::Android.Views.Accessibility.AccessibilityManager;
                IList<global::Android.AccessibilityServices.AccessibilityServiceInfo> installed = accessibility?.InstalledAccessibilityServiceList;
                if (installed != null)
                {
                    foreach (global::Android.AccessibilityServices.AccessibilityServiceInfo info in installed)
                    {
                        string package = info?.ResolveInfo?.ServiceInfo?.PackageName;
                        string className = info?.ResolveInfo?.ServiceInfo?.Name;
                        if (string.IsNullOrEmpty(package))
                            continue;
                        string flatId = package + "/" + className;
                        string flatIdShort = package + "/" + ShortClassName(className);
                        bool isEnabled = enabledIds.Contains(flatId)
                            || enabledIds.Contains(flatIdShort)
                            || enabledIds.Any(id => id.StartsWith(package + "/", StringComparison.OrdinalIgnoreCase));
                        string title = null;
                        try { title = info.ResolveInfo?.LoadLabel(pm)?.ToString(); } catch { /* ignored */ }
                        result.Add(new SystemSpecialAppInfo
                        {
                            PackageName = package,
                            Label = title ?? SafeLabelByPackage(pm, package) ?? package,
                            Detail = ShortClassName(className),
                            IsEnabled = isEnabled,
                        });
                    }
                }
            }
            catch
            {
                // Fallback: enabled list only.
                foreach (string id in enabledIds)
                {
                    string package = id.Split('/')[0];
                    result.Add(new SystemSpecialAppInfo
                    {
                        PackageName = package,
                        Label = SafeLabelByPackage(pm, package) ?? package,
                        Detail = id.Contains('/') ? ShortClassName(id.Split('/')[1]) : null,
                        IsEnabled = true,
                    });
                }
            }

            return result
                .OrderByDescending(a => a.IsEnabled)
                .ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static long TryGetPssKb(ActivityManager am, int pid)
        {
            if (am == null || pid <= 0)
                return 0;
            try
            {
                Debug.MemoryInfo[] mem = am.GetProcessMemoryInfo(new[] { pid });
                if (mem != null && mem.Length > 0)
                    return mem[0].TotalPss;
            }
            catch
            {
                // ignored
            }
            return 0;
        }

        private static int ImportanceToRank(Importance importance)
        {
            int value = (int)importance;
            if (value <= (int)Importance.Foreground)
                return 0;
            if (value <= (int)Importance.Visible)
                return 1;
            if (value <= (int)Importance.Service)
                return 2;
            if (value <= (int)Importance.Background)
                return 3;
            return 4;
        }

        private static string FormatImportance(Importance importance)
        {
            int value = (int)importance;
            if (value <= (int)Importance.Foreground)
                return "Foreground";
            if (value <= (int)Importance.Visible)
                return "Visible";
            if (value <= (int)Importance.Service)
                return "Service";
            if (value <= (int)Importance.Background)
                return "Background";
            return "Cached/Empty";
        }

        private static string ShortClassName(string className)
        {
            if (string.IsNullOrEmpty(className))
                return null;
            int idx = className.LastIndexOf('.');
            return idx >= 0 && idx + 1 < className.Length ? className.Substring(idx + 1) : className;
        }

        private static string TryReadProcCmdline(string procDir)
        {
            try
            {
                string path = Path.Combine(procDir, "cmdline");
                if (!File.Exists(path))
                    return null;
                string raw = File.ReadAllText(path);
                if (string.IsNullOrEmpty(raw))
                    return null;
                return raw.Replace('\0', ' ').Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string TryReadProcComm(string procDir)
        {
            try
            {
                string path = Path.Combine(procDir, "comm");
                if (!File.Exists(path))
                    return null;
                return File.ReadAllText(path).Trim();
            }
            catch
            {
                return null;
            }
        }

        private static int TryReadProcUid(string procDir)
        {
            try
            {
                string path = Path.Combine(procDir, "status");
                if (!File.Exists(path))
                    return 0;
                foreach (string line in File.ReadLines(path))
                {
                    if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                        continue;
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid))
                        return uid;
                }
            }
            catch
            {
                // ignored
            }
            return 0;
        }

        private static void RememberNearFullCharge(Context context, int levelPercent)
        {
            if (levelPercent < 99)
                return;
            try
            {
                ISharedPreferences prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                prefs.Edit().PutLong(PrefLastNearFull, DateTime.UtcNow.Ticks).Apply();
            }
            catch
            {
                // ignored
            }
        }

        private static DateTime? ReadLastNearFullCharge(Context context)
        {
            try
            {
                ISharedPreferences prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                long ticks = prefs.GetLong(PrefLastNearFull, -1);
                if (ticks <= 0)
                    return null;
                return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            }
            catch
            {
                return null;
            }
        }

        private static string SafeLabel(PackageManager pm, ApplicationInfo app)
        {
            try
            {
                return pm.GetApplicationLabel(app)?.ToString() ?? app.PackageName;
            }
            catch
            {
                return app.PackageName;
            }
        }

        private static string SafeLabelByPackage(PackageManager pm, string packageName)
        {
            if (string.IsNullOrEmpty(packageName) || packageName.StartsWith("uid:", StringComparison.Ordinal))
                return packageName;
            try
            {
                ApplicationInfo app = pm.GetApplicationInfo(packageName, 0);
                return SafeLabel(pm, app);
            }
            catch
            {
                return packageName;
            }
        }

        private static string BatteryHealthToString(int health)
        {
            return ((BatteryHealth)health) switch
            {
                BatteryHealth.Good => "Good",
                BatteryHealth.Overheat => "Overheat",
                BatteryHealth.Dead => "Dead",
                BatteryHealth.OverVoltage => "Over voltage",
                BatteryHealth.UnspecifiedFailure => "Unspecified failure",
                BatteryHealth.Cold => "Cold",
                _ => "Unknown",
            };
        }

        private static string BatteryStatusToString(int status)
        {
            return ((BatteryStatus)status) switch
            {
                BatteryStatus.Charging => "Charging",
                BatteryStatus.Discharging => "Discharging",
                BatteryStatus.Full => "Full",
                BatteryStatus.NotCharging => "Not charging",
                _ => "Unknown",
            };
        }

        private static string BatteryPluggedToString(int plugged)
        {
            if (plugged == 0)
                return "Unplugged";
            var parts = new List<string>();
            if ((plugged & (int)BatteryPlugged.Ac) != 0)
                parts.Add("AC");
            if ((plugged & (int)BatteryPlugged.Usb) != 0)
                parts.Add("USB");
            if ((plugged & (int)BatteryPlugged.Wireless) != 0)
                parts.Add("Wireless");
            return parts.Count > 0 ? string.Join(", ", parts) : plugged.ToString(CultureInfo.InvariantCulture);
        }

        private static string ThermalStatusToString(ThermalStatus status)
        {
            return status switch
            {
                ThermalStatus.None => "None",
                ThermalStatus.Light => "Light",
                ThermalStatus.Moderate => "Moderate",
                ThermalStatus.Severe => "Severe",
                ThermalStatus.Critical => "Critical",
                ThermalStatus.Emergency => "Emergency",
                ThermalStatus.Shutdown => "Shutdown",
                _ => status.ToString(),
            };
        }

        private static string TryReadFirstMatchingLine(string path, params string[] keys)
        {
            if (!File.Exists(path))
                return null;
            foreach (string line in File.ReadLines(path))
            {
                foreach (string key in keys)
                {
                    if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0 && idx + 1 < line.Length)
                            return line.Substring(idx + 1).Trim();
                    }
                }
            }
            return null;
        }

        private static Dictionary<string, long> ParseMemInfo()
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists("/proc/meminfo"))
                return result;
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                string key = line.Substring(0, colon).Trim();
                string rest = line.Substring(colon + 1).Trim();
                string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    result[key] = value;
            }
            return result;
        }

        private static long TryReadZramTotal()
        {
            string[] paths =
            {
                "/sys/block/zram0/disksize",
                "/sys/class/zram-control/zram0/disksize",
            };
            foreach (string path in paths)
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    string text = File.ReadAllText(path).Trim();
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
                        return bytes;
                }
                catch
                {
                    // ignored
                }
            }
            return 0;
        }
    }
}
