using System.Windows;
using System.Windows.Threading;
using GameLoopBooster.App.ViewModels;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Global\GameLoopBooster.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("GameLoop Booster از قبل در حال اجراست (آیکون کنار ساعت ویندوز).", "GameLoop Booster",
                MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error($"Unhandled: {args.ExceptionObject}");

        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;

        _tray = new TrayIcon(window, vm);

        var startHidden = e.Args.Contains("--tray") || vm.StartMinimized;
        if (!startHidden) window.Show();

        _ = vm.InitializeAsync();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error($"خطای پیش‌بینی‌نشده: {e.Exception.Message}");
        MessageBox.Show(e.Exception.Message, "GameLoop Booster", MessageBoxButton.OK, MessageBoxImage.Error,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        e.Handled = true;
    }

    public void ExitApp()
    {
        (MainWindow?.DataContext as MainViewModel)?.Shutdown();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
