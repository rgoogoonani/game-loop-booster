using System.Windows;
using GameLoopBooster.App.ViewModels;
using Forms = System.Windows.Forms;

namespace GameLoopBooster.App;

/// <summary>Notification-area icon so the monitor keeps running after the window is closed.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;

    public TrayIcon(Window window, MainViewModel vm)
    {
        _window = window;
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;

        var menu = new Forms.ContextMenuStrip { RightToLeft = Forms.RightToLeft.Yes };
        menu.Items.Add("نمایش", null, (_, _) => ShowWindow());
        var pause = new Forms.ToolStripMenuItem("مانیتور خودکار GameLoop") { CheckOnClick = true, Checked = vm.IsWatching };
        pause.CheckedChanged += (_, _) => vm.IsWatching = pause.Checked;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsWatching)) pause.Checked = vm.IsWatching;
            if (e.PropertyName == nameof(MainViewModel.GameRunning)) UpdateText(vm);
        };
        menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => ((App)Application.Current).ExitApp());

        _icon = new Forms.NotifyIcon
        {
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream),
            Text = "GameLoop Booster",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowWindow();
        };
    }

    private void UpdateText(MainViewModel vm) =>
        _icon.Text = vm.GameRunning ? "GameLoop Booster — GameLoop بهینه شده" : "GameLoop Booster — در انتظار GameLoop";

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
