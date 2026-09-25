using Ballknower.AI;
using Ballknower.Commands;
using Ballknower.Config;
using Ballknower.Diagnostics;
using Ballknower.Tools;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace Ballknower;

public partial class MainWindow : Window
{
    private const double InitialPillPosition = 0.30;
    private const double ChatPillPosition = 0.65;

    private const double MessageGap = 16;
    private const double PillAnimationMilliseconds = 600;
    private const double WindowFadeMilliseconds = 200;

    private readonly AppSettings _settings;
    private readonly List<OpenRouterMessage> _conversation;
    private readonly CommandParser _commandParser;
    private readonly ToolRegistry _toolRegistry;
    private readonly CredentialStore _credentialStore;

    private readonly TranslateTransform _inputPillTransform;
    private readonly TranslateTransform _messageAreaTransform;

    private readonly SolidColorBrush _lightBrush =
        new SolidColorBrush(Colors.White);

    private readonly SolidColorBrush _darkBrush =
        new SolidColorBrush(Colors.Black);

    private readonly SolidColorBrush _blackTextBrush =
        new SolidColorBrush(Colors.Black);

    private readonly SolidColorBrush _whiteTextBrush =
        new SolidColorBrush(Colors.White);

    private SettingsWindow? _settingsWindow;

    private Storyboard? _pillStoryboard;

    private bool _isProcessing;
    private bool _hasEnteredChat;
    private bool _desktopUnblurred;
    private bool _isCapturingBackdrop;

    private bool _isRefreshingPinnedBackdrop;
    private bool _inputIsLight;
    private bool _messageAreaIsLight;

    private bool _isApplyingCommandSuggestion;

    /*
     * Prevents the close event from starting the fade
     * more than once.
     */
    private bool _isClosingWithFade;

    /*
     * Prevents the startup fade from being started more
     * than once.
     */
    private bool _isOpeningWithFade;

    /*
     * /pin state.
     *
     * When true, losing focus does NOT hide Ballknower.
     */
    private bool _isPinned;

    /*
     * Used when the user has explicitly confirmed that
     * Ballknower should close.
     */
    private bool _allowClose;

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    public MainWindow()
    {
        InitializeComponent();

        /*
         * Keep the window invisible while the initial
         * desktop backdrop and adaptive colors are prepared.
         */
        Opacity = 0;

        _lightBrush.Freeze();
        _darkBrush.Freeze();
        _blackTextBrush.Freeze();
        _whiteTextBrush.Freeze();

        InputPill.Background =
            _darkBrush;

        MessageArea.Background =
            _darkBrush;

        ChatInput.Foreground =
            _whiteTextBrush;

        ChatInput.CaretBrush =
            _whiteTextBrush;

        _inputIsLight = false;
        _messageAreaIsLight = false;

        _inputPillTransform =
            new TranslateTransform();

        _messageAreaTransform =
            new TranslateTransform();

        InputPill.RenderTransform =
            _inputPillTransform;

        MessageArea.RenderTransform =
            _messageAreaTransform;

        SizeChanged +=
            MainWindow_SizeChanged;

        Loaded +=
            MainWindow_Loaded;

        Closed +=
            MainWindow_Closed;

        Closing +=
            MainWindow_Closing;

        Deactivated +=
            MainWindow_Deactivated;

        Activated +=
            MainWindow_Activated;

        _toolRegistry =
            new ToolRegistry();

        _toolRegistry.Register(
            new CreateFileTool());

        _toolRegistry.Register(
            new DeleteFileTool());

        _toolRegistry.Register(
            new ReadFileTool());

        _toolRegistry.Register(
            new ListDirectoryTool());

        _toolRegistry.Register(
            new MoveFileTool());

        var settingsStore =
            new SettingsStore();

        _settings =
            settingsStore.Load();

        _credentialStore =
            new CredentialStore();

        _commandParser =
            new CommandParser();

        _conversation =
            new List<OpenRouterMessage>
            {
                new OpenRouterMessage
                {
                    Role = "system",
                    Content =
                        "You are Ballknower, a Windows desktop assistant. " +
                        "Use the provided native tools when the user requests " +
                        "supported file operations. " +
                        "For familiar folders use ~/Desktop, ~/Documents " +
                        "or ~/Downloads. Never guess the Windows username. " +
                        "The application handles tool execution and " +
                        "deletion confirmation. " +
                        "Do not claim an action succeeded before receiving " +
                        "its tool result. " +
                        "If the user cancels an action, do not retry it " +
                        "without a new user request. " +
                        "Do not write XML-style tool calls or JSON tool " +
                        "requests in ordinary chat messages."
                }
            };

        ContentRoot.Loaded +=
            (_, _) =>
            {
                UpdateLayoutPositions();

                ChatInput.Focus();

                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(
                        UpdateDesktopBackdrop));
            };

        PreviewKeyDown +=
            MainWindow_PreviewKeyDown;
    }

    private void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOpeningWithFade)
            return;

        _isOpeningWithFade = true;

        /*
         * The window starts at zero opacity while the
         * desktop backdrop is prepared.
         */
        if (Opacity < 1)
        {
            var fadeIn =
                new DoubleAnimation
                {
                    From = Opacity,
                    To = 1,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            WindowFadeMilliseconds),

                    EasingFunction =
                        new QuadraticEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        }
                };

            fadeIn.Completed +=
                (_, _) =>
                {
                    Opacity = 1;
                    _isOpeningWithFade = false;
                };

            BeginAnimation(
                Window.OpacityProperty,
                fadeIn);
        }
        else
        {
            _isOpeningWithFade = false;
        }
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _pillStoryboard?.Stop();

        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        /*
         * The second Close() call, after the fade finishes,
         * is allowed to actually close the window.
         */
        if (_isClosingWithFade)
        {
            return;
        }

        /*
         * If Ballknower is pinned, ask for confirmation.
         */
        if (_isPinned && !_allowClose)
        {
            var result =
                WpfMessageBox.Show(
                    "Do you want to close Ballknower?",
                    "Close Ballknower",
                    WpfMessageBoxButton.YesNo,
                    WpfMessageBoxImage.Question);

            if (result ==
                WpfMessageBoxResult.Yes)
            {
                _allowClose = true;
            }
            else
            {
                e.Cancel = true;

                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(
                        () =>
                        {
                            if (!IsVisible)
                                Show();

                            Activate();

                            ChatInput.Focus();
                        }));

                return;
            }
        }

        /*
         * Stop the actual close and perform the fade first.
         */
        e.Cancel = true;

        _isClosingWithFade = true;

        _pillStoryboard?.Stop();

        var fadeOut =
            new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration =
                    TimeSpan.FromMilliseconds(
                        WindowFadeMilliseconds),

                EasingFunction =
                    new QuadraticEase
                    {
                        EasingMode =
                            EasingMode.EaseIn
                    }
            };

        fadeOut.Completed +=
            (_, _) =>
            {
                _allowClose = true;

                /*
                 * Remove the animation before the second
                 * Close() so the window can close normally.
                 */
                BeginAnimation(
                    Window.OpacityProperty,
                    null);

                Close();
            };

        BeginAnimation(
            Window.OpacityProperty,
            fadeOut);
    }

    private void MainWindow_Deactivated(
        object? sender,
        EventArgs e)
    {
        if (_isCapturingBackdrop)
            return;

        if (_settingsWindow is not null &&
            _settingsWindow.IsVisible)
        {
            return;
        }

        if (_isPinned)
        {
            return;
        }

        Hide();
    }

    private void MainWindow_Activated(
        object? sender,
        EventArgs e)
    {
        if (!_isPinned)
            return;

        if (_desktopUnblurred)
            return;

        if (_isCapturingBackdrop ||
            _isRefreshingPinnedBackdrop)
        {
            return;
        }

        RefreshPinnedBackdrop();
    }

    private void UpdateDesktopBackdrop()
    {
        if (_desktopUnblurred)
            return;

        if (DesktopBackdrop.Source is not null)
            return;

        try
        {
            _isCapturingBackdrop = true;

            Hide();

            var bounds =
                System.Windows.Forms.Screen
                    .PrimaryScreen?
                    .Bounds;

            if (bounds is null)
                return;

            using var screenshot =
                new DrawingBitmap(
                    bounds.Value.Width,
                    bounds.Value.Height,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       screenshot))
            {
                graphics.CopyFromScreen(
                    bounds.Value.Left,
                    bounds.Value.Top,
                    0,
                    0,
                    screenshot.Size,
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            int smallWidth =
                Math.Max(
                    1,
                    screenshot.Width / 4);

            int smallHeight =
                Math.Max(
                    1,
                    screenshot.Height / 4);

            using var small =
                new DrawingBitmap(
                    smallWidth,
                    smallHeight,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       small))
            {
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D
                        .InterpolationMode.HighQualityBilinear;

                graphics.DrawImage(
                    screenshot,
                    0,
                    0,
                    smallWidth,
                    smallHeight);
            }

            using var blurred =
                new DrawingBitmap(
                    screenshot.Width,
                    screenshot.Height,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       blurred))
            {
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D
                        .InterpolationMode.HighQualityBilinear;

                graphics.DrawImage(
                    small,
                    0,
                    0,
                    blurred.Width,
                    blurred.Height);
            }

            using var stream =
                new MemoryStream();

            blurred.Save(
                stream,
                System.Drawing.Imaging.ImageFormat.Png);

            stream.Position = 0;

            var image =
                new BitmapImage();

            image.BeginInit();

            image.CacheOption =
                BitmapCacheOption.OnLoad;

            image.StreamSource =
                stream;

            image.EndInit();

            image.Freeze();

            DesktopBackdrop.Source =
                image;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Desktop backdrop capture failed",
                ex);
        }
        finally
        {
            _isCapturingBackdrop = false;

            Show();

            Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(
                    () =>
                    {
                        UpdateAllAdaptiveColors();

                        /*
                         * MainWindow_Loaded handles the initial
                         * fade-in. If the window is already loaded,
                         * make sure it becomes visible.
                         */
                        if (!_isOpeningWithFade)
                        {
                            var fadeIn =
                                new DoubleAnimation
                                {
                                    From = Opacity,
                                    To = 1,
                                    Duration =
                                        TimeSpan.FromMilliseconds(
                                            WindowFadeMilliseconds),

                                    EasingFunction =
                                        new QuadraticEase
                                        {
                                            EasingMode =
                                                EasingMode.EaseOut
                                        }
                                };

                            BeginAnimation(
                                Window.OpacityProperty,
                                fadeIn);
                        }
                    }));
        }
    }

    private void RefreshPinnedBackdrop()
    {
        if (_isRefreshingPinnedBackdrop)
            return;

        _isRefreshingPinnedBackdrop = true;
        _isCapturingBackdrop = true;

        try
        {
            /*
             * Completely remove Ballknower from the desktop
             * before taking the screenshot.
             */
            Opacity = 0;

            Hide();

            /*
             * Give Windows/WPF a chance to finish removing
             * Ballknower from the visible desktop composition.
             */
            Dispatcher.Invoke(
                DispatcherPriority.Render,
                new Action(() => { }));

            var bounds =
                System.Windows.Forms.Screen
                    .PrimaryScreen?
                    .Bounds;

            if (bounds is null)
                return;

            using var screenshot =
                new DrawingBitmap(
                    bounds.Value.Width,
                    bounds.Value.Height,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       screenshot))
            {
                graphics.CopyFromScreen(
                    bounds.Value.Left,
                    bounds.Value.Top,
                    0,
                    0,
                    screenshot.Size,
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            int smallWidth =
                Math.Max(
                    1,
                    screenshot.Width / 4);

            int smallHeight =
                Math.Max(
                    1,
                    screenshot.Height / 4);

            using var small =
                new DrawingBitmap(
                    smallWidth,
                    smallHeight,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       small))
            {
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D
                        .InterpolationMode.HighQualityBilinear;

                graphics.DrawImage(
                    screenshot,
                    0,
                    0,
                    smallWidth,
                    smallHeight);
            }

            using var blurred =
                new DrawingBitmap(
                    screenshot.Width,
                    screenshot.Height,
                    DrawingPixelFormat.Format32bppArgb);

            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(
                       blurred))
            {
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D
                        .InterpolationMode.HighQualityBilinear;

                graphics.DrawImage(
                    small,
                    0,
                    0,
                    blurred.Width,
                    blurred.Height);
            }

            using var stream =
                new MemoryStream();

            blurred.Save(
                stream,
                System.Drawing.Imaging.ImageFormat.Png);

            stream.Position = 0;

            var image =
                new BitmapImage();

            image.BeginInit();

            image.CacheOption =
                BitmapCacheOption.OnLoad;

            image.StreamSource =
                stream;

            image.EndInit();

            image.Freeze();

            DesktopBackdrop.Source =
                image;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Pinned desktop backdrop refresh failed",
                ex);
        }
        finally
        {
            _isCapturingBackdrop = false;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(
                    () =>
                    {
                        try
                        {
                            UpdateAllAdaptiveColors();

                            Show();

                            /*
                             * Pinned backdrop refresh should appear
                             * immediately. It must NOT use the normal
                             * opening fade, which could cause a flash
                             * during Alt-Tab.
                             */
                            Dispatcher.BeginInvoke(
                                DispatcherPriority.Render,
                                new Action(
                                    () =>
                                    {
                                        Opacity = 1;

                                        _isRefreshingPinnedBackdrop =
                                            false;
                                    }));
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error(
                                "Pinned backdrop reveal failed",
                                ex);

                            Opacity = 1;

                            _isRefreshingPinnedBackdrop =
                                false;
                        }
                    }));
        }
    }

    private void UnblurDesktop()
    {
        _desktopUnblurred = true;

        DesktopBackdrop.Source =
            null;

        BackdropOverlay.Visibility =
            Visibility.Collapsed;

        UpdateAllAdaptiveColors();
    }

    /*
     * Returns every command that the user is currently
     * allowed to autocomplete.
     */
    private List<CommandSuggestion> GetEligibleCommands()
    {
        var commands =
            new Dictionary<string, CommandSuggestion>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["settings"] =
                    new CommandSuggestion
                    {
                        Command = "settings",
                        Description = "Opens Ballknower settings."
                    },

                ["logs"] =
                    new CommandSuggestion
                    {
                        Command = "logs",
                        Description = "Opens the Ballknower error logs."
                    },

                ["see"] =
                    new CommandSuggestion
                    {
                        Command = "see",
                        Description = "Shows the desktop without the blur."
                    },

                ["pin"] =
                    new CommandSuggestion
                    {
                        Command = "pin",
                        Description = "Keeps Ballknower visible when switching apps."
                    },

                ["unpin"] =
                    new CommandSuggestion
                    {
                        Command = "unpin",
                        Description = "Makes Ballknower hide when it loses focus."
                    }
            };

        foreach (var shortcut in
                 _settings.Shortcuts)
        {
            string command =
                (shortcut.Key ?? string.Empty)
                    .Trim()
                    .TrimStart('/');

            if (string.IsNullOrWhiteSpace(command))
                continue;

            string launchPath =
                shortcut.Value ?? string.Empty;

            string appName =
                Path.GetFileName(
                    launchPath);

            if (string.IsNullOrWhiteSpace(appName))
            {
                appName =
                    launchPath;
            }

            if (!appName.EndsWith(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                appName += ".exe";
            }

            commands[command] =
                new CommandSuggestion
                {
                    Command = command,

                    Description =
                        $"Opens {appName}"
                };
        }

        var result =
            new List<CommandSuggestion>(
                commands.Values);

        result.Sort(
            (a, b) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    a.Command,
                    b.Command));

        return result;
    }

    private void ChatInput_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isApplyingCommandSuggestion)
            return;

        UpdateCommandSuggestions();
    }

    private void UpdateCommandSuggestions()
    {
        string text =
            ChatInput.Text;

        string trimmed =
            text.TrimStart();

        /*
         * Suggestions only apply while the input is a
         * single command token.
         */
        if (!trimmed.StartsWith("/") ||
            trimmed.Length == 0)
        {
            HideCommandSuggestions();
            return;
        }

        if (trimmed.Contains(' ') ||
            trimmed.Contains('\t') ||
            trimmed.Contains('\r') ||
            trimmed.Contains('\n'))
        {
            HideCommandSuggestions();
            return;
        }

        string prefix =
            trimmed.Substring(1);

        var eligibleCommands =
            GetEligibleCommands();

        var matches =
            new List<CommandSuggestion>();

        foreach (var command in
                 eligibleCommands)
        {
            if (command.Command.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(command);
            }
        }

        if (matches.Count == 0)
        {
            HideCommandSuggestions();
            return;
        }

        CommandSuggestionList.ItemsSource =
            matches;

        CommandSuggestionList.SelectedIndex =
            0;

        CommandSuggestions.Visibility =
            Visibility.Visible;

        UpdateCommandSuggestionColors();
        UpdateCommandSuggestionPosition();
    }

    private void HideCommandSuggestions()
    {
        CommandSuggestions.Visibility =
            Visibility.Collapsed;

        CommandSuggestionList.ItemsSource =
            null;
    }

    private void UpdateCommandSuggestionPosition()
    {
        if (InputPill.ActualHeight <= 0 ||
            ContentRoot.ActualHeight <= 0)
        {
            return;
        }

        // Get the pill's actual top position in the window.
        double pillY =
            InputPill
                .TransformToVisual(ContentRoot)
                .Transform(new System.Windows.Point(0, 0))
                .Y;

        double suggestionHeight =
            CommandSuggestions.ActualHeight > 0
                ? CommandSuggestions.ActualHeight
                : Math.Min(
                    260,
                    Math.Max(
                        70,
                        GetEligibleCommands().Count * 58 + 16));

        // Keep the suggestions directly above the pill.
        CommandSuggestions.Margin =
            new Thickness(
                0,
                Math.Max(
                    0,
                    pillY - suggestionHeight - 12),
                0,
                0);
    }

    private void UpdateCommandSuggestionColors()
    {
        bool lightBackground =
            _inputIsLight;

        CommandSuggestions.Background =
            lightBackground
                ? _lightBrush
                : _darkBrush;

        CommandSuggestionList.Foreground =
            lightBackground
                ? _blackTextBrush
                : _whiteTextBrush;

        /*
         * Rebuild the item containers' foreground so the
         * adaptive color is applied even after virtualization.
         */
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(
                () =>
                {
                    foreach (var item in
                             CommandSuggestionList.Items)
                    {
                        if (CommandSuggestionList
                                .ItemContainerGenerator
                                .ContainerFromItem(item)
                            is ListBoxItem container)
                        {
                            container.Foreground =
                                lightBackground
                                    ? _blackTextBrush
                                    : _whiteTextBrush;
                        }
                    }
                }));
    }

    private void CommandSuggestionList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateCommandSuggestionColors();
    }

    private void CommandSuggestionList_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var item =
                ItemsControl.ContainerFromElement(
                    CommandSuggestionList,
                    source)
                as ListBoxItem;

            if (item is not null)
            {
                CommandSuggestionList.SelectedItem =
                    item.DataContext;

                ApplySelectedCommandSuggestion();

                e.Handled = true;
            }
        }
    }

    private void ApplySelectedCommandSuggestion()
    {
        if (CommandSuggestionList.SelectedItem
            is not CommandSuggestion suggestion)
        {
            return;
        }

        _isApplyingCommandSuggestion = true;

        try
        {
            ChatInput.Text =
                "/" + suggestion.Command;

            ChatInput.CaretIndex =
                ChatInput.Text.Length;
        }
        finally
        {
            _isApplyingCommandSuggestion = false;
        }

        HideCommandSuggestions();

        ChatInput.Focus();
    }

    private void UpdateAllAdaptiveColors()
    {
        UpdateInputPillColor();
        UpdateMessageAreaColor();
    }

    private void UpdateInputPillColor()
    {
        try
        {
            double x =
                (ContentRoot.ActualWidth -
                 InputPill.ActualWidth) / 2;

            double y =
                _inputPillTransform.Y;

            double width =
                InputPill.ActualWidth;

            double height =
                InputPill.ActualHeight;

            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            double luminance =
                GetAdaptiveLuminance(
                    x,
                    y,
                    width,
                    height);

            bool shouldBeLight =
                luminance < 0.50;

            _inputIsLight =
                shouldBeLight;

            InputPill.Background =
                shouldBeLight
                    ? _lightBrush
                    : _darkBrush;

            ChatInput.Foreground =
                shouldBeLight
                    ? _blackTextBrush
                    : _whiteTextBrush;

            ChatInput.CaretBrush =
                shouldBeLight
                    ? _blackTextBrush
                    : _whiteTextBrush;

            /*
             * Keep the autocomplete popup synchronized with
             * the pill's adaptive color.
             */
            if (CommandSuggestions.Visibility ==
                Visibility.Visible)
            {
                UpdateCommandSuggestionColors();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Input pill color update failed",
                ex);
        }
    }

    private void UpdateMessageAreaColor()
    {
        try
        {
            if (MessageArea.Visibility !=
                Visibility.Visible)
            {
                return;
            }

            double x =
                (ContentRoot.ActualWidth -
                 MessageArea.ActualWidth) / 2;

            double y =
                _messageAreaTransform.Y;

            double width =
                MessageArea.ActualWidth;

            double height =
                MessageArea.ActualHeight;

            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            double luminance =
                GetAdaptiveLuminance(
                    x,
                    y,
                    width,
                    height);

            bool shouldBeLight =
                luminance < 0.50;

            _messageAreaIsLight =
                shouldBeLight;

            MessageArea.Background =
                shouldBeLight
                    ? _lightBrush
                    : _darkBrush;

            UpdateMessageTextColors(
                shouldBeLight);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Message area color update failed",
                ex);
        }
    }

    private double GetAdaptiveLuminance(
        double x,
        double y,
        double width,
        double height)
    {
        if (_desktopUnblurred)
        {
            return SampleScreenAroundRegion(
                x,
                y,
                width,
                height);
        }

        return SampleBackdropAroundRegion(
            x,
            y,
            width,
            height);
    }

    private double SampleBackdropAroundRegion(
        double x,
        double y,
        double width,
        double height)
    {
        if (DesktopBackdrop.Source is not BitmapSource bitmap)
            return 0.0;

        const double sampleSize = 35;

        double scaleX =
            bitmap.PixelWidth /
            Math.Max(
                1.0,
                ContentRoot.ActualWidth);

        double scaleY =
            bitmap.PixelHeight /
            Math.Max(
                1.0,
                ContentRoot.ActualHeight);

        double total = 0;
        int regions = 0;

        total +=
            SampleBackdropRegion(
                bitmap,
                x - sampleSize,
                y,
                sampleSize,
                height,
                scaleX,
                scaleY);

        regions++;

        total +=
            SampleBackdropRegion(
                bitmap,
                x + width,
                y,
                sampleSize,
                height,
                scaleX,
                scaleY);

        regions++;

        total +=
            SampleBackdropRegion(
                bitmap,
                x,
                y - sampleSize,
                width,
                sampleSize,
                scaleX,
                scaleY);

        regions++;

        total +=
            SampleBackdropRegion(
                bitmap,
                x,
                y + height,
                width,
                sampleSize,
                scaleX,
                scaleY);

        regions++;

        return regions == 0
            ? 0.0
            : total / regions;
    }

    private double SampleBackdropRegion(
        BitmapSource bitmap,
        double x,
        double y,
        double width,
        double height,
        double scaleX,
        double scaleY)
    {
        double rootWidth =
            Math.Max(
                1.0,
                ContentRoot.ActualWidth);

        double rootHeight =
            Math.Max(
                1.0,
                ContentRoot.ActualHeight);

        double left =
            Math.Max(
                0,
                x);

        double top =
            Math.Max(
                0,
                y);

        double right =
            Math.Min(
                rootWidth,
                x + width);

        double bottom =
            Math.Min(
                rootHeight,
                y + height);

        if (right <= left ||
            bottom <= top)
        {
            return 0.0;
        }

        int pixelLeft =
            Math.Clamp(
                (int)(left * scaleX),
                0,
                bitmap.PixelWidth - 1);

        int pixelTop =
            Math.Clamp(
                (int)(top * scaleY),
                0,
                bitmap.PixelHeight - 1);

        int pixelRight =
            Math.Clamp(
                (int)(right * scaleX),
                pixelLeft + 1,
                bitmap.PixelWidth);

        int pixelBottom =
            Math.Clamp(
                (int)(bottom * scaleY),
                pixelTop + 1,
                bitmap.PixelHeight);

        int sampleWidth =
            Math.Max(
                1,
                pixelRight - pixelLeft);

        int sampleHeight =
            Math.Max(
                1,
                pixelBottom - pixelTop);

        var pixels =
            new byte[
                sampleWidth *
                sampleHeight *
                4];

        bitmap.CopyPixels(
            new Int32Rect(
                pixelLeft,
                pixelTop,
                sampleWidth,
                sampleHeight),
            pixels,
            sampleWidth * 4,
            0);

        double total = 0;
        int samples = 0;

        int stepX =
            Math.Max(
                1,
                sampleWidth / 10);

        int stepY =
            Math.Max(
                1,
                sampleHeight / 10);

        for (
            int py = 0;
            py < sampleHeight;
            py += stepY)
        {
            for (
                int px = 0;
                px < sampleWidth;
                px += stepX)
            {
                int index =
                    (py * sampleWidth + px) * 4;

                byte blue =
                    pixels[index];

                byte green =
                    pixels[index + 1];

                byte red =
                    pixels[index + 2];

                total +=
                    (0.2126 * red +
                     0.7152 * green +
                     0.0722 * blue) /
                    255.0;

                samples++;
            }
        }

        return samples == 0
            ? 0.0
            : total / samples;
    }

    private double SampleScreenAroundRegion(
        double x,
        double y,
        double width,
        double height)
    {
        try
        {
            var screen =
                System.Windows.Forms.Screen
                    .PrimaryScreen;

            if (screen is null)
                return 0.0;

            const double sampleSize = 35;

            double total = 0;
            int regions = 0;

            total +=
                SampleScreenRegion(
                    screen.Bounds,
                    x - sampleSize,
                    y,
                    sampleSize,
                    height);

            regions++;

            total +=
                SampleScreenRegion(
                    screen.Bounds,
                    x + width,
                    y,
                    sampleSize,
                    height);

            regions++;

            total +=
                SampleScreenRegion(
                    screen.Bounds,
                    x,
                    y - sampleSize,
                    width,
                    sampleSize);

            regions++;

            total +=
                SampleScreenRegion(
                    screen.Bounds,
                    x,
                    y + height,
                    width,
                    sampleSize);

            regions++;

            return regions == 0
                ? 0.0
                : total / regions;
        }
        catch
        {
            return 0.0;
        }
    }

    private double SampleScreenRegion(
        System.Drawing.Rectangle screenBounds,
        double x,
        double y,
        double width,
        double height)
    {
        int relativeLeft =
            Math.Max(
                0,
                (int)x);

        int relativeTop =
            Math.Max(
                0,
                (int)y);

        if (relativeLeft >=
            screenBounds.Width ||
            relativeTop >=
            screenBounds.Height)
        {
            return 0.0;
        }

        int sampleWidth =
            Math.Min(
                Math.Max(
                    1,
                    (int)width),
                screenBounds.Width -
                relativeLeft);

        int sampleHeight =
            Math.Min(
                Math.Max(
                    1,
                    (int)height),
                screenBounds.Height -
                relativeTop);

        if (sampleWidth <= 0 ||
            sampleHeight <= 0)
        {
            return 0.0;
        }

        int left =
            screenBounds.Left +
            relativeLeft;

        int top =
            screenBounds.Top +
            relativeTop;

        using var bitmap =
            new DrawingBitmap(
                sampleWidth,
                sampleHeight,
                DrawingPixelFormat.Format32bppArgb);

        using (DrawingGraphics graphics =
               DrawingGraphics.FromImage(
                   bitmap))
        {
            graphics.CopyFromScreen(
                left,
                top,
                0,
                0,
                bitmap.Size,
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        double total = 0;
        int samples = 0;

        int stepX =
            Math.Max(
                1,
                sampleWidth / 10);

        int stepY =
            Math.Max(
                1,
                sampleHeight / 10);

        for (
            int py = 0;
            py < sampleHeight;
            py += stepY)
        {
            for (
                int px = 0;
                px < sampleWidth;
                px += stepX)
            {
                DrawingColor pixel =
                    bitmap.GetPixel(
                        px,
                        py);

                total +=
                    (0.2126 * pixel.R +
                     0.7152 * pixel.G +
                     0.0722 * pixel.B) /
                    255.0;

                samples++;
            }
        }

        return samples == 0
            ? 0.0
            : total / samples;
    }

    private void UpdateMessageTextColors(
        bool lightBackground)
    {
        foreach (var child in
                 MessagePanel.Children)
        {
            if (child is TextBlock textBlock)
            {
                textBlock.Foreground =
                    lightBackground
                        ? _blackTextBrush
                        : _whiteTextBrush;
            }
        }
    }

    private void MainWindow_SizeChanged(
        object sender,
        SizeChangedEventArgs args)
    {
        UpdateLayoutPositions();
    }

    private void UpdateLayoutPositions()
    {
        double height =
            ContentRoot.ActualHeight;

        if (height <= 0)
            return;

        double targetPosition =
            _hasEnteredChat
                ? height * ChatPillPosition
                : height * InitialPillPosition;

        if (_pillStoryboard is null)
        {
            _inputPillTransform.Y =
                targetPosition;
        }

        UpdateMessageAreaPosition();
        UpdateCommandSuggestionPosition();
        UpdateAllAdaptiveColors();
    }

    private void UpdateMessageAreaPosition()
    {
        double height =
            ContentRoot.ActualHeight;

        if (height <= 0)
            return;

        double pillY =
            height * ChatPillPosition;

        double availableHeight =
            pillY -
            MessageGap -
            16;

        double messageHeight =
            Math.Max(
                0,
                Math.Min(
                    400,
                    availableHeight));

        MessageArea.Height =
            messageHeight;

        double messageY =
            pillY -
            messageHeight -
            MessageGap;

        _messageAreaTransform.Y =
            Math.Max(
                0,
                messageY);
    }

    private void AnimateInputPillDown()
    {
        double height =
            ContentRoot.ActualHeight;

        if (height <= 0)
            return;

        double targetY =
            height * ChatPillPosition;

        double startingY =
            _inputPillTransform.Y;

        if (_hasEnteredChat)
        {
            _inputPillTransform.Y =
                targetY;

            UpdateMessageAreaPosition();
            UpdateCommandSuggestionPosition();
            UpdateAllAdaptiveColors();

            return;
        }

        _hasEnteredChat = true;

        _pillStoryboard?.Stop();

        _pillStoryboard = null;

        var animation =
            new DoubleAnimation
            {
                From =
                    startingY,

                To =
                    targetY,

                Duration =
                    TimeSpan.FromMilliseconds(
                        PillAnimationMilliseconds),

                FillBehavior =
                    FillBehavior.Stop,

                EasingFunction =
                    new ExponentialEase
                    {
                        Exponent = 4,

                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };

        animation.CurrentTimeInvalidated +=
            (_, _) =>
            {
                UpdateCommandSuggestionPosition();
                UpdateAllAdaptiveColors();
            };

        animation.Completed +=
            (_, _) =>
            {
                _inputPillTransform.Y =
                    targetY;

                UpdateMessageAreaPosition();
                UpdateCommandSuggestionPosition();
                UpdateAllAdaptiveColors();

                _pillStoryboard = null;
            };

        _pillStoryboard =
            new Storyboard();

        _pillStoryboard.Children.Add(
            animation);

        Storyboard.SetTarget(
            animation,
            _inputPillTransform);

        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(
                TranslateTransform.YProperty));

        _pillStoryboard.Begin();
    }

    private void MainWindow_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        /*
         * Escape first dismisses autocomplete.
         * A second Escape closes Ballknower.
         */
        if (CommandSuggestions.Visibility ==
            Visibility.Visible)
        {
            HideCommandSuggestions();

            e.Handled = true;
            return;
        }

        Close();

        e.Handled = true;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        Topmost = false;

        _settingsWindow =
            new SettingsWindow(
                _settings);

        _settingsWindow.Owner =
            this;

        _settingsWindow.Topmost =
            true;

        _settingsWindow.Closed +=
            (_, _) =>
            {
                _settingsWindow = null;

                Topmost = true;

                Activate();

                ChatInput.Focus();
            };

        _settingsWindow.Show();

        _settingsWindow.Activate();
    }

    private async void ChatInput_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        /*
         * Autocomplete keyboard navigation.
         */
        if (CommandSuggestions.Visibility ==
            Visibility.Visible)
        {
            if (e.Key == Key.Down)
            {
                if (CommandSuggestionList.Items.Count > 0)
                {
                    int nextIndex =
                        Math.Min(
                            CommandSuggestionList.SelectedIndex + 1,
                            CommandSuggestionList.Items.Count - 1);

                    CommandSuggestionList.SelectedIndex =
                        nextIndex;

                    CommandSuggestionList.ScrollIntoView(
                        CommandSuggestionList.SelectedItem);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                if (CommandSuggestionList.Items.Count > 0)
                {
                    int previousIndex =
                        Math.Max(
                            CommandSuggestionList.SelectedIndex - 1,
                            0);

                    CommandSuggestionList.SelectedIndex =
                        previousIndex;

                    CommandSuggestionList.ScrollIntoView(
                        CommandSuggestionList.SelectedItem);
                }

                e.Handled = true;
                return;
            }

            /*
             * Tab accepts the selected suggestion.
             */
            if (e.Key == Key.Tab)
            {
                ApplySelectedCommandSuggestion();

                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_isProcessing)
            return;

        string message =
            ChatInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
            return;

        HideCommandSuggestions();

        ChatInput.Clear();

        _isProcessing = true;

        ChatInput.IsEnabled = false;

        try
        {
            if (_commandParser.IsCommand(message))
            {
                ExecuteShortcut(message);

                return;
            }

            if (!_hasEnteredChat)
            {
                AnimateInputPillDown();
            }

            MessageArea.Visibility =
                Visibility.Visible;

            UpdateMessageAreaPosition();
            UpdateAllAdaptiveColors();

            AddUserMessage(message);

            string? apiKey =
                _credentialStore.GetApiKey(
                    _settings.AIProvider);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AddAssistantMessage(
                    $"{_settings.AIProvider} API key not found.");

                return;
            }

            IAiClient client =
                _settings.AIProvider ==
                "OpenRouter"
                    ? new OpenRouterClient(
                        apiKey,
                        _toolRegistry)
                    : new GroqClient(
                        apiKey,
                        _toolRegistry);

            int conversationStart =
                _conversation.Count;

            _conversation.Add(
                new OpenRouterMessage
                {
                    Role = "user",
                    Content = message
                });

            try
            {
                await RunNativeToolLoopAsync(
                    client);
            }
            catch
            {
                if (_conversation.Count ==
                    conversationStart + 1)
                {
                    _conversation.RemoveAt(
                        conversationStart);
                }

                throw;
            }
        }
        catch (AiProviderException ex)
        {
            AppLogger.Error(
                $"{ex.Provider} HTTP {ex.StatusCode}",
                ex);

            AddAssistantMessage(
                ex.Message);
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Error(
                "AI network request failed",
                ex);

            AddAssistantMessage(
                "Could not connect to the AI provider. " +
                "Check your internet connection and try again.");
        }
        catch (TaskCanceledException ex)
        {
            AppLogger.Error(
                "AI request timed out or was canceled",
                ex);

            AddAssistantMessage(
                "The AI request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Chat request failed",
                ex);

            AddAssistantMessage(
                "Something went wrong. Details were saved " +
                "to the Ballknower error log.");
        }
        finally
        {
            _isProcessing = false;

            ChatInput.IsEnabled = true;

            if (_settingsWindow is null)
                ChatInput.Focus();
        }
    }

    private void ExecuteShortcut(
        string message)
    {
        var parsed =
            _commandParser.Parse(message);

        switch (parsed.Command)
        {
            case "settings":

                OpenSettings();

                return;

            case "logs":

                OpenLogs();

                return;

            case "see":

                UnblurDesktop();

                return;

            case "pin":

                _isPinned = true;
                Topmost = false;

                AddAssistantMessage(
                    "Pinned. Ballknower will stay open while you switch to another app.");

                return;

            case "unpin":

                _isPinned = false;
                Topmost = true;

                AddAssistantMessage(
                    "Unpinned. Ballknower will hide when it loses focus.");

                return;
        }

        if (!_settings.Shortcuts.TryGetValue(
                parsed.Command,
                out var launchPath))
        {
            AddAssistantMessage(
                $"No shortcut configured for /{parsed.Command}");

            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = launchPath,
                    UseShellExecute = true
                });

            /*
             * If the user command is being executed before
             * an AI chat has been opened, Ballknower closes
             * after successfully launching the application.
             */
            if (!_hasEnteredChat)
            {
                _allowClose = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Shortcut /{parsed.Command} failed",
                ex);

            AddAssistantMessage(
                $"Could not launch /{parsed.Command}. " +
                "Details were saved to the error log.");
        }
    }

    private void OpenLogs()
    {
        try
        {
            string logDirectory =
                AppLogger.LogDirectory;

            Directory.CreateDirectory(
                logDirectory);

            AddAssistantMessage(
                $"Logs: {logDirectory}");

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "explorer.exe",

                    Arguments =
                        $"\"{logDirectory}\"",

                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Could not open the log directory",
                ex);

            AddAssistantMessage(
                "Could not open the Ballknower log directory.");
        }
    }

    private async Task RunNativeToolLoopAsync(
        IAiClient client)
    {
        const int maxToolCalls = 5;

        int toolCallsExecuted = 0;

        var validator =
            new ToolValidator(
                _toolRegistry);

        var executor =
            new ToolExecutor(
                _toolRegistry);

        while (true)
        {
            string model =
                _settings.AIProvider ==
                "OpenRouter"
                    ? _settings.OpenRouterModel
                    : _settings.GroqModel;

            var requestMessages =
                ConversationHistoryManager.BuildRequest(
                    _conversation,
                    _settings.HistoryTokenBudget);

            var response =
                await client.SendWithToolsAsync(
                    model,
                    requestMessages);

            var assistantMessage =
                new OpenRouterMessage
                {
                    Role = "assistant",
                    Content = response.Content,
                    ToolCalls = response.ToolCalls
                };

            _conversation.Add(
                assistantMessage);

            if (response.ToolCalls is null ||
                response.ToolCalls.Count == 0)
            {
                AddAssistantMessage(
                    string.IsNullOrWhiteSpace(
                        response.Content)
                        ? "The model returned an empty response."
                        : response.Content);

                return;
            }

            bool limitReached = false;

            foreach (var call in response.ToolCalls)
            {
                ToolResult result;

                if (toolCallsExecuted >=
                    maxToolCalls)
                {
                    limitReached = true;

                    result =
                        new ToolResult
                        {
                            Tool =
                                call.Function.Name,

                            Success = false,

                            Message =
                                "Maximum number of tool calls " +
                                "reached for this request."
                        };
                }
                else
                {
                    toolCallsExecuted++;

                    result =
                        await ExecuteNativeToolCallAsync(
                            call,
                            validator,
                            executor);
                }

                _conversation.Add(
                    new OpenRouterMessage
                    {
                        Role = "tool",

                        ToolCallId =
                            call.Id,

                        Content =
                            JsonSerializer.Serialize(
                                new
                                {
                                    tool = result.Tool,
                                    success = result.Success,
                                    message = result.Message
                                })
                    });
            }

            if (limitReached ||
                toolCallsExecuted >=
                maxToolCalls)
            {
                AddAssistantMessage(
                    "Reached the tool-call limit. " +
                    "The last tool results have been " +
                    "recorded in this conversation.");

                return;
            }
        }
    }

    private async Task<ToolResult>
        ExecuteNativeToolCallAsync(
            OpenRouterToolCall call,
            ToolValidator validator,
            ToolExecutor executor)
    {
        string toolName =
            call.Function.Name;

        if (call.Type != "function")
        {
            return new ToolResult
            {
                Tool = toolName,

                Success = false,

                Message =
                    "Unsupported tool-call type."
            };
        }

        Dictionary<string, string>?
            arguments;

        try
        {
            arguments =
                JsonSerializer.Deserialize<
                    Dictionary<string, string>>(
                    call.Function.Arguments);
        }
        catch (JsonException)
        {
            return new ToolResult
            {
                Tool = toolName,

                Success = false,

                Message =
                    "The model supplied invalid tool arguments."
            };
        }

        var request =
            new ToolRequest
            {
                Tool = toolName,

                Arguments =
                    arguments ??
                    new Dictionary<string, string>()
            };

        if (!validator.TryValidate(
                request,
                out var validationError))
        {
            return new ToolResult
            {
                Tool = toolName,

                Success = false,

                Message =
                    validationError
            };
        }

        try
        {
            NormalizeToolArguments(
                request);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Path normalization failed for {toolName}",
                ex);

            return new ToolResult
            {
                Tool = toolName,

                Success = false,

                Message =
                    "Invalid file path: " +
                    ex.Message
            };
        }

        return await executor.ExecuteAsync(
            request.Tool,
            request.Arguments);
    }

    private void NormalizeToolArguments(
        ToolRequest request)
    {
        foreach (string argumentName in
                 new[]
                 {
                     "path",
                     "source",
                     "destination"
                 })
        {
            NormalizePathArgument(
                request,
                argumentName);
        }
    }

    private void NormalizePathArgument(
        ToolRequest request,
        string argumentName)
    {
        if (!request.Arguments.TryGetValue(
                argumentName,
                out var path))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
            return;

        path =
            path.Replace(
                '/',
                '\\');

        if (path == "~")
        {
            path =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
        }
        else if (path.StartsWith("~\\"))
        {
            string relativePath =
                path.Substring(2);

            int separatorIndex =
                relativePath.IndexOf('\\');

            string folderName =
                separatorIndex >= 0
                    ? relativePath.Substring(
                        0,
                        separatorIndex)
                    : relativePath;

            string remainder =
                separatorIndex >= 0
                    ? relativePath.Substring(
                        separatorIndex + 1)
                    : string.Empty;

            string baseFolder;

            switch (folderName.ToLowerInvariant())
            {
                case "desktop":

                    baseFolder =
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.DesktopDirectory);

                    break;

                case "documents":

                    baseFolder =
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.MyDocuments);

                    break;

                case "downloads":

                    baseFolder =
                        GetDownloadsFolder();

                    break;

                default:

                    baseFolder =
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile);

                    remainder =
                        relativePath;

                    break;
            }

            if (string.IsNullOrWhiteSpace(
                    baseFolder))
            {
                throw new DirectoryNotFoundException(
                    $"Could not locate the {folderName} folder.");
            }

            path =
                string.IsNullOrEmpty(
                    remainder)
                    ? baseFolder
                    : Path.Combine(
                        baseFolder,
                        remainder);
        }

        request.Arguments[argumentName] =
            Path.GetFullPath(path);
    }

    private static string GetDownloadsFolder()
    {
        var folderId =
            new Guid(
                "374DE290-123F-4565-9164-39C4925E467B");

        int result =
            SHGetKnownFolderPath(
                ref folderId,
                0,
                IntPtr.Zero,
                out var pathPointer);

        if (result != 0)
        {
            throw new DirectoryNotFoundException(
                "Windows could not locate the Downloads folder.");
        }

        try
        {
            return Marshal.PtrToStringUni(
                       pathPointer)
                   ?? throw new DirectoryNotFoundException(
                       "Windows returned an empty Downloads path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(
                pathPointer);
        }
    }

    private void AddUserMessage(
        string message)
    {
        var text =
            new TextBlock
            {
                Text =
                    "You: " + message,

                FontSize = 18,

                Foreground =
                    _messageAreaIsLight
                        ? _blackTextBrush
                        : _whiteTextBrush,

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12)
            };

        MessagePanel.Children.Add(
            text);

        UpdateMessageAreaColor();
    }

    private void AddAssistantMessage(
        string message)
    {
        var text =
            new TextBlock
            {
                Text =
                    "Ballknower: " + message,

                FontSize = 18,

                Foreground =
                    _messageAreaIsLight
                        ? _blackTextBrush
                        : _whiteTextBrush,

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12)
            };

        MessagePanel.Children.Add(
            text);

        UpdateMessageAreaColor();
    }

    private sealed class CommandSuggestion
    {
        public string Command { get; init; } =
            string.Empty;

        public string Description { get; init; } =
            string.Empty;
    }
}