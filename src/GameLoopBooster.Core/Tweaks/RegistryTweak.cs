using System.Globalization;
using GameLoopBooster.Core.Backup;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Tweaks;

public class RegistryTweak : Tweak
{
    public RegistryHive Hive { get; init; } = RegistryHive.LocalMachine;
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;

    public override string Location => $@"{HiveName(Hive)}\{KeyPath}  →  {ValueName}";

    public override string? ReadCurrent()
    {
        using var key = RegistryIO.Open(Hive, KeyPath, writable: false);
        var raw = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return null;
        return RegistryIO.Serialize(raw, key!.GetValueKind(ValueName));
    }

    public override BackupEntry Capture()
    {
        using var key = RegistryIO.Open(Hive, KeyPath, writable: false);
        var raw = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = raw is null ? Kind : key!.GetValueKind(ValueName);
        return new BackupEntry
        {
            TweakId = Id,
            Title = Title,
            Kind = BackupKind.Registry,
            Existed = raw is not null,
            Value = raw is null ? null : RegistryIO.Serialize(raw, kind),
            Hive = Hive.ToString(),
            KeyPath = KeyPath,
            ValueName = ValueName,
            ValueKind = kind.ToString(),
        };
    }

    public override void Apply(string target)
    {
        using var key = RegistryIO.Create(Hive, KeyPath);
        key.SetValue(ValueName, RegistryIO.Deserialize(target, Kind), Kind);
    }

    public override string? Normalize(string? value)
    {
        if (value is null) return null;
        return Kind is RegistryValueKind.DWord or RegistryValueKind.QWord && RegistryIO.TryParseNumber(value, out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : value.Trim();
    }

    public override string Display(string? value)
    {
        if (value is null) return base.Display(value);
        if (Kind == RegistryValueKind.DWord && RegistryIO.TryParseNumber(value, out var n))
            return $"{n} (0x{n:X})";
        return value;
    }

    public static string HiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        _ => hive.ToString(),
    };
}

/// <summary>Appends a compatibility flag to an AppCompatFlags\Layers value instead of overwriting existing flags.</summary>
public sealed class CompatLayerTweak : RegistryTweak
{
    public override void Apply(string target)
    {
        var current = ReadCurrent() ?? "~";
        var flags = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~").ToList();
        foreach (var f in target.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~"))
            if (!flags.Contains(f, StringComparer.OrdinalIgnoreCase)) flags.Add(f);
        base.Apply("~ " + string.Join(' ', flags));
    }

    /// <summary>Applied when every target flag is present, whatever other flags the value holds.</summary>
    public override bool Matches(string? current, string target)
    {
        if (current is null) return false;
        var have = (Normalize(current) ?? "").Split(' ');
        return (Normalize(target) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(f => have.Contains(f));
    }

    public override string? Normalize(string? value)
    {
        if (value is null) return null;
        var flags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~").Select(f => f.ToUpperInvariant()).OrderBy(f => f);
        return string.Join(' ', flags);
    }
}

public static class RegistryIO
{
    public static RegistryKey BaseKey(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    public static RegistryKey? Open(RegistryHive hive, string path, bool writable)
    {
        using var root = BaseKey(hive);
        return root.OpenSubKey(path, writable);
    }

    public static RegistryKey Create(RegistryHive hive, string path)
    {
        using var root = BaseKey(hive);
        return root.CreateSubKey(path, writable: true) ?? throw new InvalidOperationException($"Cannot open {path}");
    }

    public static bool TryParseNumber(string text, out ulong value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed) && signed < 0)
        {
            value = unchecked((uint)(int)signed);
            return signed >= int.MinValue;
        }
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static string Serialize(object raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((uint)Convert.ToInt32(raw)).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => unchecked((ulong)Convert.ToInt64(raw)).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])raw),
        RegistryValueKind.MultiString => string.Join("\n", (string[])raw),
        _ => raw.ToString() ?? "",
    };

    public static object Deserialize(string text, RegistryValueKind kind)
    {
        switch (kind)
        {
            case RegistryValueKind.DWord:
                if (!TryParseNumber(text, out var d) || d > uint.MaxValue)
                    throw new FormatException($"'{text}' is not a valid DWORD (0..4294967295 or 0x hex)");
                return unchecked((int)(uint)d);
            case RegistryValueKind.QWord:
                if (!TryParseNumber(text, out var q)) throw new FormatException($"'{text}' is not a valid QWORD");
                return unchecked((long)q);
            case RegistryValueKind.Binary:
                return Convert.FromBase64String(text);
            case RegistryValueKind.MultiString:
                return text.Split('\n');
            default:
                return text;
        }
    }

    /// <summary>Puts a captured registry value back, or deletes it if it did not exist originally.</summary>
    public static void Restore(BackupEntry entry)
    {
        var hive = Enum.Parse<RegistryHive>(entry.Hive!);
        if (!entry.Existed)
        {
            using var key = Open(hive, entry.KeyPath!, writable: true);
            key?.DeleteValue(entry.ValueName!, throwOnMissingValue: false);
            return;
        }
        var kind = Enum.Parse<RegistryValueKind>(entry.ValueKind!);
        using var k = Create(hive, entry.KeyPath!);
        k.SetValue(entry.ValueName!, Deserialize(entry.Value ?? "", kind), kind);
    }
}
