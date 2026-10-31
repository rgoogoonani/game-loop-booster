using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Tweaks;

public sealed record TweakResult(string TweakId, string Title, bool Success, string Message);

public sealed record ApplySummary(IReadOnlyList<TweakResult> Results, bool RebootRequired)
{
    public int Succeeded => Results.Count(r => r.Success);
    public int Failed => Results.Count(r => !r.Success);
}

/// <summary>Applies tweaks (backing up first) and restores them from the backup.</summary>
public sealed class TweakEngine
{
    public BackupStore Backup { get; }

    public TweakEngine(BackupStore backup) => Backup = backup;

    public ApplySummary Apply(IEnumerable<(Tweak Tweak, string Target)> items)
    {
        var results = new List<TweakResult>();
        var reboot = false;
        // The plan switch must run first so that processor settings land on the new plan.
        foreach (var (tweak, target) in items.OrderBy(i => i.Tweak is PowerPlanTweak ? 0 : 1))
        {
            try
            {
                if (tweak.IsAt(target))
                {
                    results.Add(new TweakResult(tweak.Id, tweak.Title, true, "از قبل اعمال شده"));
                    continue;
                }
                Backup.EnsureCaptured(tweak);
                tweak.Apply(target);
                reboot |= tweak.RequiresReboot;
                results.Add(new TweakResult(tweak.Id, tweak.Title, true, $"→ {tweak.Display(target)}"));
                Log.Success($"اعمال شد: {tweak.Title} = {target}");
            }
            catch (Exception ex)
            {
                results.Add(new TweakResult(tweak.Id, tweak.Title, false, ex.Message));
                Log.Error($"خطا در {tweak.Title}: {ex.Message}");
            }
        }
        return new ApplySummary(results, reboot);
    }

    public TweakResult Restore(string tweakId)
    {
        var entry = Backup.Get(tweakId);
        if (entry is null) return new TweakResult(tweakId, tweakId, true, "تغییری داده نشده بود");
        try
        {
            BackupStore.RestoreEntry(entry);
            Backup.Remove(tweakId);
            Log.Success($"برگردانده شد: {entry.Title}");
            return new TweakResult(tweakId, entry.Title, true, "به حالت اولیه برگشت");
        }
        catch (Exception ex)
        {
            Log.Error($"بازگردانی {entry.Title} ناموفق: {ex.Message}");
            return new TweakResult(tweakId, entry.Title, false, ex.Message);
        }
    }

    /// <summary>Restores everything in reverse order of capture (power settings before the plan switch back).</summary>
    public ApplySummary RestoreAll()
    {
        var results = Backup.Entries
            .OrderByDescending(e => e.CapturedAt)
            .ThenBy(e => e.Kind == BackupKind.PowerPlan ? 1 : 0)
            .Select(e => Restore(e.TweakId))
            .ToList();
        return new ApplySummary(results, results.Count > 0);
    }
}
