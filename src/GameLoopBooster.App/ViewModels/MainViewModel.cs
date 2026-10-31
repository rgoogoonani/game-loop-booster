using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Diagnostics;
using GameLoopBooster.Core.Hardware;
using GameLoopBooster.Core.Processes;
using GameLoopBooster.Core.Settings;
using GameLoopBooster.Core.Tweaks;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.App.ViewModels;

public sealed record CoreChip(string Label, string Threads, bool Preferred, string Kind);

public sealed record BackupRow(string Title, string Original, string Captured, string Kind);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly BackupStore _backup = new();
    private readonly TweakEngine _engine;
    private ProcessOptimizer? _optimizer;
    private CancellationTokenSource? _jitterCts;
    private readonly List<double> _jitterBuffer = new();

    public MainViewModel()
    {
        _engine = new TweakEngine(_backup);
        TweaksView = CollectionViewSource.GetDefaultView(Tweaks);
        TweaksView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakItemViewModel.CategoryName)));
        TweaksView.Filter = o => o is TweakItemViewModel t && (AdvancedMode || t.Level is TweakLevel.Safe or TweakLevel.Aggressive);

        Rules = new ObservableCollection<ProcessRule>(_settings.ProcessRules);
        profileLevel = _settings.ProfileLevel;
        advancedMode = _settings.AdvancedMode;
        autoOptimize = _settings.AutoOptimizeProcesses;
        highResTimer = _settings.HighResolutionTimer;
        sessionPowerPlan = _settings.SessionPowerPlan;
        startMinimized = _settings.StartMinimized;
        minimizeToTray = _settings.MinimizeToTray;

        foreach (var e in Log.Snapshot()) Logs.Insert(0, e);
        Log.Written += e => Ui(() =>
        {
            Logs.Insert(0, e);
            if (Logs.Count > 400) Logs.RemoveAt(Logs.Count - 1);
        });
        _backup.Changed += () => Ui(RefreshBackupRows);
    }

    // ── Collections ──────────────────────────────────────────
    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = new();
    public ICollectionView TweaksView { get; }
    public ObservableCollection<ProcessRule> Rules { get; }
    public ObservableCollection<ManagedProcess> LiveProcesses { get; } = new();
    public ObservableCollection<CoreChip> CoreChips { get; } = new();
    public ObservableCollection<SystemCheck> Checks { get; } = new();
    public ObservableCollection<BackupRow> BackupRows { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();

    public Array AffinityModes { get; } = Enum.GetValues(typeof(AffinityMode));
    public Array PriorityLevels { get; } = Enum.GetValues(typeof(PriorityLevel));

    // ── State ────────────────────────────────────────────────
    [ObservableProperty] private string currentPage = "dashboard";
    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "در حال شناسایی سخت‌افزار…";
    [ObservableProperty] private HardwareInfo? hardware;
    [ObservableProperty] private string? gameLoopDir;
    [ObservableProperty] private TweakLevel profileLevel;
    [ObservableProperty] private int appliedCount;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private double score;
    [ObservableProperty] private bool rebootPending;
    [ObservableProperty] private bool gameRunning;
    [ObservableProperty] private bool isWatching;
    [ObservableProperty] private string timerText = "—";
    [ObservableProperty] private string profileHint = "";

    // Settings
    [ObservableProperty] private bool advancedMode;
    [ObservableProperty] private bool autoOptimize;
    [ObservableProperty] private bool highResTimer;
    [ObservableProperty] private bool sessionPowerPlan;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool minimizeToTray;

    // Jitter
    [ObservableProperty] private bool isJitterRunning;
    [ObservableProperty] private double[] jitterValues = Array.Empty<double>();
    [ObservableProperty] private JitterResult? jitterResult;
    [ObservableProperty] private int jitterSeconds = 15;
    [ObservableProperty] private double jitterProgress;

    public string CpuName => Hardware?.CpuName ?? "…";
    public string Topology => Hardware?.TopologySummary ?? "";
    public string ProfileName => Hardware?.ProfileName ?? "";
    public string RamText => Hardware is null ? "" :
        $"{Hardware.RamGb:0} GB" + (Hardware.RamSpeedMts > 0 ? $" · {Hardware.RamSpeedMts} MT/s" : "");
    public string GpuText => Hardware?.PrimaryGpu?.Name ?? "—";
    public string WindowsText => Hardware?.WindowsName ?? "";
    public string GameLoopText => GameLoopDir ?? "پیدا نشد — مسیر را در تنظیمات وارد کنید";
    public string PreferredCoresText => Hardware?.Layout switch
    {
        CpuLayout.IntelHybrid => $"امولاتور روی {Hardware.PerformanceCoreCount} P-Core، لانچر و سرویس‌ها روی {Hardware.EfficientCoreCount} E-Core",
        CpuLayout.AmdMultiCcd => Hardware.IsX3D ? "امولاتور روی CCD دارای 3D V-Cache، بقیه روی CCD دوم" : "امولاتور روی یک CCD تا تأخیر بین CCDها حذف شود",
        null => "",
        _ => "همه هسته‌ها یکسان‌اند؛ فقط Priority و EcoQoS تنظیم می‌شود",
    };

    partial void OnHardwareChanged(HardwareInfo? value)
    {
        foreach (var p in new[] { nameof(CpuName), nameof(Topology), nameof(ProfileName), nameof(RamText), nameof(GpuText), nameof(WindowsText), nameof(PreferredCoresText) })
            OnPropertyChanged(p);
    }

    partial void OnGameLoopDirChanged(string? value) => OnPropertyChanged(nameof(GameLoopText));

    // ── Initialization ───────────────────────────────────────
    public async Task InitializeAsync()
    {
        try
        {
            Log.Info("GameLoop Booster شروع شد");
            var (hw, dir, exes, tweaks) = await Task.Run(() =>
            {
                var hw = HardwareDetector.Detect();
                var dir = GameLoopLocator.FindInstallDir(_settings.GameLoopPathOverride);
                var exes = GameLoopLocator.FindExecutables(dir);
                return (hw, dir, exes, TweakCatalog.Build(hw, exes));
            });

            Hardware = hw;
            GameLoopDir = dir;
            Log.Info($"سخت‌افزار: {hw.CpuName} · {hw.TopologySummary} · {RamText} · {GpuText}");
            Log.Info(dir is null ? "مسیر GameLoop پیدا نشد" : $"GameLoop: {dir} ({exes.Count} فایل اجرایی)");

            BuildCoreChips(hw);
            foreach (var t in tweaks)
            {
                var item = new TweakItemViewModel(t, hw, OnTweakEdited);
                Tweaks.Add(item);
            }
            ApplyProfileSelection();
            RefreshBackupRows();

            _optimizer = new ProcessOptimizer(hw, () => _settings);
            _optimizer.Updated += () => Ui(SyncProcesses);
            _optimizer.GameStateChanged += running => Ui(() =>
            {
                GameRunning = running;
                UpdateTimerText();
            });
            IsWatching = true;

            // Reflect the real scheduled-task state without triggering OnStartWithWindowsChanged.
#pragma warning disable MVVMTK0034
            startWithWindows = await Task.Run(Autostart.IsEnabled);
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(StartWithWindows));
            IsLoading = false;
            StatusText = "آماده";
            UpdateProfileHint();
            await RefreshAsync();
            await RunChecksAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"راه‌اندازی: {ex}");
            StatusText = "خطا در راه‌اندازی: " + ex.Message;
            IsLoading = false;
        }
    }

    private void BuildCoreChips(HardwareInfo hw)
    {
        CoreChips.Clear();
        int p = 0, e = 0;
        foreach (var c in hw.Cores)
        {
            var preferred = (hw.PreferredMask & c.Mask) != 0 && hw.Layout != CpuLayout.Uniform;
            string label, kind;
            if (hw.Layout == CpuLayout.IntelHybrid)
            {
                label = c.IsPerformance ? $"P{p++}" : $"E{e++}";
                kind = c.IsPerformance ? "P-Core" : "E-Core";
            }
            else
            {
                label = $"C{c.Index}";
                kind = hw.Layout == CpuLayout.AmdMultiCcd ? $"CCD{c.L3Domain}" + (preferred && hw.IsX3D ? " · V-Cache" : "") : "Core";
            }
            CoreChips.Add(new CoreChip(label, string.Join(",", c.LogicalProcessors), preferred, kind));
        }
    }

    // ── Profile & selection ──────────────────────────────────
    partial void OnProfileLevelChanged(TweakLevel value)
    {
        _settings.ProfileLevel = value;
        _settings.Save();
        ApplyProfileSelection();
        UpdateProfileHint();
        _ = RefreshAsync();
    }

    private void UpdateProfileHint()
    {
        var safe = Tweaks.Count(t => t.Level == TweakLevel.Safe);
        var aggr = Tweaks.Count(t => t.Level == TweakLevel.Aggressive);
        ProfileHint = ProfileLevel == TweakLevel.Safe
            ? $"{safe} تنظیم کم‌ریسک که روی همه سیستم‌ها جواب می‌دهد"
            : $"{safe + aggr} تنظیم: پایه + {aggr} مورد قوی‌تر (MPO، حافظه، شبکه و …)";
    }

    /// <summary>Default selection comes from the profile; explicit choices from advanced mode win.</summary>
    private void ApplyProfileSelection()
    {
        foreach (var t in Tweaks)
        {
            var byProfile = t.Level is { } l && l != TweakLevel.Expert && l <= ProfileLevel;
            var selected = _settings.TweakSelection.TryGetValue(t.Id, out var s) ? s : byProfile;
            _settings.TweakValues.TryGetValue(t.Id, out var v);
            t.Load(selected, v);
        }
        UpdateCounts();
    }

    private void OnTweakEdited(TweakItemViewModel t)
    {
        var byProfile = t.Level is { } l && l != TweakLevel.Expert && l <= ProfileLevel;
        if (t.IsSelected == byProfile) _settings.TweakSelection.Remove(t.Id);
        else _settings.TweakSelection[t.Id] = t.IsSelected;

        if (t.IsCustomValue) _settings.TweakValues[t.Id] = t.TargetValue;
        else _settings.TweakValues.Remove(t.Id);

        _settings.Save();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        SelectedCount = Tweaks.Count(t => t.IsSelected);
        AppliedCount = Tweaks.Count(t => t.IsSelected && t.IsApplied);
        Score = SelectedCount == 0 ? 0 : Math.Round(100.0 * AppliedCount / SelectedCount);
    }

    partial void OnAdvancedModeChanged(bool value)
    {
        _settings.AdvancedMode = value;
        _settings.Save();
        TweaksView.Refresh();
    }

    // ── Commands: tweaks ─────────────────────────────────────
    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Tweaks.Count == 0) return;
        var items = Tweaks.ToList();
        var updates = await Task.Run(() => items.Select(t => (t, t.ReadState(_backup.Get(t.Id)))).ToList());
        foreach (var (t, apply) in updates) apply(t);
        UpdateCounts();
        UpdateTimerText();
    }

    [RelayCommand]
    private async Task BoostAsync()
    {
        var selected = Tweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Info("هیچ تنظیمی انتخاب نشده است.");
            return;
        }
        await RunBusy($"در حال اعمال {selected.Count} تنظیم…", async () =>
        {
            var summary = await Task.Run(() => _engine.Apply(selected.Select(t => (t.Tweak, t.TargetValue))));
            RebootPending |= summary.RebootRequired;
            await RefreshAsync();
            var msg = $"{summary.Succeeded} تنظیم اعمال شد" + (summary.Failed > 0 ? $"، {summary.Failed} مورد ناموفق (جزئیات در گزارش)" : "") + ".";
            if (summary.RebootRequired) msg += "\nبرای اثر کامل برخی تنظیمات، سیستم را ری‌استارت کنید.";
            StatusText = msg.Replace("\n", " ");
            Info(msg);
        });
    }

    [RelayCommand]
    private async Task ApplyOneAsync(TweakItemViewModel? t)
    {
        if (t is null) return;
        await RunBusy($"اعمال {t.Title}…", async () =>
        {
            var s = await Task.Run(() => _engine.Apply(new[] { (t.Tweak, t.TargetValue) }));
            RebootPending |= s.RebootRequired;
            if (s.Failed > 0) Info($"ناموفق: {s.Results[0].Message}");
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task RestoreOneAsync(TweakItemViewModel? t)
    {
        if (t is null) return;
        await RunBusy($"بازگردانی {t.Title}…", async () =>
        {
            var r = await Task.Run(() => _engine.Restore(t.Id));
            if (!r.Success) Info($"ناموفق: {r.Message}");
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private void ResetValue(TweakItemViewModel? t)
    {
        if (t is not null) t.TargetValue = t.Tweak.RecommendedValue;
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (_backup.Entries.Count == 0)
        {
            Info("هنوز هیچ تنظیمی تغییر داده نشده؛ سیستم در حالت اولیه است.");
            return;
        }
        if (!Confirm($"همه {_backup.Entries.Count} تنظیم تغییر یافته به مقدار اولیه (قبل از نصب این برنامه) برگردانده شوند؟"))
            return;
        await RunBusy("در حال بازگردانی همه تنظیمات…", async () =>
        {
            var s = await Task.Run(_engine.RestoreAll);
            RebootPending = s.Results.Count > 0;
            await RefreshAsync();
            Info($"{s.Succeeded} تنظیم به حالت اولیه برگشت" + (s.Failed > 0 ? $"، {s.Failed} مورد ناموفق" : "") +
                 ".\nبرای اثر کامل، سیستم را ری‌استارت کنید.");
        });
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        await RunBusy("در حال ساخت System Restore Point (ممکن است یک دقیقه طول بکشد)…", async () =>
        {
            var r = await Task.Run(BackupStore.CreateRestorePoint);
            if (r.Success && r.Output.Contains("OK"))
            {
                Log.Success("System Restore Point ساخته شد");
                Info("نقطه بازگردانی ویندوز ساخته شد.");
            }
            else
            {
                Log.Error($"Restore Point: {r.Error.Trim()}");
                Info("ساخت Restore Point ناموفق بود. احتمالاً System Protection روی درایو C خاموش است.\n" + r.Error.Trim());
            }
        });
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
    }

    private void RefreshBackupRows()
    {
        BackupRows.Clear();
        foreach (var e in _backup.Entries.OrderBy(e => e.CapturedAt))
        {
            var original = !e.Existed ? "تعریف نشده (حذف می‌شود)" : e.Value ?? "—";
            BackupRows.Add(new BackupRow(e.Title, original, e.CapturedAt.ToString("yyyy/MM/dd HH:mm"), e.Kind.ToString()));
        }
    }

    // ── Processes ────────────────────────────────────────────
    partial void OnIsWatchingChanged(bool value)
    {
        if (_optimizer is null) return;
        if (value) _optimizer.Start(); else _optimizer.Stop();
        if (!value)
        {
            GameRunning = false;
            LiveProcesses.Clear();
        }
        UpdateTimerText();
    }

    private void SyncProcesses()
    {
        if (_optimizer is null) return;
        var now = _optimizer.Processes;
        LiveProcesses.Clear();
        foreach (var p in now) LiveProcesses.Add(p);
        GameRunning = _optimizer.GameRunning;
    }

    [RelayCommand]
    private void OptimizeNow()
    {
        if (_optimizer is null) return;
        if (!IsWatching) IsWatching = true;
        _optimizer.ApplyNow();
    }

    [RelayCommand]
    private void SaveRules()
    {
        _settings.ProcessRules = Rules.ToList();
        _settings.Save();
        Log.Info("قوانین پردازه‌ها ذخیره شد");
        _optimizer?.ApplyNow();
    }

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new ProcessRule { ProcessName = "example.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false });
    }

    [RelayCommand]
    private void RemoveRule(ProcessRule? rule)
    {
        if (rule is not null) Rules.Remove(rule);
    }

    [RelayCommand]
    private void ResetRules()
    {
        if (!Confirm("قوانین پردازه‌ها به پیش‌فرض برگردد؟")) return;
        Rules.Clear();
        foreach (var r in ProcessRule.Defaults()) Rules.Add(r);
        SaveRules();
    }

    [RelayCommand]
    private async Task BrowseGameLoopAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "پوشه نصب GameLoop (مثلاً TxGameAssistant)" };
        if (dlg.ShowDialog() != true) return;
        _settings.GameLoopPathOverride = dlg.FolderName;
        _settings.Save();
        GameLoopDir = dlg.FolderName;
        await RebuildCatalogAsync();
    }

    private async Task RebuildCatalogAsync()
    {
        if (Hardware is null) return;
        var hw = Hardware;
        var tweaks = await Task.Run(() => TweakCatalog.Build(hw, GameLoopLocator.FindExecutables(GameLoopDir)));
        Tweaks.Clear();
        foreach (var t in tweaks) Tweaks.Add(new TweakItemViewModel(t, hw, OnTweakEdited));
        ApplyProfileSelection();
        await RefreshAsync();
    }

    // ── Diagnostics ──────────────────────────────────────────
    [RelayCommand]
    private async Task RunChecksAsync()
    {
        if (Hardware is null) return;
        var hw = Hardware;
        var list = await Task.Run(() => SystemChecks.Run(hw));
        Checks.Clear();
        foreach (var c in list) Checks.Add(c);
        UpdateTimerText();
    }

    [RelayCommand]
    private async Task RunJitterAsync()
    {
        if (IsJitterRunning)
        {
            _jitterCts?.Cancel();
            return;
        }
        IsJitterRunning = true;
        JitterResult = null;
        _jitterBuffer.Clear();
        JitterValues = Array.Empty<double>();
        _jitterCts = new CancellationTokenSource();
        var progress = new Progress<JitterBatch>(batch =>
        {
            _jitterBuffer.AddRange(batch.Values);
            JitterProgress = Math.Min(100, batch.Fraction * 100);
            JitterValues = Overview(_jitterBuffer, 1200);
        });
        try
        {
            Log.Info($"تست Jitter ({JitterSeconds} ثانیه) شروع شد");
            var r = await JitterTest.RunAsync(TimeSpan.FromSeconds(JitterSeconds), progress, _jitterCts.Token);
            JitterResult = r;
            JitterProgress = 100;
            Log.Info($"Jitter: پایه {r.BaselineMs:0.000}ms · p99 {r.P99Ms:0.000}ms · بدترین {r.MaxMs:0.000}ms · بالای 1ms: {r.StallsOver1ms} · بالای 4ms: {r.StallsOver4ms} → {r.Verdict}");
        }
        catch (Exception ex)
        {
            Log.Error($"تست Jitter: {ex.Message}");
        }
        finally
        {
            IsJitterRunning = false;
        }
    }

    /// <summary>
    /// Squeezes the whole run into at most <paramref name="buckets"/> bars, keeping each bucket's worst value,
    /// so the chart shows when in the test a stall happened instead of only the last second or two.
    /// </summary>
    private static double[] Overview(List<double> values, int buckets)
    {
        if (values.Count <= buckets) return values.ToArray();
        var result = new double[buckets];
        for (var i = 0; i < buckets; i++)
        {
            var from = (int)((long)i * values.Count / buckets);
            var to = (int)((long)(i + 1) * values.Count / buckets);
            var max = 0.0;
            for (var j = from; j < to; j++) max = Math.Max(max, values[j]);
            result[i] = max;
        }
        return result;
    }

    private void UpdateTimerText()
    {
        try
        {
            var (_, _, cur) = TimerResolution.Query();
            TimerText = $"{cur:0.###} ms";
        }
        catch { TimerText = "—"; }
    }

    // ── Settings ─────────────────────────────────────────────
    partial void OnAutoOptimizeChanged(bool value) { _settings.AutoOptimizeProcesses = value; _settings.Save(); }
    partial void OnHighResTimerChanged(bool value) { _settings.HighResolutionTimer = value; _settings.Save(); }
    partial void OnSessionPowerPlanChanged(bool value) { _settings.SessionPowerPlan = value; _settings.Save(); }
    partial void OnStartMinimizedChanged(bool value) { _settings.StartMinimized = value; _settings.Save(); }
    partial void OnMinimizeToTrayChanged(bool value) { _settings.MinimizeToTray = value; _settings.Save(); }

    partial void OnStartWithWindowsChanged(bool value)
    {
        var exe = Environment.ProcessPath ?? "";
        Task.Run(() =>
        {
            if (!Autostart.Set(value, exe)) Ui(() => Info("تنظیم اجرای خودکار با ویندوز ناموفق بود (جزئیات در گزارش)."));
            else Log.Info(value ? "اجرای خودکار با ویندوز فعال شد" : "اجرای خودکار با ویندوز غیرفعال شد");
        });
    }

    public void Shutdown()
    {
        _optimizer?.Dispose();
        TimerResolution.Release();
        _settings.Save();
    }

    // ── Helpers ──────────────────────────────────────────────
    private async Task RunBusy(string status, Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = status;
        try { await work(); }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Info("خطا: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (StatusText == status) StatusText = "آماده";
        }
    }

    private static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    private static void Info(string text) =>
        MessageBox.Show(text, "GameLoop Booster", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

    private static bool Confirm(string text) =>
        MessageBox.Show(text, "GameLoop Booster", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) == MessageBoxResult.Yes;
}
