using System;
using System.Windows;
using System.Windows.Threading;

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

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(
                InitializeBackgroundActivity));
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
                ShowBallknowerFromShortcut,
                (MainWindow as MainWindow)?.OpeningShortcut ?? "Alt+Win");
    }

    private void ShowBallknowerFromShortcut()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(
                () => _backgroundActivity?.ShowBallknower()));
    }

    public void UpdateOpeningShortcut(string shortcut)
    {
        _keyboardShortcutManager?.SetOpeningShortcut(shortcut);
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
