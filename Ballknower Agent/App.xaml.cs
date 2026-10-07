using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

using WpfApplication = System.Windows.Application;

namespace Ballknower;

public partial class App : WpfApplication
{
    public static bool IsExiting { get; internal set; }

    private BackgroundActivity? _backgroundActivity;
    private KeyboardShortcutManager? _keyboardShortcutManager;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterForWindowsStartup();

        var startHidden = e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        InitializeBackgroundActivity();

        if (!startHidden)
            mainWindow.Show();
    }

    private static void RegisterForWindowsStartup()
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "Ballknower";

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath);
            runKey?.SetValue(
                valueName,
                string.Concat('"', Environment.ProcessPath, '"', " --startup"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to register Ballknower for Windows startup: {ex}");
        }
    }

    private void InitializeBackgroundActivity()
    {
        if (_backgroundActivity is not null)
            return;

        _backgroundActivity =
            new BackgroundActivity(
                () => MainWindow as MainWindow,
                ExitApplication);

        _keyboardShortcutManager =
            new KeyboardShortcutManager(
                ShowBallknowerFromShortcut);
    }

    private void ShowBallknowerFromShortcut(LaunchMode launchMode)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(
                () => _backgroundActivity?.ShowBallknower(launchMode)));
    }
    public void ExitApplication()
    {
        if (IsExiting)
            return;

        IsExiting = true;

        MainWindow?.Close();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        _keyboardShortcutManager?.Dispose();
        _keyboardShortcutManager = null;

        _backgroundActivity?.Dispose();
        _backgroundActivity = null;

        base.OnExit(e);
    }
}
