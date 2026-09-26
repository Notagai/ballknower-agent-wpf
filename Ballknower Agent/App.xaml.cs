using System;
using System.Windows;
using System.Windows.Threading;

using WpfApplication = System.Windows.Application;

namespace Ballknower;

public partial class App : WpfApplication
{
    public static bool IsExiting { get; internal set; }

    private BackgroundActivity? _backgroundActivity;

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
                () => MainWindow,
                ExitApplication);
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
        _backgroundActivity?.Dispose();
        _backgroundActivity = null;

        base.OnExit(e);
    }
}
