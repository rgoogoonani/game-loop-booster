namespace GameLoopBooster.Core.Hardware;

public enum CpuVendor { Unknown, Intel, Amd }
public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

public enum CpuLayout
{
    /// <summary>All cores identical, single cache domain.</summary>
    Uniform,
    /// <summary>Intel 12th gen+ with Performance and Efficient cores.</summary>
    IntelHybrid,
    /// <summary>AMD with more than one L3 domain (e.g. 7950X, 7950X3D).</summary>
    AmdMultiCcd,
}

public sealed class CoreInfo
{
    public int Index { get; init; }
    public byte EfficiencyClass { get; init; }
    public bool IsPerformance { get; set; }
    public bool HasSmt { get; init; }
    public ulong Mask { get; init; }
    public int L3Domain { get; set; }
    public bool InPreferredCcd { get; set; }
    public IReadOnlyList<int> LogicalProcessors => Bits(Mask);

    internal static List<int> Bits(ulong mask)
    {
        var list = new List<int>();
        for (var i = 0; i < 64; i++)
            if ((mask & (1UL << i)) != 0) list.Add(i);
        return list;
    }
}

public sealed class CacheDomain
{
    public int Index { get; init; }
    public ulong Mask { get; init; }
    public long SizeBytes { get; init; }
    public int SizeMb => (int)(SizeBytes / (1024 * 1024));
}

public sealed class GpuInfo
{
    public string Name { get; init; } = "";
    public GpuVendor Vendor { get; init; }
    public string DriverVersion { get; init; } = "";
    public bool IsIntegrated { get; init; }
}

public sealed class HardwareInfo
{
    public string CpuName { get; init; } = "Unknown CPU";
    public CpuVendor CpuVendor { get; init; }
    public CpuLayout Layout { get; init; }
    public int LogicalProcessors { get; init; }
    public IReadOnlyList<CoreInfo> Cores { get; init; } = Array.Empty<CoreInfo>();
    public IReadOnlyList<CacheDomain> L3Domains { get; init; } = Array.Empty<CacheDomain>();
    public bool IsX3D { get; init; }

    public ulong RamBytes { get; init; }
    public int RamSpeedMts { get; init; }
    public int RamRatedSpeedMts { get; init; }
    public int RamModules { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();
    public GpuInfo? PrimaryGpu => Gpus.FirstOrDefault(g => !g.IsIntegrated) ?? Gpus.FirstOrDefault();
    public bool HasIntegratedAndDedicated => Gpus.Any(g => g.IsIntegrated) && Gpus.Any(g => !g.IsIntegrated);

    public string WindowsName { get; init; } = "";
    public int WindowsBuild { get; init; }

    public int PerformanceCoreCount => Cores.Count(c => c.IsPerformance);
    public int EfficientCoreCount => Cores.Count(c => !c.IsPerformance);
    public double RamGb => Math.Round(RamBytes / 1024d / 1024d / 1024d);

    /// <summary>Logical processors of the cores the game should run on (P-cores, or the X3D / first CCD).</summary>
    public ulong PreferredMask => Layout switch
    {
        CpuLayout.IntelHybrid => Cores.Where(c => c.IsPerformance).Aggregate(0UL, (m, c) => m | c.Mask),
        CpuLayout.AmdMultiCcd => Cores.Where(c => c.InPreferredCcd).Aggregate(0UL, (m, c) => m | c.Mask),
        _ => AllMask,
    };

    /// <summary>Same as <see cref="PreferredMask"/> but one logical processor per physical core (no SMT sibling).</summary>
    public ulong PreferredMaskNoSmt => Cores
        .Where(c => (PreferredMask & c.Mask) != 0)
        .Aggregate(0UL, (m, c) => m | LowestBit(c.Mask));

    /// <summary>Where background helpers go: E-cores on hybrid, the non-preferred CCD on dual-CCD AMD.</summary>
    public ulong SecondaryMask
    {
        get
        {
            var rest = AllMask & ~PreferredMask;
            return rest == 0 ? AllMask : rest;
        }
    }

    public ulong AllMask => LogicalProcessors >= 64 ? ulong.MaxValue : (1UL << LogicalProcessors) - 1;

    public string TopologySummary => Layout switch
    {
        CpuLayout.IntelHybrid => $"{PerformanceCoreCount}P + {EfficientCoreCount}E · {LogicalProcessors} Threads",
        CpuLayout.AmdMultiCcd => $"{L3Domains.Count} CCD · {Cores.Count} Cores · {LogicalProcessors} Threads" + (IsX3D ? " · 3D V-Cache" : ""),
        _ => $"{Cores.Count} Cores · {LogicalProcessors} Threads" + (IsX3D ? " · 3D V-Cache" : ""),
    };

    public string ProfileName => Layout switch
    {
        CpuLayout.IntelHybrid => "Intel Hybrid (P-Core Focus)",
        CpuLayout.AmdMultiCcd when IsX3D => "AMD X3D (V-Cache CCD Focus)",
        CpuLayout.AmdMultiCcd => "AMD Multi-CCD (Single CCD Focus)",
        _ when IsX3D => "AMD X3D",
        _ => "Uniform CPU",
    };

    private static ulong LowestBit(ulong mask) => mask & (~mask + 1);
}
