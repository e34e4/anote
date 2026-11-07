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
        private const string PrefNetLogEnabled = "net_out_log_enabled";
        private const string PrefSavedIpsJson = "saved_ips_json";
        private const string NetOutLogFile = "/data/local/tmp/anote_net_out.log";
        private const string NetOutPidFile = "/data/local/tmp/anote_net_out.pid";
        private const string NetOutScriptFile = "/data/local/tmp/anote_net_monitor.sh";
        private const string IptablesLogRuleState =
            "OUTPUT -m state --state NEW -j LOG --log-prefix \"NET_OUT: \" --log-uid";
        private const string IptablesLogRuleConntrack =
            "OUTPUT -m conntrack --ctstate NEW -j LOG --log-prefix \"NET_OUT: \" --log-uid";
        private const string IptablesNflogRule =
            "OUTPUT -m conntrack --ctstate NEW -j NFLOG --nflog-group 5 --nflog-prefix NET_OUT";
        private const string LogTag = "aNoteSystem";
        private readonly IAppContextService _appContext;
        private readonly object _cpuSampleLock = new object();

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
                                User = FormatUidUser(proc.Uid),
                                ProcessName = proc.ProcessName,
                                PackageName = package,
                                Label = SafeLabelByPackage(pm, package) ?? proc.ProcessName,
                                Importance = FormatImportance(proc.Importance),
                                PssKb = pssKb,
                                IsApp = !string.IsNullOrEmpty(package) && package.Contains('.', StringComparison.Ordinal),
                                IsUserApp = proc.Uid >= 10000,
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
                                User = FormatUidUser(svc.Uid),
                                ProcessName = svc.Process ?? package,
                                PackageName = package,
                                Label = SafeLabelByPackage(pm, package) ?? package,
                                Importance = svc.Foreground ? "Foreground service" : "Service",
                                PssKb = TryGetPssKb(am, svc.Pid),
                                IsApp = !string.IsNullOrEmpty(package) && package.Contains('.', StringComparison.Ordinal),
                                IsUserApp = svc.Uid >= 10000,
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

                        string state;
                        int ppid;
                        long rssKb;
                        TryReadProcStatusExtras(dir, out state, out ppid, out rssKb);
                        bool isApp = !string.IsNullOrEmpty(package) && package.Contains('.', StringComparison.Ordinal);
                        bool isUserApp = uid >= 10000;
                        Upsert(new SystemProcessInfo
                        {
                            Pid = pid,
                            Ppid = ppid,
                            Uid = uid,
                            User = FormatUidUser(uid),
                            ProcessName = processName,
                            PackageName = package ?? processName,
                            Label = SafeLabelByPackage(pm, package) ?? processName,
                            Importance = "Background (/proc)",
                            State = state,
                            Cmdline = cmdline,
                            PssKb = TryGetPssKb(am, pid),
                            RssBytes = rssKb > 0 ? rssKb * 1024 : 0,
                            IsApp = isApp,
                            IsUserApp = isUserApp,
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

                // Enrich AppManager-style fields (cmdline / state / RSS / app flags) and CPU %.
                EnrichProcessDetails(byPid.Values, pm);
                SampleProcessCpuPercents(byPid.Values);

                var snapshot = new SystemProcessesSnapshot
                {
                    Processes = byPid.Values
                        .OrderBy(p => p.SortRank)
                        .ThenByDescending(p => p.PssKb)
                        .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    AutostartApps = QueryAutostartApps(context, pm),
                    AccessibilityApps = QueryAccessibilityApps(context, pm),
                    Notes = "Running apps list merges ActivityManager, services and /proc (AppManager-style). Kill uses kill -9 via root when available.",
                };
                return snapshot;
            });
        }

        /// <inheritdoc/>
        public Task<bool> KillProcessAsync(SystemProcessInfo process)
        {
            return Task.Run(() =>
            {
                if (process == null)
                    return false;
                try
                {
                    Context context = _appContext.Context;
                    if (!string.IsNullOrEmpty(process.PackageName)
                        && string.Equals(process.PackageName, context.PackageName, StringComparison.Ordinal))
                        return false;

                    // Prefer AppManager-style kill -9 (root). Fall back to KillBackgroundProcesses.
                    if (process.Pid > 0)
                    {
                        string killOut = RunSuCommand("kill -9 " + process.Pid.ToString(CultureInfo.InvariantCulture));
                        if (killOut != null)
                            return true;
                    }

                    if (string.IsNullOrEmpty(process.PackageName))
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

        /// <inheritdoc/>
        public Task<SystemLiveCounters> GetLiveCountersAsync()
        {
            return Task.Run(() =>
            {
                var counters = new SystemLiveCounters();
                try
                {
                    counters.CpuLoadPercent = ReadCpuLoadPercent();
                    counters.CpuTemperatureCelsius = ReadCpuTemperatureCelsius();
                    counters.FreeMemoryBytes = ReadFreeMemoryBytes();
                    counters.CpuFrequencyMhz = ReadCpuFrequencyMhz();
                }
                catch (Exception ex)
                {
                    counters.Notes = ex.Message;
                }
                return counters;
            });
        }

        /// <inheritdoc/>
        public Task<SystemActionResult> SetNetworkOutboundLoggingAsync(bool enabled)
        {
            return Task.Run(() =>
            {
                if (!TryEnsureRoot(out string rootError))
                    return SystemActionResult.Fail(rootError);

                if (enabled)
                {
                    bool iptablesLogOk = TryAddIptablesLogRule(out string iptablesDetail);
                    bool nflogOk = TryAddIptablesRule(IptablesNflogRule);
                    bool monitorOk = StartConnectionMonitor(out string monitorError);

                    if (!iptablesLogOk && !monitorOk)
                    {
                        return SystemActionResult.Fail(
                            "Could not enable logging. " +
                            (iptablesDetail ?? string.Empty) + " " +
                            (monitorError ?? string.Empty));
                    }

                    SetNetLogPref(true);
                    if (iptablesLogOk)
                        return SystemActionResult.Ok("Outbound logging enabled (iptables LOG → dmesg).");
                    if (monitorOk)
                        return SystemActionResult.Ok(
                            "Kernel has no iptables LOG target" +
                            (nflogOk ? " (NFLOG ok)" : string.Empty) +
                            ". Using ss connection monitor instead. Grant stayed; Refresh reads the log file.");
                    return SystemActionResult.Ok("Outbound logging flag set.");
                }

                StopConnectionMonitor();
                TryDeleteIptablesRule(IptablesLogRuleState);
                TryDeleteIptablesRule(IptablesLogRuleConntrack);
                TryDeleteIptablesRule(IptablesNflogRule);
                SetNetLogPref(false);
                return SystemActionResult.Ok("Outbound logging disabled.");
            });
        }

        /// <inheritdoc/>
        public Task<bool> IsNetworkOutboundLoggingEnabledAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    ISharedPreferences prefs = _appContext.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                    return prefs.GetBoolean(PrefNetLogEnabled, false);
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public Task<SystemNetworkLogEntry[]> GetNetworkOutboundLogsAsync(int maxCount = 200)
        {
            return Task.Run(() =>
            {
                if (maxCount <= 0)
                    maxCount = 200;

                var list = new List<SystemNetworkLogEntry>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                void AddLines(string output)
                {
                    if (string.IsNullOrWhiteSpace(output))
                        return;
                    foreach (string line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;
                        if (!trimmed.Contains("NET_OUT", StringComparison.Ordinal))
                            trimmed = "NET_OUT: " + trimmed;
                        if (!seen.Add(trimmed))
                            continue;
                        list.Add(ParseNetworkLogLine(trimmed));
                    }
                }

                // Prefer file written by the ss monitor (works when iptables LOG is missing).
                AddLines(RunSuCommand("tail -n " + maxCount.ToString(CultureInfo.InvariantCulture) + " " + NetOutLogFile + " 2>/dev/null"));

                // Live snapshot so Refresh always shows something while logging is enabled.
                AddLines(CaptureSsSnapshotAsNetOut());

                // Classic dmesg path when kernel LOG target works.
                string dmesg = RunSuCommand("dmesg 2>/dev/null | grep \"NET_OUT\" | tail -n " + maxCount.ToString(CultureInfo.InvariantCulture));
                if (dmesg == null)
                    dmesg = RunShellCommand("dmesg 2>/dev/null | grep \"NET_OUT\" | tail -n " + maxCount.ToString(CultureInfo.InvariantCulture));
                AddLines(dmesg);

                if (list.Count > maxCount)
                    return list.Skip(list.Count - maxCount).ToArray();
                return list.ToArray();
            });
        }

        /// <inheritdoc/>
        public Task<SystemSavedIpEntry[]> GetSavedIpsAsync()
        {
            return Task.Run(() => LoadSavedIps().ToArray());
        }

        /// <inheritdoc/>
        public Task<SystemSavedIpEntry> SaveIpFromLogAsync(SystemNetworkLogEntry entry)
        {
            return Task.Run(() =>
            {
                if (entry == null)
                    return null;
                string ip = entry.DestinationIp;
                if (string.IsNullOrWhiteSpace(ip))
                    ip = entry.SourceIp;
                if (string.IsNullOrWhiteSpace(ip))
                    return null;

                List<SystemSavedIpEntry> saved = LoadSavedIps();
                SystemSavedIpEntry existing = saved.FirstOrDefault(s =>
                    string.Equals(s.IpAddress, ip, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.RawLog ?? string.Empty, entry.Raw ?? string.Empty, StringComparison.Ordinal));
                if (existing != null)
                    return existing;

                string package = entry.ProcessName;
                if (entry.Uid.HasValue)
                {
                    try
                    {
                        string[] pkgs = _appContext.Context.PackageManager.GetPackagesForUid(entry.Uid.Value);
                        if (pkgs != null && pkgs.Length > 0)
                            package = pkgs[0];
                    }
                    catch
                    {
                        // ignored
                    }
                }

                var item = new SystemSavedIpEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    IpAddress = ip,
                    Uid = entry.Uid,
                    PackageName = package,
                    RawLog = entry.Raw,
                    SavedUtc = DateTime.UtcNow,
                    IsResolved = false,
                };
                saved.Insert(0, item);
                PersistSavedIps(saved);
                return item;
            });
        }

        /// <inheritdoc/>
        public Task RemoveSavedIpAsync(string id)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(id))
                    return;
                List<SystemSavedIpEntry> saved = LoadSavedIps();
                int removed = saved.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
                if (removed > 0)
                    PersistSavedIps(saved);
            });
        }

        /// <inheritdoc/>
        public async Task<int> ResolveSavedIpsAsync()
        {
            List<SystemSavedIpEntry> saved = await Task.Run(LoadSavedIps).ConfigureAwait(false);
            int resolved = 0;
            foreach (SystemSavedIpEntry entry in saved)
            {
                if (entry == null || entry.IsResolved || string.IsNullOrWhiteSpace(entry.IpAddress))
                    continue;
                try
                {
                    IPHostEntry host = await Dns.GetHostEntryAsync(entry.IpAddress).ConfigureAwait(false);
                    if (host != null && !string.IsNullOrWhiteSpace(host.HostName))
                    {
                        entry.Hostname = host.HostName;
                        entry.IsResolved = true;
                        resolved++;
                    }
                }
                catch
                {
                    // leave unresolved
                }
            }
            if (resolved > 0)
                await Task.Run(() => PersistSavedIps(saved)).ConfigureAwait(false);
            return resolved;
        }

        private double? ReadCpuLoadPercent()
        {
            lock (_cpuSampleLock)
            {
                // Prefer /proc/loadavg — always available as a single sample (no empty first poll).
                double? loadAvg = ReadLoadAveragePercent();
                if (loadAvg.HasValue)
                    return loadAvg;

                // Fallback: dual-sample /proc/stat (needs ~200ms).
                if (!TryReadCpuTimes(out long idle1, out long total1))
                    return null;

                try { System.Threading.Thread.Sleep(220); }
                catch { /* ignored */ }

                if (!TryReadCpuTimes(out long idle2, out long total2))
                    return null;

                long totalDelta = total2 - total1;
                long idleDelta = idle2 - idle1;
                if (totalDelta <= 0)
                    return null;

                return Math.Max(0, Math.Min(100, (1.0 - ((double)idleDelta / totalDelta)) * 100.0));
            }
        }

        private static bool TryReadCpuTimes(out long idleAll, out long total)
        {
            idleAll = 0;
            total = 0;
            string line = ReadProcFirstLine("/proc/stat");
            if (string.IsNullOrEmpty(line) || !line.StartsWith("cpu", StringComparison.Ordinal))
                return false;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return false;
            try
            {
                long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
                long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
                long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;
                idleAll = idle + iowait;
                total = user + nice + system + idle + iowait + irq + softirq + steal;
                return total > 0;
            }
            catch
            {
                return false;
            }
        }

        private static double? ReadLoadAveragePercent()
        {
            try
            {
                string text = ReadProcFirstLine("/proc/loadavg");
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return null;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double load1))
                    return null;
                int cores = Math.Max(1, Java.Lang.Runtime.GetRuntime().AvailableProcessors());
                // 1-minute load average as % of one full core-set (can exceed 100 on saturation).
                return Math.Max(0, Math.Min(100, (load1 / cores) * 100.0));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads the first line of a proc file via .NET or a short shell fallback (some OEMs
        /// restrict FileStream on /proc from managed code).
        /// </summary>
        private static string ReadProcFirstLine(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var reader = new StreamReader(path);
                    string line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        return line.Trim();
                }
            }
            catch
            {
                // fall through to shell
            }

            try
            {
                using Java.Lang.Process process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "sh", "-c", "head -n 1 " + path });
                using var reader = new StreamReader(process.InputStream);
                string line = reader.ReadLine();
                process.WaitFor();
                return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static double? ReadCpuTemperatureCelsius()
        {
            string[] candidates =
            {
                "/sys/class/thermal/thermal_zone0/temp",
                "/sys/devices/virtual/thermal/thermal_zone0/temp",
                "/sys/class/thermal/thermal_zone1/temp",
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    string text = File.ReadAllText(path).Trim();
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw))
                        continue;
                    // Most Android thermal zones report millidegrees.
                    double celsius = raw > 1000 ? raw / 1000.0 : raw;
                    if (celsius > -40 && celsius < 150)
                        return celsius;
                }
                catch
                {
                    // ignored
                }
            }

            // Prefer zones whose type looks like CPU.
            try
            {
                if (Directory.Exists("/sys/class/thermal"))
                {
                    foreach (string zoneDir in Directory.GetDirectories("/sys/class/thermal", "thermal_zone*"))
                    {
                        string typePath = Path.Combine(zoneDir, "type");
                        string tempPath = Path.Combine(zoneDir, "temp");
                        if (!File.Exists(typePath) || !File.Exists(tempPath))
                            continue;
                        string type = File.ReadAllText(typePath).Trim();
                        if (type.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0
                            && type.IndexOf("soc", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        string text = File.ReadAllText(tempPath).Trim();
                        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw))
                            continue;
                        double celsius = raw > 1000 ? raw / 1000.0 : raw;
                        if (celsius > -40 && celsius < 150)
                            return celsius;
                    }
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private long? ReadFreeMemoryBytes()
        {
            try
            {
                Context context = _appContext.Context;
                var am = context.GetSystemService(Context.ActivityService) as ActivityManager;
                if (am != null)
                {
                    var mem = new ActivityManager.MemoryInfo();
                    am.GetMemoryInfo(mem);
                    return mem.AvailMem;
                }
            }
            catch
            {
                // ignored
            }
            try
            {
                var meminfo = ParseMemInfo();
                if (meminfo.TryGetValue("MemAvailable", out long availableKb))
                    return availableKb * 1024;
                if (meminfo.TryGetValue("MemFree", out long freeKb))
                    return freeKb * 1024;
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private static double? ReadCpuFrequencyMhz()
        {
            try
            {
                string[] cpuDirs = Directory.Exists("/sys/devices/system/cpu")
                    ? Directory.GetDirectories("/sys/devices/system/cpu", "cpu*")
                    : Array.Empty<string>();
                long sumKhz = 0;
                int count = 0;
                foreach (string cpuDir in cpuDirs)
                {
                    string name = Path.GetFileName(cpuDir);
                    if (name == null || name.Length < 4 || !char.IsDigit(name[3]))
                        continue;
                    string freqPath = Path.Combine(cpuDir, "cpufreq", "scaling_cur_freq");
                    if (!File.Exists(freqPath))
                        continue;
                    string text = File.ReadAllText(freqPath).Trim();
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long khz) && khz > 0)
                    {
                        sumKhz += khz;
                        count++;
                    }
                }
                if (count == 0)
                    return null;
                return (sumKhz / (double)count) / 1000.0;
            }
            catch
            {
                return null;
            }
        }

        private static void EnrichProcessDetails(IEnumerable<SystemProcessInfo> processes, PackageManager pm)
        {
            foreach (SystemProcessInfo proc in processes)
            {
                if (proc == null || proc.Pid <= 0)
                    continue;
                string dir = "/proc/" + proc.Pid.ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(proc.Cmdline))
                    proc.Cmdline = TryReadProcCmdline(dir);
                if (string.IsNullOrEmpty(proc.State) || proc.Ppid == 0 || proc.RssBytes == 0)
                {
                    TryReadProcStatusExtras(dir, out string state, out int ppid, out long rssKb);
                    if (string.IsNullOrEmpty(proc.State))
                        proc.State = state;
                    if (proc.Ppid <= 0)
                        proc.Ppid = ppid;
                    if (proc.RssBytes <= 0 && rssKb > 0)
                        proc.RssBytes = rssKb * 1024;
                }
                if (string.IsNullOrEmpty(proc.User))
                    proc.User = FormatUidUser(proc.Uid);
                if (!proc.IsApp)
                {
                    string package = proc.PackageName;
                    proc.IsApp = !string.IsNullOrEmpty(package) && package.Contains('.', StringComparison.Ordinal)
                        && SafeIsInstalledPackage(pm, package);
                }
                if (!proc.IsUserApp)
                    proc.IsUserApp = proc.Uid >= 10000 && proc.IsApp;
            }
        }

        private static void SampleProcessCpuPercents(IEnumerable<SystemProcessInfo> processes)
        {
            var list = processes?.Where(p => p != null && p.Pid > 0).ToList();
            if (list == null || list.Count == 0)
                return;

            var first = new Dictionary<int, long>();
            long firstTotal = ReadCpuTotalJiffies();
            foreach (SystemProcessInfo proc in list)
            {
                long jiffies = TryReadProcCpuJiffies("/proc/" + proc.Pid.ToString(CultureInfo.InvariantCulture));
                if (jiffies >= 0)
                    first[proc.Pid] = jiffies;
            }

            try { System.Threading.Thread.Sleep(120); } catch { /* ignored */ }

            long secondTotal = ReadCpuTotalJiffies();
            long totalDelta = secondTotal - firstTotal;
            if (totalDelta <= 0)
                return;

            foreach (SystemProcessInfo proc in list)
            {
                if (!first.TryGetValue(proc.Pid, out long firstJiffies))
                    continue;
                long secondJiffies = TryReadProcCpuJiffies("/proc/" + proc.Pid.ToString(CultureInfo.InvariantCulture));
                if (secondJiffies < 0)
                    continue;
                long delta = secondJiffies - firstJiffies;
                if (delta < 0)
                    continue;
                proc.CpuPercent = Math.Max(0, Math.Min(100, (delta * 100.0) / totalDelta));
            }
        }

        private static long ReadCpuTotalJiffies()
        {
            try
            {
                if (!File.Exists("/proc/stat"))
                    return 0;
                string line = File.ReadLines("/proc/stat").FirstOrDefault();
                if (string.IsNullOrEmpty(line))
                    return 0;
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                long total = 0;
                for (int i = 1; i < parts.Length && i <= 8; i++)
                {
                    if (long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                        total += v;
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        private static long TryReadProcCpuJiffies(string procDir)
        {
            try
            {
                string path = Path.Combine(procDir, "stat");
                if (!File.Exists(path))
                    return -1;
                string text = File.ReadAllText(path);
                int close = text.LastIndexOf(')');
                if (close < 0 || close + 2 >= text.Length)
                    return -1;
                string[] parts = text.Substring(close + 2).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                // fields 12,13 after comm are utime,stime (0-based index 11,12 in full; after ') ' they are index 11-2? )
                // After ") " the fields start at state(0), ppid(1), ... utime is field 14 overall = index 11 after state.
                if (parts.Length < 15)
                    return -1;
                long utime = long.Parse(parts[11], CultureInfo.InvariantCulture);
                long stime = long.Parse(parts[12], CultureInfo.InvariantCulture);
                return utime + stime;
            }
            catch
            {
                return -1;
            }
        }

        private static void TryReadProcStatusExtras(string procDir, out string state, out int ppid, out long rssKb)
        {
            state = null;
            ppid = 0;
            rssKb = 0;
            try
            {
                string path = Path.Combine(procDir, "status");
                if (!File.Exists(path))
                    return;
                foreach (string line in File.ReadLines(path))
                {
                    if (line.StartsWith("State:", StringComparison.Ordinal))
                        state = line.Substring(6).Trim();
                    else if (line.StartsWith("PPid:", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ppid);
                    }
                    else if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rssKb);
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        private static string FormatUidUser(int uid)
        {
            if (uid <= 0)
                return null;
            if (uid == 0)
                return "root";
            if (uid == 1000)
                return "system";
            if (uid >= 10000)
                return "u0_a" + (uid - 10000).ToString(CultureInfo.InvariantCulture);
            return "uid:" + uid.ToString(CultureInfo.InvariantCulture);
        }

        private static bool SafeIsInstalledPackage(PackageManager pm, string packageName)
        {
            if (pm == null || string.IsNullOrEmpty(packageName))
                return false;
            try
            {
                pm.GetApplicationInfo(packageName, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static SystemNetworkLogEntry ParseNetworkLogLine(string line)
        {
            var entry = new SystemNetworkLogEntry { Raw = line };
            entry.SourceIp = MatchToken(line, "SRC=");
            entry.DestinationIp = MatchToken(line, "DST=");
            entry.Interface = MatchToken(line, "OUT=");
            if (string.IsNullOrEmpty(entry.Interface))
                entry.Interface = MatchToken(line, "IN=");
            entry.ProcessName = MatchToken(line, "PROC=");
            string uidText = MatchToken(line, "UID=");
            if (int.TryParse(uidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid))
                entry.Uid = uid;

            // ss-monitor lines may look like: NET_OUT: 10.0.0.1:443 users:(("chrome",pid=1,fd=2))
            if (string.IsNullOrEmpty(entry.DestinationIp))
            {
                foreach (string token in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = token.LastIndexOf(':');
                    if (colon <= 0 || colon + 1 >= token.Length)
                        continue;
                    string host = token.Substring(0, colon).Trim('[', ']');
                    if (host == "0.0.0.0" || host == "*" || host == "::" || host.StartsWith("127.", StringComparison.Ordinal))
                        continue;
                    if (System.Net.IPAddress.TryParse(host, out _))
                    {
                        entry.DestinationIp = host;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(entry.ProcessName))
            {
                int users = line.IndexOf("users:((\"", StringComparison.Ordinal);
                if (users >= 0)
                {
                    int start = users + "users:((\"".Length;
                    int end = line.IndexOf('"', start);
                    if (end > start)
                        entry.ProcessName = line.Substring(start, end - start);
                }
            }
            return entry;
        }

        private static string MatchToken(string line, string key)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(key))
                return null;
            int idx = line.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return null;
            int start = idx + key.Length;
            int end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
                end++;
            if (end <= start)
                return null;
            return line.Substring(start, end - start);
        }

        private List<SystemSavedIpEntry> LoadSavedIps()
        {
            try
            {
                ISharedPreferences prefs = _appContext.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                string json = prefs.GetString(PrefSavedIpsJson, null);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<SystemSavedIpEntry>();
                var list = System.Text.Json.JsonSerializer.Deserialize<List<SystemSavedIpEntry>>(json);
                return list ?? new List<SystemSavedIpEntry>();
            }
            catch
            {
                return new List<SystemSavedIpEntry>();
            }
        }

        private void PersistSavedIps(List<SystemSavedIpEntry> entries)
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(entries ?? new List<SystemSavedIpEntry>());
                ISharedPreferences prefs = _appContext.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                prefs.Edit().PutString(PrefSavedIpsJson, json).Apply();
            }
            catch
            {
                // ignored
            }
        }

        private void SetNetLogPref(bool enabled)
        {
            try
            {
                ISharedPreferences prefs = _appContext.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                prefs.Edit().PutBoolean(PrefNetLogEnabled, enabled).Apply();
            }
            catch
            {
                // ignored
            }
        }

        private static bool TryEnsureRoot(out string error)
        {
            error = null;
            var result = RunSuDetailed("id");
            if (result.ExitCode == 0 && result.Stdout != null &&
                result.Stdout.IndexOf("uid=0", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            string detail = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            if (string.IsNullOrWhiteSpace(detail))
                detail = "exit " + result.ExitCode;
            global::Android.Util.Log.Warn(LogTag, "su failed: " + detail);
            error = "Root denied for aNote. Open KernelSU/Magisk, grant root to app.anote, then tap Enable again. (" + detail.Trim() + ")";
            return false;
        }

        private static bool TryAddIptablesLogRule(out string detail)
        {
            detail = null;
            if (TryAddIptablesRule(IptablesLogRuleConntrack))
                return true;
            if (TryAddIptablesRule(IptablesLogRuleState))
                return true;

            var probe = RunSuDetailed("iptables -A " + IptablesLogRuleConntrack + " 2>&1");
            detail = string.IsNullOrWhiteSpace(probe.Stdout) ? probe.Stderr : probe.Stdout;
            if (string.IsNullOrWhiteSpace(detail))
                detail = "iptables LOG target unavailable";
            global::Android.Util.Log.Warn(LogTag, "iptables LOG failed: " + detail);
            TryDeleteIptablesRule(IptablesLogRuleConntrack);
            TryDeleteIptablesRule(IptablesLogRuleState);
            return false;
        }

        private static bool TryAddIptablesRule(string rule)
        {
            // -C succeeds if present; otherwise -A. Both must be attempted without aborting the shell early wrongly.
            var result = RunSuDetailed(
                "iptables -C " + rule + " >/dev/null 2>&1 || iptables -A " + rule + " 2>&1");
            return result.ExitCode == 0;
        }

        private static void TryDeleteIptablesRule(string rule)
        {
            RunSuDetailed("iptables -D " + rule + " >/dev/null 2>&1 || true");
        }

        private static bool StartConnectionMonitor(out string error)
        {
            error = null;
            StopConnectionMonitor();

            // Ash script: append unique peer addresses from ss every 2s as NET_OUT lines.
            string script =
                "#!/system/bin/sh\n" +
                "LOG=" + NetOutLogFile + "\n" +
                "SEEN=/data/local/tmp/anote_net_out.seen\n" +
                ": > \"$LOG\"\n" +
                ": > \"$SEEN\"\n" +
                "chmod 666 \"$LOG\" 2>/dev/null\n" +
                "while true; do\n" +
                "  ss -H -tunap 2>/dev/null | while IFS= read -r line; do\n" +
                "    case \"$line\" in\n" +
                "      *ESTAB*|*SYN-SENT*|*SYN-RECV*) ;;\n" +
                "      *) continue ;;\n" +
                "    esac\n" +
                "    case \"$line\" in\n" +
                "      *127.0.0.1*|*\\[::1\\]*) continue ;;\n" +
                "    esac\n" +
                "    key=$(printf '%s' \"$line\" | tr -s ' ')\n" +
                "    grep -Fqx \"$key\" \"$SEEN\" 2>/dev/null && continue\n" +
                "    printf '%s\\n' \"$key\" >> \"$SEEN\"\n" +
                "    printf 'NET_OUT: %s\\n' \"$key\" >> \"$LOG\"\n" +
                "  done\n" +
                "  tail -n 2000 \"$SEEN\" > \"$SEEN.tmp\" 2>/dev/null && mv \"$SEEN.tmp\" \"$SEEN\"\n" +
                "  tail -n 2000 \"$LOG\" > \"$LOG.tmp\" 2>/dev/null && mv \"$LOG.tmp\" \"$LOG\"\n" +
                "  sleep 2\n" +
                "done\n";

            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
            var write = RunSuDetailed(
                "echo " + b64 + " | base64 -d > " + NetOutScriptFile +
                " && chmod 755 " + NetOutScriptFile +
                " && (nohup " + NetOutScriptFile + " >/dev/null 2>&1 & echo $! > " + NetOutPidFile + ")");
            if (write.ExitCode != 0)
            {
                error = "Failed to start ss monitor: " + (write.Stderr ?? write.Stdout ?? ("exit " + write.ExitCode));
                global::Android.Util.Log.Warn(LogTag, error);
                return false;
            }

            // Confirm pid file exists / process started.
            var pidCheck = RunSuDetailed("test -s " + NetOutPidFile + " && kill -0 $(cat " + NetOutPidFile + ") 2>/dev/null");
            if (pidCheck.ExitCode != 0)
            {
                // nohup timing — still treat as ok if script was written; Refresh can live-snapshot.
                global::Android.Util.Log.Warn(LogTag, "monitor pid not confirmed yet");
            }
            return true;
        }

        private static void StopConnectionMonitor()
        {
            RunSuDetailed(
                "if [ -f " + NetOutPidFile + " ]; then kill $(cat " + NetOutPidFile + ") 2>/dev/null; fi; " +
                "pkill -f " + NetOutScriptFile + " 2>/dev/null; " +
                "rm -f " + NetOutPidFile + " 2>/dev/null; true");
        }

        private static string CaptureSsSnapshotAsNetOut()
        {
            string output = RunSuCommand("ss -H -tunap 2>/dev/null | grep -E 'ESTAB|SYN-SENT|SYN-RECV'");
            if (string.IsNullOrWhiteSpace(output))
                return null;
            var sb = new System.Text.StringBuilder();
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                if (trimmed.Contains("127.0.0.1", StringComparison.Ordinal) || trimmed.Contains("[::1]", StringComparison.Ordinal))
                    continue;
                sb.Append("NET_OUT: ").Append(trimmed).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Runs a shell command via <c>su -c</c>. Returns stdout on success (exit 0), otherwise null.
        /// </summary>
        private static string RunSuCommand(string command)
        {
            var result = RunSuDetailed(command);
            return result.ExitCode == 0 ? (result.Stdout ?? string.Empty) : null;
        }

        private static (int ExitCode, string Stdout, string Stderr) RunSuDetailed(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return (-1, null, "empty command");
            try
            {
                using Java.Lang.Process process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "su", "-c", command });
                string stdout;
                string stderr;
                using (var reader = new StreamReader(process.InputStream))
                    stdout = reader.ReadToEnd();
                using (var reader = new StreamReader(process.ErrorStream))
                    stderr = reader.ReadToEnd();
                process.WaitFor();
                return (process.ExitValue(), stdout, stderr);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, "su exec failed: " + ex);
                return (-1, null, ex.Message);
            }
        }

        private static string RunShellCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;
            try
            {
                using Java.Lang.Process process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "sh", "-c", command });
                string stdout;
                using (var reader = new StreamReader(process.InputStream))
                    stdout = reader.ReadToEnd();
                process.WaitFor();
                return stdout ?? string.Empty;
            }
            catch
            {
                return null;
            }
        }
    }
}
