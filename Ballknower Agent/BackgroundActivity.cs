using System;
using System.Drawing;
using System.Windows;

using FormsContextMenu = System.Windows.Forms.ContextMenuStrip;
using FormsMenuItem = System.Windows.Forms.ToolStripMenuItem;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;

namespace Ballknower;

/// <summary>
/// Keeps Ballknower alive in the background and exposes the
/// tray controls used to reopen or exit the application.
/// </summary>
public sealed class BackgroundActivity : IDisposable
{
    private readonly Func<MainWindow?> _getMainWindow;
    private readonly Action _exitApplication;

    private readonly FormsNotifyIcon _notifyIcon;
    private readonly FormsContextMenu _contextMenu;

    private bool _disposed;

    public BackgroundActivity(
        Func<MainWindow?> getMainWindow,
        Action exitApplication)
    {
        _getMainWindow = getMainWindow;
        _exitApplication = exitApplication;

        _contextMenu = new FormsContextMenu();

        var openItem =
            new FormsMenuItem("Open Ballknower");

        openItem.Click +=
            (_, _) => ShowBallknower();

        var settingsItem =
            new FormsMenuItem("Settings");

        settingsItem.Click +=
            (_, _) => OpenSettings();

        var exitItem =
            new FormsMenuItem("Exit Ballknower");

        exitItem.Click +=
            (_, _) => _exitApplication();

        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon =
            new FormsNotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Ballknower",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

        _notifyIcon.DoubleClick +=
            (_, _) => ShowBallknower();
    }

    public void OpenSettings()
    {
        if (_disposed)
            return;

        var window = _getMainWindow();

        window?.OpenSettingsFromTray();
    }

    public void ShowBallknower()
    {
        if (_disposed)
            return;

        var window = _getMainWindow();

        if (window is null)
            return;

        window.RefreshDesktopBackdropForReopen();
        window.FocusBallknower();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
