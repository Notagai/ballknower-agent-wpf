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
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Ballknower;

public partial class MainWindow : Window
{
    private const double InitialPillPosition = 0.30;
    private const double ChatPillPosition = 0.65;
    private const double MessageGap = 16;
    private const double PillAnimationMilliseconds = 600;

    private readonly AppSettings _settings;
    private readonly List<OpenRouterMessage> _conversation;
    private readonly CommandParser _commandParser;
    private readonly ToolRegistry _toolRegistry;
    private readonly CredentialStore _credentialStore;

    private readonly TranslateTransform _inputPillTransform;
    private readonly TranslateTransform _messageAreaTransform;

    private SettingsWindow? _settingsWindow;
    private Storyboard? _pillStoryboard;

    private bool _isProcessing;
    private bool _hasEnteredChat;

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    public MainWindow()
    {
        InitializeComponent();

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

        Closed +=
            MainWindow_Closed;

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

        _backdropTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        BackdropRefreshMilliseconds)
            };

        _backdropTimer.Tick +=
            (_, _) => UpdateDesktopBackdrop();

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

    private void UpdateDesktopBackdrop()
    {
        if (DesktopBackdrop.Source is not null)
            return;

        try
        {
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
                Math.Max(1, screenshot.Width / 4);

            int smallHeight =
                Math.Max(1, screenshot.Height / 4);

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
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            DesktopBackdrop.Source = image;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Desktop backdrop capture failed",
                ex);
        }
        finally
        {
            Show();
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

        _pillStoryboard?.Stop();

        _pillStoryboard = null;

        _inputPillTransform.Y =
            height *
            (_hasEnteredChat
                ? ChatPillPosition
                : InitialPillPosition);

        UpdateMessageAreaPosition();
    }

    private void UpdateMessageAreaPosition()
    {
        double height =
            ContentRoot.ActualHeight;

        if (height <= 0)
            return;

        double pillY =
            height *
            ChatPillPosition;

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
            height *
            ChatPillPosition;

        if (_hasEnteredChat)
        {
            _inputPillTransform.Y =
                targetY;

            UpdateMessageAreaPosition();

            return;
        }

        double startingY =
            _inputPillTransform.Y;

        _hasEnteredChat = true;

        UpdateMessageAreaPosition();

        _pillStoryboard?.Stop();

        var animation =
            new DoubleAnimation
            {
                From = startingY,
                To = targetY,
                Duration =
                    TimeSpan.FromMilliseconds(
                        PillAnimationMilliseconds),

                EasingFunction =
                    new ExponentialEase
                    {
                        Exponent = 4,
                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };

        Storyboard.SetTarget(
            animation,
            _inputPillTransform);

        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(
                TranslateTransform.YProperty));

        _pillStoryboard =
            new Storyboard();

        _pillStoryboard.Children.Add(
            animation);

        _pillStoryboard.Completed +=
            (_, _) =>
            {
                _inputPillTransform.Y =
                    targetY;

                _pillStoryboard = null;
            };

        _pillStoryboard.Begin();
    }

    private void MainWindow_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

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

        /*
         * Let the settings window sit above Ballknower
         * without closing the main overlay.
         */
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

                UpdateDesktopBackdrop();
            };

        _settingsWindow.Show();

        _settingsWindow.Activate();
    }

    private async void ChatInput_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_isProcessing)
            return;

        string message =
            ChatInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
            return;

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

            AnimateInputPillDown();

            UpdateMessageAreaPosition();

            MessageArea.Visibility =
                Visibility.Visible;

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
        MessagePanel.Children.Add(
            new TextBlock
            {
                Text =
                    "You: " + message,

                FontSize = 18,

                Foreground =
                    System.Windows.Media.Brushes.White,

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12)
            });
    }

    private void AddAssistantMessage(
        string message)
    {
        MessagePanel.Children.Add(
            new TextBlock
            {
                Text =
                    "Ballknower: " + message,

                FontSize = 18,

                Foreground =
                    System.Windows.Media.Brushes.White,

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12)
            });
    }
}