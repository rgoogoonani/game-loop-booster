using CommunityToolkit.Mvvm.ComponentModel;
using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Hardware;
using GameLoopBooster.Core.Tweaks;

namespace GameLoopBooster.App.ViewModels;

public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly Action<TweakItemViewModel> _changed;
    private bool _loading = true;

    public TweakItemViewModel(Tweak tweak, HardwareInfo hw, Action<TweakItemViewModel> changed)
    {
        Tweak = tweak;
        Level = tweak.Recommend(hw);
        _changed = changed;
        targetValue = tweak.RecommendedValue;
    }

    public Tweak Tweak { get; }
    public TweakLevel? Level { get; }

    public string Id => Tweak.Id;
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string Location => Tweak.Location;
    public string ValueHint => Tweak.ValueHint;
    public bool RequiresReboot => Tweak.RequiresReboot;
    public string Recommended => Tweak.Display(Tweak.RecommendedValue);

    public string CategoryName => Tweak.Category switch
    {
        TweakCategory.Scheduler => "زمان‌بندی CPU",
        TweakCategory.Power => "پاورپلن و پردازنده",
        TweakCategory.Timer => "تایمر سیستم",
        TweakCategory.Gpu => "گرافیک",
        TweakCategory.Memory => "حافظه",
        TweakCategory.GameBar => "Game Bar و Game Mode",
        TweakCategory.Network => "شبکه",
        TweakCategory.Emulator => "تنظیمات ویندوز برای فایل‌های GameLoop",
        _ => Tweak.Category.ToString(),
    };

    public string LevelText => Level switch
    {
        TweakLevel.Safe => "پایه",
        TweakLevel.Aggressive => "حداکثر",
        TweakLevel.Expert => "متخصص",
        _ => "برای این سیستم لازم نیست",
    };

    public string LevelKey => Level?.ToString() ?? "None";

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string targetValue;
    [ObservableProperty] private string current = "…";
    [ObservableProperty] private string original = "—";
    [ObservableProperty] private bool isApplied;
    [ObservableProperty] private bool hasBackup;
    [ObservableProperty] private string? error;

    public bool IsCustomValue => !string.Equals(TargetValue, Tweak.RecommendedValue, StringComparison.Ordinal);

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_loading) _changed(this);
    }

    partial void OnTargetValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomValue));
        if (!_loading) _changed(this);
    }

    /// <summary>Sets the initial state without reporting it as a user change.</summary>
    public void Load(bool selected, string? value)
    {
        _loading = true;
        IsSelected = selected;
        if (!string.IsNullOrWhiteSpace(value)) TargetValue = value;
        _loading = false;
    }

    /// <summary>Reads live state (slow: may spawn powercfg). Call off the UI thread, apply the result on it.</summary>
    public Func<TweakItemViewModel, bool> ReadState(BackupEntry? backup)
    {
        string current;
        bool applied;
        string? error = null;
        try
        {
            var raw = Tweak.ReadCurrent();
            current = Tweak.Display(raw);
            applied = Tweak.Matches(raw, TargetValue);
        }
        catch (Exception ex)
        {
            current = "خطا در خواندن";
            applied = false;
            error = ex.Message;
        }
        var original = backup is null ? "—" : backup.Existed ? DisplayBackup(backup) : "تعریف نشده";
        return vm =>
        {
            vm.Current = current;
            vm.IsApplied = applied;
            vm.Error = error;
            vm.HasBackup = backup is not null;
            vm.Original = original;
            return applied;
        };
    }

    private string DisplayBackup(BackupEntry e)
    {
        try { return Tweak.Display(e.Value); }
        catch { return e.Value ?? "—"; }
    }
}
