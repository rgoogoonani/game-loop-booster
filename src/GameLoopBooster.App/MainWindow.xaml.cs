using System.ComponentModel;
using System.Windows;
using GameLoopBooster.App.ViewModels;

namespace GameLoopBooster.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            // A borderless window maximizes slightly past the screen edges; pad it back in.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel { MinimizeToTray: true })
        {
            // Keep the GameLoop monitor running in the notification area.
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        ((App)Application.Current).ExitApp();
    }
}
