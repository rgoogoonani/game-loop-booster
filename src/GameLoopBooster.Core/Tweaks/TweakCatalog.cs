using GameLoopBooster.Core.Hardware;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Tweaks;

/// <summary>
/// Every tweak the app knows, with a per-hardware recommendation. Nothing here touches GameLoop's files:
/// per-executable entries only write Windows' own per-app settings (GPU preference, compatibility layer).
/// </summary>
public static class TweakCatalog
{
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = SystemProfile + @"\Tasks\Games";

    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";

    public static List<Tweak> Build(HardwareInfo hw, IReadOnlyList<string> gameExecutables)
    {
        var hybrid = hw.Layout == CpuLayout.IntelHybrid;
        var list = new List<Tweak>
        {
            // ── Scheduler ─────────────────────────────────────────────
            new RegistryTweak
            {
                Id = "sched.priosep",
                Title = "اولویت‌دهی به برنامه پیش‌زمینه (Win32PrioritySeparation)",
                Description = "مقدار 38 (0x26) کوانتوم‌های کوتاه و متغیر با بیشترین Boost برای پنجره فعال می‌دهد؛ ترد‌های امولاتور سریع‌تر نوبت CPU می‌گیرند و تأخیر ورودی کمتر می‌شود. پیش‌فرض ویندوز 2 است.",
                Category = TweakCategory.Scheduler,
                KeyPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                ValueName = "Win32PrioritySeparation",
                RecommendedValue = "38",
                ValueHint = "38 (0x26) پیشنهادی · 42 (0x2A) کوانتوم ثابت کوتاه · 2 پیش‌فرض",
            },
            new RegistryTweak
            {
                Id = "mmcss.responsiveness",
                Title = "کاهش سهم CPU رزرو شده برای پس‌زمینه (SystemResponsiveness)",
                Description = "MMCSS به‌صورت پیش‌فرض ۲۰٪ CPU را برای کارهای پس‌زمینه نگه می‌دارد. با 10 سهم بیشتری به ترد‌های چندرسانه‌ای و بازی می‌رسد.",
                Category = TweakCategory.Scheduler,
                KeyPath = SystemProfile,
                ValueName = "SystemResponsiveness",
                RecommendedValue = "10",
                ValueHint = "0 تا 100 (درصد) · پیش‌فرض 20",
            },
            new RegistryTweak
            {
                Id = "mmcss.netthrottle",
                Title = "حذف محدودیت شبکه هنگام پخش چندرسانه‌ای (NetworkThrottlingIndex)",
                Description = "ویندوز وقتی صدا/ویدیو پخش می‌شود ترافیک شبکه را محدود می‌کند. غیرفعال کردن آن جلوی پرش پینگ هنگام بازی را می‌گیرد.",
                Category = TweakCategory.Network,
                KeyPath = SystemProfile,
                ValueName = "NetworkThrottlingIndex",
                RecommendedValue = "4294967295",
                ValueHint = "0xFFFFFFFF = خاموش · پیش‌فرض 10",
            },
            new RegistryTweak
            {
                Id = "mmcss.games.gpu",
                Title = "اولویت GPU برای وظیفه Games در MMCSS",
                Description = "اولویت زمان‌بندی GPU برای برنامه‌هایی که در دسته Games ثبت می‌شوند.",
                Category = TweakCategory.Scheduler,
                KeyPath = GamesTask,
                ValueName = "GPU Priority",
                RecommendedValue = "8",
                ValueHint = "0 تا 31 · پیش‌فرض 8",
            },
            new RegistryTweak
            {
                Id = "mmcss.games.priority",
                Title = "اولویت CPU برای وظیفه Games در MMCSS",
                Description = "اولویت ترد‌هایی که در دسته Games ثبت شده‌اند.",
                Category = TweakCategory.Scheduler,
                KeyPath = GamesTask,
                ValueName = "Priority",
                RecommendedValue = "6",
                ValueHint = "1 تا 8 · پیش‌فرض 2",
            },
            new RegistryTweak
            {
                Id = "mmcss.games.category",
                Title = "دسته زمان‌بندی High برای Games",
                Description = "ترد‌های Games در دسته High زمان‌بندی می‌شوند.",
                Category = TweakCategory.Scheduler,
                KeyPath = GamesTask,
                ValueName = "Scheduling Category",
                Kind = RegistryValueKind.String,
                RecommendedValue = "High",
                ValueHint = "High / Medium / Low",
            },
            new RegistryTweak
            {
                Id = "mmcss.games.sfio",
                Title = "اولویت I/O بالا برای Games",
                Description = "اولویت ورودی/خروجی دیسک برای Games.",
                Category = TweakCategory.Scheduler,
                KeyPath = GamesTask,
                ValueName = "SFIO Priority",
                Kind = RegistryValueKind.String,
                RecommendedValue = "High",
                ValueHint = "High / Normal / Low / Idle",
            },
            new RegistryTweak
            {
                Id = "power.throttling",
                Title = "خاموش کردن Power Throttling (EcoQoS)",
                Description = hybrid
                    ? "روی CPUهای هیبریدی اینتل، ویندوز برنامه‌هایی را که فکر می‌کند «پس‌زمینه» هستند به E-coreها می‌فرستد. امولاتور وقتی پنجره‌اش فوکوس ندارد قربانی این رفتار می‌شود. این مهم‌ترین تنظیم برای سیستم شماست."
                    : "جلوی اینکه ویندوز برنامه‌ها را در حالت کم‌مصرف (EcoQoS) اجرا کند را می‌گیرد.",
                Category = TweakCategory.Power,
                RequiresReboot = true,
                KeyPath = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
                ValueName = "PowerThrottlingOff",
                RecommendedValue = "1",
                ValueHint = "1 = خاموش · 0 یا حذف = پیش‌فرض",
                Recommend = h => h.Layout == CpuLayout.IntelHybrid ? TweakLevel.Safe : TweakLevel.Aggressive,
            },

            // ── Power plan ────────────────────────────────────────────
            new PowerPlanTweak
            {
                Id = "power.plan",
                Title = "پاورپلن Ultimate Performance",
                Description = "پلن مخفی Ultimate Performance را فعال می‌کند (اگر ویندوز اجازه ندهد High Performance). تنظیمات پردازنده در ادامه روی همین پلن اعمال می‌شوند.",
                Category = TweakCategory.Power,
                RecommendedValue = "ultimate",
                ValueHint = "ultimate / high / GUID پلن",
            },
            new PowerSettingTweak
            {
                Id = "power.parking.class0",
                Title = "غیرفعال کردن Core Parking",
                Description = "حداقل درصد هسته‌های بیدار. با 100 هیچ هسته‌ای پارک (خاموش) نمی‌شود و تأخیر بیدار شدن هسته که عامل میکرو‌استاتر است حذف می‌شود.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "0cc5b647-c1df-4637-891a-dec35c318583", SettingAlias = "CPMINCORES",
                RecommendedValue = "100",
                ValueHint = "0 تا 100 (درصد)",
            },
            new PowerSettingTweak
            {
                Id = "power.parking.class1",
                Title = "غیرفعال کردن Core Parking برای P-Coreها",
                Description = "همان تنظیم قبلی برای کلاس دوم هسته‌ها (P-coreهای اینتل هیبرید).",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "0cc5b647-c1df-4637-891a-dec35c318584", SettingAlias = "CPMINCORES1",
                RecommendedValue = "100",
                ValueHint = "0 تا 100 (درصد)",
                Recommend = h => h.Layout == CpuLayout.IntelHybrid ? TweakLevel.Safe : null,
            },
            new PowerSettingTweak
            {
                Id = "power.epp",
                Title = "Energy Performance Preference روی حداکثر کارایی",
                Description = "به CPU می‌گوید فرکانس را سریع بالا ببرد و پایین نیاورد. 0 یعنی تمام‌قدرت؛ پیش‌فرض بالانسد حدود 33 است.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", SettingAlias = "PERFEPP",
                RecommendedValue = "0",
                ValueHint = "0 (کارایی) تا 100 (صرفه‌جویی)",
            },
            new PowerSettingTweak
            {
                Id = "power.epp1",
                Title = "Energy Performance Preference برای P-Coreها",
                Description = "همان EPP برای کلاس P-core.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "36687f9e-e3a5-4dbf-b1dc-15eb381c6864", SettingAlias = "PERFEPP1",
                RecommendedValue = "0",
                ValueHint = "0 تا 100",
                Recommend = h => h.Layout == CpuLayout.IntelHybrid ? TweakLevel.Safe : null,
            },
            new PowerSettingTweak
            {
                Id = "power.boostmode",
                Title = "Turbo Boost حالت Aggressive",
                Description = "حالت توربو پردازنده را روی Aggressive قرار می‌دهد.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "be337238-0d82-4146-a960-4f3749d470c7", SettingAlias = "PERFBOOSTMODE",
                RecommendedValue = "2",
                ValueHint = "0 خاموش · 1 فعال · 2 Aggressive",
            },
            new PowerSettingTweak
            {
                Id = "power.heteropolicy",
                Title = "زمان‌بندی هیبریدی: ترجیح P-Core",
                Description = "به زمان‌بند ویندوز می‌گوید ترد‌های سنگین را ترجیحاً روی P-core بگذارد (مقدار 2). روی CPU شما به همراه پین کردن پردازه‌ها، پرش ترد امولاتور بین P و E-core را کم می‌کند.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "93b8b6dc-0698-4d1c-9ee4-0644e900c85d", SettingAlias = "SCHEDPOLICY",
                RecommendedValue = "2",
                ValueHint = "0 همه · 1 فقط P · 2 ترجیح P · 3 فقط E · 4 ترجیح E · 5 خودکار",
                Recommend = h => h.Layout == CpuLayout.IntelHybrid ? TweakLevel.Aggressive : null,
            },
            new PowerSettingTweak
            {
                Id = "power.heteropolicy.short",
                Title = "زمان‌بندی هیبریدی ترد‌های کوتاه: ترجیح P-Core",
                Description = "همان سیاست برای ترد‌های کوتاه‌مدت.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "bae08b81-2d5e-4688-ad6a-13243356654b", SettingAlias = "SHORTSCHEDPOLICY",
                RecommendedValue = "2",
                ValueHint = "0 تا 5 (مثل بالا)",
                Recommend = h => h.Layout == CpuLayout.IntelHybrid ? TweakLevel.Aggressive : null,
            },
            new PowerSettingTweak
            {
                Id = "power.minstate",
                Title = "حداقل وضعیت پردازنده 100٪",
                Description = "فرکانس را همیشه بالا نگه می‌دارد. اثر روی استاتر کم است ولی دما و مصرف را بالا می‌برد؛ فقط اگر خنک‌کننده قوی دارید.",
                Category = TweakCategory.Power,
                SubGroup = SubProcessor, Setting = "893dee8e-2bef-41e0-89c6-b55d0929964c", SettingAlias = "PROCTHROTTLEMIN",
                RecommendedValue = "100",
                ValueHint = "0 تا 100 (درصد)",
                Recommend = _ => TweakLevel.Expert,
            },
            new PowerSettingTweak
            {
                Id = "power.pcie.aspm",
                Title = "خاموش کردن مدیریت توان PCIe (ASPM)",
                Description = "لینک PCIe کارت گرافیک وارد حالت کم‌مصرف نمی‌شود؛ تأخیر بیدار شدن لینک حذف می‌شود.",
                Category = TweakCategory.Power,
                SubGroup = SubPciExpress, Setting = "ee12f906-d277-404b-b6da-e5fa1a576df5", SettingAlias = "ASPM",
                RecommendedValue = "0",
                ValueHint = "0 خاموش · 1 متوسط · 2 حداکثر صرفه‌جویی",
            },
            new PowerSettingTweak
            {
                Id = "power.usb.suspend",
                Title = "خاموش کردن USB Selective Suspend",
                Description = "موس و کیبورد هیچ‌وقت به خواب نمی‌روند؛ جلوی پرش و تأخیر ورودی را می‌گیرد.",
                Category = TweakCategory.Power,
                SubGroup = SubUsb, Setting = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", SettingAlias = "USBSELECTIVESUSPEND",
                RecommendedValue = "0",
                ValueHint = "0 خاموش · 1 روشن",
            },

            // ── Timer ─────────────────────────────────────────────────
            new RegistryTweak
            {
                Id = "timer.global",
                Title = "تایمر دقیق سراسری (GlobalTimerResolutionRequests)",
                Description = "از ویندوز ۱۱ به بعد درخواست تایمر دقیق یک برنامه فقط روی خودش اثر دارد و وقتی پنجره‌اش minimize یا بدون فوکوس باشد نادیده گرفته می‌شود. این مقدار رفتار سراسری قدیمی را برمی‌گرداند تا تایمر 0.5ms این برنامه به امولاتور هم برسد.",
                Category = TweakCategory.Timer,
                RequiresReboot = true,
                KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                ValueName = "GlobalTimerResolutionRequests",
                RecommendedValue = "1",
                ValueHint = "1 = فعال",
                Recommend = h => h.WindowsBuild >= 22000 ? TweakLevel.Safe : null,
            },
            CommandTweaks.DisableDynamicTick,

            // ── Game Bar / DVR ───────────────────────────────────────
            new RegistryTweak
            {
                Id = "gamebar.gamemode",
                Title = "روشن کردن Game Mode ویندوز",
                Description = "Game Mode جلوی آپدیت ویندوز و کارهای سنگین پس‌زمینه را حین بازی می‌گیرد.",
                Category = TweakCategory.GameBar,
                Hive = RegistryHive.CurrentUser,
                KeyPath = @"Software\Microsoft\GameBar",
                ValueName = "AutoGameModeEnabled",
                RecommendedValue = "1",
                ValueHint = "1 روشن · 0 خاموش",
            },
            new RegistryTweak
            {
                Id = "gamebar.dvr",
                Title = "خاموش کردن Game DVR (ضبط پس‌زمینه)",
                Description = "ضبط پس‌زمینه Xbox Game Bar روی هر فریم هزینه دارد و یکی از عوامل شناخته‌شده استاتر است.",
                Category = TweakCategory.GameBar,
                Hive = RegistryHive.CurrentUser,
                KeyPath = @"System\GameConfigStore",
                ValueName = "GameDVR_Enabled",
                RecommendedValue = "0",
                ValueHint = "0 خاموش · 1 روشن",
            },
            new RegistryTweak
            {
                Id = "gamebar.capture",
                Title = "خاموش کردن App Capture",
                Description = "قابلیت ضبط برنامه‌ها در Game Bar.",
                Category = TweakCategory.GameBar,
                Hive = RegistryHive.CurrentUser,
                KeyPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                ValueName = "AppCaptureEnabled",
                RecommendedValue = "0",
                ValueHint = "0 خاموش · 1 روشن",
            },
            new RegistryTweak
            {
                Id = "gamebar.policy",
                Title = "غیرفعال کردن Game DVR در سطح سیستم (Policy)",
                Description = "همان Game DVR ولی از طریق Group Policy برای همه کاربران.",
                Category = TweakCategory.GameBar,
                KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR",
                ValueName = "AllowGameDVR",
                RecommendedValue = "0",
                ValueHint = "0 خاموش",
                Recommend = _ => TweakLevel.Aggressive,
            },

            // ── GPU ───────────────────────────────────────────────────
            new RegistryTweak
            {
                Id = "gpu.mpo",
                Title = "غیرفعال کردن Multiplane Overlay (MPO)",
                Description = "MPO روی برخی درایورها (به‌خصوص AMD و NVIDIA در حالت پنجره‌ای) باعث چشمک زدن و استاتر می‌شود. امولاتور پنجره‌ای رندر می‌کند، پس ارزش تست دارد. نیاز به ری‌استارت.",
                Category = TweakCategory.Gpu,
                RequiresReboot = true,
                KeyPath = @"SOFTWARE\Microsoft\Windows\Dwm",
                ValueName = "OverlayTestMode",
                RecommendedValue = "5",
                ValueHint = "5 = غیرفعال · حذف = پیش‌فرض",
                Recommend = _ => TweakLevel.Aggressive,
            },
            new RegistryTweak
            {
                Id = "gpu.hags",
                Title = "Hardware-Accelerated GPU Scheduling (HAGS)",
                Description = "زمان‌بندی GPU را به خود کارت گرافیک می‌سپارد. اثرش روی امولاتورها متفاوت است؛ هر دو حالت را با تست Jitter و حس بازی مقایسه کنید. نیاز به ری‌استارت.",
                Category = TweakCategory.Gpu,
                RequiresReboot = true,
                KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                ValueName = "HwSchMode",
                RecommendedValue = "2",
                ValueHint = "2 روشن · 1 خاموش",
                Recommend = _ => TweakLevel.Expert,
            },

            // ── Memory ────────────────────────────────────────────────
            new RegistryTweak
            {
                Id = "mem.pagingexec",
                Title = "نگه داشتن کرنل و درایورها در RAM (DisablePagingExecutive)",
                Description = "کد کرنل و درایورها به Pagefile منتقل نمی‌شوند. با RAM کافی بی‌خطر است.",
                Category = TweakCategory.Memory,
                RequiresReboot = true,
                KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                ValueName = "DisablePagingExecutive",
                RecommendedValue = "1",
                ValueHint = "1 فعال · 0 پیش‌فرض",
                Recommend = h => h.RamGb >= 16 ? TweakLevel.Aggressive : null,
            },
            CommandTweaks.MemoryCompression,
        };

        foreach (var exe in gameExecutables)
        {
            var name = Path.GetFileName(exe);
            list.Add(new RegistryTweak
            {
                Id = $"gpu.pref.{name.ToLowerInvariant()}",
                Title = $"اجرای {name} روی کارت گرافیک اصلی",
                Description = hw.HasIntegratedAndDedicated
                    ? "سیستم شما گرافیک داخلی هم دارد؛ این تنظیم ویندوز مطمئن می‌شود امولاتور روی کارت گرافیک قوی اجرا شود."
                    : "تنظیم GPU Preference ویندوز برای این فایل (High Performance).",
                Category = TweakCategory.Emulator,
                Hive = RegistryHive.CurrentUser,
                KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences",
                ValueName = exe,
                Kind = RegistryValueKind.String,
                RecommendedValue = "GpuPreference=2;",
                ValueHint = "GpuPreference=2; پرقدرت · =1; کم‌مصرف · =0; خودکار",
                Recommend = h => h.HasIntegratedAndDedicated ? TweakLevel.Safe : TweakLevel.Aggressive,
            });
            list.Add(new CompatLayerTweak
            {
                Id = $"compat.fso.{name.ToLowerInvariant()}",
                Title = $"غیرفعال کردن Fullscreen Optimizations برای {name}",
                Description = "معادل تیک Disable fullscreen optimizations در Properties فایل؛ فقط پرچم سازگاری ویندوز است و به خود فایل دست نمی‌زند. پرچم‌های قبلی حفظ می‌شوند.",
                Category = TweakCategory.Emulator,
                Hive = RegistryHive.CurrentUser,
                KeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                ValueName = exe,
                Kind = RegistryValueKind.String,
                RecommendedValue = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE",
                ValueHint = "پرچم‌های سازگاری ویندوز",
                Recommend = _ => TweakLevel.Aggressive,
            });
        }

        foreach (var nic in NetworkInterfaces())
        {
            list.Add(new RegistryTweak
            {
                Id = $"net.ack.{nic}",
                Title = $"TcpAckFrequency برای کارت شبکه {nic[..9]}…}}",
                Description = "تأیید بسته‌های TCP را بدون تأخیر می‌فرستد. گیم‌پلی پابجی بیشتر UDP است، پس اثرش بیشتر روی لابی/لاگین است.",
                Category = TweakCategory.Network,
                KeyPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic}",
                ValueName = "TcpAckFrequency",
                RecommendedValue = "1",
                ValueHint = "1 فعال",
                Recommend = _ => TweakLevel.Aggressive,
            });
            list.Add(new RegistryTweak
            {
                Id = $"net.nodelay.{nic}",
                Title = $"TCPNoDelay (خاموش کردن Nagle) برای {nic[..9]}…}}",
                Description = "بسته‌های کوچک TCP را بدون جمع کردن ارسال می‌کند.",
                Category = TweakCategory.Network,
                KeyPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic}",
                ValueName = "TCPNoDelay",
                RecommendedValue = "1",
                ValueHint = "1 فعال",
                Recommend = _ => TweakLevel.Aggressive,
            });
        }

        return list;
    }

    /// <summary>Interfaces that currently have an IPv4 address (the ones actually carrying game traffic).</summary>
    private static IEnumerable<string> NetworkInterfaces()
    {
        using var root = RegistryIO.Open(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", false);
        if (root is null) yield break;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            var dhcp = k?.GetValue("DhcpIPAddress") as string;
            var stat = (k?.GetValue("IPAddress") as string[])?.FirstOrDefault();
            var ip = !string.IsNullOrEmpty(dhcp) && dhcp != "0.0.0.0" ? dhcp : stat;
            if (!string.IsNullOrEmpty(ip) && ip != "0.0.0.0" && !ip.StartsWith("169.254"))
                yield return name;
        }
    }
}
