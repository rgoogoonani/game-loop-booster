# GameLoop Booster

<div dir="rtl">

بهینه‌ساز ویندوز برای کاهش **لگ و میکرو‌استاتر** در امولاتور GameLoop (پابجی موبایل).
برنامه سخت‌افزار سیستم را شناسایی می‌کند و تنظیمات مناسب همان سیستم را اعمال می‌کند؛ همه تغییرات با یک کلیک قابل برگشت هستند.

## امکانات

- **شناسایی سخت‌افزار**: CPU هیبریدی اینتل (P-Core / E-Core)، CPUهای چند CCD و X3D شرکت AMD، رم (سرعت و تعداد ماژول)، کارت گرافیک (و وجود گرافیک داخلی)، نسخه ویندوز.
- **پروفایل خودکار** در دو سطح «پایه (امن)» و «حداکثر»:
  - رجیستری: `Win32PrioritySeparation`، MMCSS (`SystemResponsiveness`، `NetworkThrottlingIndex`، اولویت‌های Games)، `PowerThrottlingOff`، `GlobalTimerResolutionRequests`، Game DVR / Game Mode، MPO، `DisablePagingExecutive`، GPU Preference و Fullscreen Optimizations برای فایل‌های GameLoop، Nagle/TcpAck.
  - پاورپلن: Ultimate Performance، غیرفعال کردن Core Parking، EPP روی حداکثر کارایی، سیاست زمان‌بندی هیبریدی (ترجیح P-Core)، ASPM و USB Selective Suspend.
  - موارد متخصص (فقط دستی): `disabledynamictick`، HAGS، حداقل وضعیت پردازنده.
- **اعمال خودکار هنگام اجرای GameLoop**: قفل کردن موتور امولاتور (`aow_exe.exe` و …) روی P-Coreها یا CCD دارای V-Cache، Priority بالا، خروج از EcoQoS، و فرستادن لانچر و سرویس‌ها به E-Coreها. تایمر 0.5ms و پاورپلن Ultimate فقط در مدت بازی.
- **بکاپ کامل**: مقدار اصلی هر تنظیم قبل از اولین تغییر در `backup.json` ذخیره می‌شود (هرگز بازنویسی نمی‌شود) + خروجی `.reg` هر کلید + امکان ساخت System Restore Point.
- **بازگردانی یک‌کلیکی** همه تنظیمات به حالت قبل از برنامه، یا بازگردانی تک‌به‌تک.
- **حالت پیشرفته**: نمایش مسیر دقیق رجیستری / تنظیم powercfg، مقدار فعلی، مقدار اولیه و پیشنهادی، و وارد کردن مقدار دلخواه؛ ویرایش قوانین پردازه‌ها (Affinity، Priority، ماسک هگز دلخواه).
- **عیب‌یابی**: تست Jitter زمان‌بندی (مکث‌های DPC/ISR که عامل اصلی میکرو‌استاتر هستند) با نمودار زنده، بررسی VBS/HVCI، Hyper-V، تایمر، پاورپلن، XMP رم و …
- اجرا همراه ویندوز (Task Scheduler با دسترسی ادمین، بدون پیام UAC) و آیکون کنار ساعت.

## ایمنی

این برنامه **هیچ فایلی از GameLoop یا PUBG را تغییر نمی‌دهد**، کدی تزریق نمی‌کند و حافظه پردازه‌ها را نمی‌خواند.
فقط تنظیمات خود ویندوز و Affinity/Priority پردازه‌ها را از بیرون (مثل Task Manager) تغییر می‌دهد. اگر پردازه‌ای محافظت‌شده باشد، برنامه آن را رد می‌کند.

## دانلود و اجرا

- فایل `GameLoopBooster.exe` را از بخش **Actions → آخرین بیلد → Artifacts** (یا Releases) دانلود کنید. فایل مستقل است و نیازی به نصب .NET ندارد.
- برنامه با دسترسی Administrator اجرا می‌شود.
- پیشنهاد: اول یک Restore Point بسازید، تست Jitter را اجرا کنید، «بوست کن!» را بزنید، ری‌استارت کنید و دوباره تست بگیرید.

## ساخت از سورس

</div>

```powershell
# Windows + .NET 8 SDK
dotnet build GameLoopBooster.sln -c Release
dotnet publish src/GameLoopBooster.App/GameLoopBooster.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

<div dir="rtl">

## ساختار

</div>

```
src/GameLoopBooster.Core   hardware detection, tweak catalog, backup/restore, process optimizer, diagnostics
src/GameLoopBooster.App    WPF UI (dark, RTL, Vazirmatn font), tray icon, MVVM view models
```

Data (backup, .reg exports, settings, log): `%ProgramData%\GameLoopBooster`.
