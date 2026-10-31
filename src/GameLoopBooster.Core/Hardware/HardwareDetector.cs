using System.Management;
using System.Runtime.InteropServices;
using GameLoopBooster.Core.Native;
using GameLoopBooster.Core.Util;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Hardware;

public static class HardwareDetector
{
    public static HardwareInfo Detect()
    {
        var cpuName = ReadCpuName();
        var vendor = cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Amd
            : cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Intel
            : CpuVendor.Unknown;

        var cores = SafeList(ReadCores);
        var l3 = SafeList(ReadL3Domains);

        for (var i = 0; i < l3.Count; i++)
            foreach (var core in cores.Where(c => (c.Mask & l3[i].Mask) != 0))
                core.L3Domain = i;

        var distinctClasses = cores.Select(c => c.EfficiencyClass).Distinct().Count();
        var maxClass = cores.Count > 0 ? cores.Max(c => c.EfficiencyClass) : (byte)0;
        foreach (var c in cores) c.IsPerformance = c.EfficiencyClass == maxClass;

        var layout = distinctClasses > 1 ? CpuLayout.IntelHybrid
            : l3.Count > 1 ? CpuLayout.AmdMultiCcd
            : CpuLayout.Uniform;

        // X3D: the V-Cache CCD exposes a much larger L3 (96 MB+) than a regular CCD (32 MB).
        var isX3D = cpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase) || l3.Any(d => d.SizeMb >= 64);
        if (layout == CpuLayout.AmdMultiCcd && l3.Count > 0)
        {
            var preferred = l3.OrderByDescending(d => d.SizeBytes).ThenBy(d => d.Index).First();
            var preferredIdx = l3.IndexOf(preferred);
            foreach (var c in cores) c.InPreferredCcd = c.L3Domain == preferredIdx;
        }

        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref mem);
        var (speed, rated, modules) = ReadRam();

        var (winName, build) = ReadWindows();

        return new HardwareInfo
        {
            CpuName = cpuName,
            CpuVendor = vendor,
            Layout = layout,
            LogicalProcessors = Environment.ProcessorCount,
            Cores = cores,
            L3Domains = l3,
            IsX3D = vendor == CpuVendor.Amd && isX3D,
            RamBytes = mem.ullTotalPhys,
            RamSpeedMts = speed,
            RamRatedSpeedMts = rated,
            RamModules = modules,
            Gpus = SafeList(ReadGpus),
            WindowsName = winName,
            WindowsBuild = build,
        };
    }

    private static List<T> SafeList<T>(Func<List<T>> read)
    {
        try { return read(); }
        catch (Exception ex)
        {
            Log.Warn($"Hardware probe failed: {ex.Message}");
            return new List<T>();
        }
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown CPU";
    }

    private static void EnumerateRecords(int relation, Action<IntPtr, int> visit)
    {
        uint len = 0;
        NativeMethods.GetLogicalProcessorInformationEx(relation, IntPtr.Zero, ref len);
        var buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(relation, buffer, ref len))
                throw new InvalidOperationException($"GetLogicalProcessorInformationEx failed ({Marshal.GetLastWin32Error()})");
            var offset = 0;
            while (offset < len)
            {
                var size = Marshal.ReadInt32(buffer, offset + 4);
                visit(buffer, offset);
                offset += size;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX / PROCESSOR_RELATIONSHIP layout:
    // +0 Relationship, +4 Size, +8 Flags, +9 EfficiencyClass, +30 GroupCount, +32 GROUP_AFFINITY.Mask
    private static List<CoreInfo> ReadCores()
    {
        var cores = new List<CoreInfo>();
        EnumerateRecords(NativeMethods.RelationProcessorCore, (buf, off) =>
        {
            if (Marshal.ReadInt32(buf, off) != NativeMethods.RelationProcessorCore) return;
            var group = Marshal.ReadInt16(buf, off + 40);
            if (group != 0) return; // processor groups > 0 only exist on >64 thread machines
            cores.Add(new CoreInfo
            {
                Index = cores.Count,
                HasSmt = (Marshal.ReadByte(buf, off + 8) & NativeMethods.LTP_PC_SMT) != 0,
                EfficiencyClass = Marshal.ReadByte(buf, off + 9),
                Mask = (ulong)Marshal.ReadInt64(buf, off + 32),
            });
        });
        return cores;
    }

    // CACHE_RELATIONSHIP: +8 Level, +12 CacheSize, +16 Type, +38 GroupCount, +40 GROUP_AFFINITY.Mask
    private static List<CacheDomain> ReadL3Domains()
    {
        var domains = new List<CacheDomain>();
        EnumerateRecords(NativeMethods.RelationCache, (buf, off) =>
        {
            if (Marshal.ReadInt32(buf, off) != NativeMethods.RelationCache) return;
            if (Marshal.ReadByte(buf, off + 8) != 3) return;
            domains.Add(new CacheDomain
            {
                Index = domains.Count,
                SizeBytes = (uint)Marshal.ReadInt32(buf, off + 12),
                Mask = (ulong)Marshal.ReadInt64(buf, off + 40),
            });
        });
        return domains;
    }

    private static (int speed, int rated, int modules) ReadRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            int speed = 0, rated = 0, modules = 0;
            foreach (var mo in searcher.Get())
            {
                modules++;
                rated = Math.Max(rated, Convert.ToInt32(mo["Speed"] ?? 0));
                speed = Math.Max(speed, Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? 0));
            }
            return (speed == 0 ? rated : speed, rated, modules);
        }
        catch (Exception ex)
        {
            Log.Warn($"RAM query failed: {ex.Message}");
            return (0, 0, 0);
        }
    }

    private static List<GpuInfo> ReadGpus()
    {
        var gpus = new List<GpuInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController");
        foreach (var mo in searcher.Get())
        {
            var name = mo["Name"]?.ToString() ?? "";
            var compat = mo["AdapterCompatibility"]?.ToString() ?? "";
            var text = name + " " + compat;
            if (text.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Parsec", StringComparison.OrdinalIgnoreCase)) continue;

            var vendor = text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                : text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || text.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                : text.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
                : GpuVendor.Unknown;

            var integrated = vendor == GpuVendor.Intel && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                || (vendor == GpuVendor.Amd && name.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase));

            gpus.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                DriverVersion = mo["DriverVersion"]?.ToString() ?? "",
                IsIntegrated = integrated,
            });
        }
        return gpus;
    }

    private static (string name, int build) ReadWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = int.TryParse(key?.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
        var display = key?.GetValue("DisplayVersion") as string ?? "";
        var product = build >= 22000 ? "Windows 11" : "Windows 10";
        return ($"{product} {display} (Build {build})".Replace("  ", " "), build);
    }
}
