using Ballknower.Config;
using Ballknower.Google;
using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfBrushes = System.Windows.Media.Brushes;

namespace Ballknower;

public partial class SettingsWindow : Window
{
    private static readonly int[] HistoryBudgets =
    {
        1000,
        2000,
        4000,
        8000,
        16000
    };

    private readonly AppSettings _targetSettings;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly Dictionary<string, string> _apiKeys;
    private readonly GoogleDriveService _googleDriveService;

    private string? _editingCommand;
    private bool _isInitializing;
    private bool _isDirty;
    private bool _allowClose;
    private static readonly HttpClient TestHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    public SettingsWindow(AppSettings settings, GoogleDriveService googleDriveService)
    {
        InitializeComponent();

        _isInitializing = true;

        _targetSettings = settings;
        _googleDriveService = googleDriveService;

        _settings = new AppSettings
        {
            AIProvider = settings.AIProvider,
            OpenRouterModel = settings.OpenRouterModel,
            GroqModel = settings.GroqModel,
            OpenAIModel = settings.OpenAIModel,
            GeminiModel = settings.GeminiModel,
            StreamResponses = settings.StreamResponses,
            JailbreakEnabled = settings.JailbreakEnabled,
            JailbreakPrompt = settings.JailbreakPrompt,
            OpeningShortcut = settings.OpeningShortcut,
            HistoryTokenBudget = settings.HistoryTokenBudget,
            Shortcuts = new Dictionary<string, string>(
                settings.Shortcuts),
            StylePreset = settings.StylePreset,
            StyleThemeMode = settings.StyleThemeMode,
            LightThemeBackground = settings.LightThemeBackground,
            LightThemeForeground = settings.LightThemeForeground,
            DarkThemeBackground = settings.DarkThemeBackground,
            DarkThemeForeground = settings.DarkThemeForeground,
            StyleFontFamily = settings.StyleFontFamily,
            StyleAnimatedEffects = settings.StyleAnimatedEffects,
            StyleRainbowBorder = settings.StyleRainbowBorder,
            StyleGlowEffect = settings.StyleGlowEffect
        };

        _settingsStore = new SettingsStore();
        _credentialStore = new CredentialStore();
        _apiKeys = new Dictionary<string, string>();

        LoadApiKeys();

        ProviderInput.SelectedValue =
            _settings.AIProvider;

        OpeningShortcutInput.SelectedValue =
            NormalizeOpeningShortcut(
                _settings.OpeningShortcut);

        UpdateProviderUI();

        StreamingCheckBox.IsChecked =
            _settings.StreamResponses;

        JailbreakCheckBox.IsChecked = _settings.JailbreakEnabled;
        JailbreakPromptInput.Text = _settings.JailbreakPrompt;
        JailbreakPromptPanel.Visibility = _settings.JailbreakEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateHistorySlider();
        RefreshShortcutList();
        RefreshGoogleDriveStatus();
        LoadStyleControls();

        _isInitializing = false;
    }

    private static string NormalizeOpeningShortcut(string shortcut)
    {
        return shortcut is "Ctrl+Win" or "Shift+Win"
            ? shortcut
            : "Alt+Win";
    }

    private void OpeningShortcutInput_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isInitializing ||
            OpeningShortcutInput.SelectedValue is not string shortcut)
            return;

        _settings.OpeningShortcut =
            NormalizeOpeningShortcut(shortcut);
        MarkDirty();
    }

    private void SettingsChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void JailbreakCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (JailbreakPromptPanel is not null)
            JailbreakPromptPanel.Visibility = JailbreakCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        MarkDirty();
    }
    private void SettingsChanged(object sender, RoutedEventArgs e, bool unused) => MarkDirty();
    private void MarkDirty()
    {
        if (_isInitializing) return;
        _isDirty = true;
        if (UnsavedChangesText is not null) UnsavedChangesText.Visibility = Visibility.Visible;
        KeyTestStatus.Text = "○ Not tested"; PromptTestStatus.Text = "○ Not tested";
    }

    private async void FetchGeminiModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            PromptTestStatus.Text = "Enter your Gemini API key first.";
            return;
        }

        FetchGeminiModelsButton.IsEnabled = false;
        FetchGeminiModelsButton.Content = "Fetching…";
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(key)}";
            var models = new List<string>();
            string? pageToken = null;
            do
            {
                var pageUrl = url + (string.IsNullOrWhiteSpace(pageToken) ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
                using var response = await TestHttp.GetAsync(pageUrl);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Google API returned {(int)response.StatusCode}: {body}");

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("models", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var supportsGenerate = item.TryGetProperty("supportedGenerationMethods", out var methods) &&
                            methods.ValueKind == JsonValueKind.Array &&
                            System.Linq.Enumerable.Any(methods.EnumerateArray(), m => m.GetString() == "generateContent");
                        if (supportsGenerate && item.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                                models.Add(name.StartsWith("models/", StringComparison.Ordinal) ? name.Substring("models/".Length) : name);
                        }
                    }
                }
                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
            } while (!string.IsNullOrWhiteSpace(pageToken));

            models = models.Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            if (models.Count == 0)
            {
                WpfMessageBox.Show(this, "Google returned no models supporting generateContent for this key.", "Gemini Models", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            var picker = new Window
            {
                Title = "Choose a Gemini Model",
                Owner = this,
                Width = 460,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 24, 24)),
                Foreground = WpfBrushes.White
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var combo = new System.Windows.Controls.ComboBox { ItemsSource = models, SelectedItem = models.Contains(ModelInput.Text.Trim()) ? ModelInput.Text.Trim() : models[0], Margin = new Thickness(0, 0, 0, 14), MinHeight = 28 };
            panel.Children.Add(new TextBlock { Text = "Models available to this API key:", Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(combo);
            var choose = new WpfButton { Content = "Use Selected Model", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(14, 5, 14, 5), IsDefault = true };
            choose.Click += (_, _) => picker.DialogResult = true;
            panel.Children.Add(choose);
            picker.Content = panel;
            if (picker.ShowDialog() == true && combo.SelectedItem is string selected)
            {
                ModelInput.Text = selected;
                SaveCurrentModel();
                MarkDirty();
                PromptTestStatus.Text = "Model selected — click Test Prompt to verify it.";
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, "Could not fetch Gemini models.\n\n" + ex.Message, "Gemini Models", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            FetchGeminiModelsButton.IsEnabled = true;
            FetchGeminiModelsButton.Content = "Fetch Available Gemini Models";
        }
    }

    private async void TestKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { KeyTestStatus.Text = "✗ Enter an API key"; return; }
        KeyTestStatus.Text = "Testing…";
        try
        {
            var endpoint = _settings.AIProvider switch
            {
                "Groq" => "https://api.groq.com/openai/v1/models",
                "OpenAI" => "https://api.openai.com/v1/models",
                "Gemini" => $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(key)}",
                _ => "https://openrouter.ai/api/v1/auth/key"
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (_settings.AIProvider != "Gemini") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await TestHttp.SendAsync(request);
            KeyTestStatus.Text = response.IsSuccessStatusCode ? "✓ Key accepted" : $"✗ Rejected ({(int)response.StatusCode})";
        }
        catch (Exception ex) { KeyTestStatus.Text = "✗ " + (ex is TaskCanceledException ? "Timed out" : "Connection failed"); }
    }

    private async void TestPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Password.Trim(); var model = ModelInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model)) { PromptTestStatus.Text = "✗ Enter a key and model"; return; }
        PromptTestStatus.Text = "Testing…";
        try
        {
            var endpoint = _settings.AIProvider switch
            {
                "Groq" => "https://api.groq.com/openai/v1/chat/completions",
                "OpenAI" => "https://api.openai.com/v1/chat/completions",
                "Gemini" => $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(key)}",
                _ => "https://openrouter.ai/api/v1/chat/completions"
            };
            var body = _settings.AIProvider == "Gemini"
                ? JsonSerializer.Serialize(new { contents = new[] { new { parts = new[] { new { text = "Reply with exactly: Ballknower test OK" } } } }, generationConfig = new { maxOutputTokens = 20 } })
                : JsonSerializer.Serialize(new { model, messages = new[] { new { role = "user", content = "Reply with exactly: Ballknower test OK" } }, max_tokens = 20, stream = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (_settings.AIProvider != "Gemini") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (_settings.AIProvider == "OpenRouter") request.Headers.Add("X-Title", "Ballknower");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await TestHttp.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            PromptTestStatus.Text = response.IsSuccessStatusCode ? "✓ Prompt succeeded" : $"✗ Failed ({(int)response.StatusCode}): {responseBody}";
        }
        catch (Exception ex) { PromptTestStatus.Text = "✗ " + (ex is TaskCanceledException ? "Timed out" : "Connection failed"); }
    }

    private void TestConversationButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentHistoryBudget();
        ConversationTestStatus.Text = _settings.HistoryTokenBudget > 0 ? "✓ Conversation settings valid" : "✗ Invalid history budget";
    }

    private void TestShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandInput.Text.Trim().TrimStart('/'); var path = PathInput.Text.Trim();
        ShortcutTestStatus.Text = !string.IsNullOrWhiteSpace(command) && !command.Contains(' ') && File.Exists(path) ? "✓ Command and executable are valid" : "✗ Enter a command and an existing executable path";
    }

    private void TestDataButton_Click(object sender, RoutedEventArgs e)
    {
        try { SaveCurrentModel(); SaveCurrentHistoryBudget(); _settings.StreamResponses = StreamingCheckBox.IsChecked == true; var json = JsonSerializer.Serialize(_settings); var copy = JsonSerializer.Deserialize<AppSettings>(json); DataTestStatus.Text = copy is not null && copy.AIProvider == _settings.AIProvider ? "✓ Settings serialize and reload" : "✗ Settings round-trip failed"; }
        catch { DataTestStatus.Text = "✗ Settings could not be serialized"; }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || !_isDirty) return;
        var result = WpfMessageBox.Show(this, "You have unsaved changes. Discard them and close Settings?", "Unsaved Changes", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) e.Cancel = true;
    }

    private static int NormalizeHistoryBudget(int value)
    {
        int closest = HistoryBudgets[0];

        foreach (int budget in HistoryBudgets)
        {
            if (Math.Abs(budget - value) <
                Math.Abs(closest - value))
            {
                closest = budget;
            }
        }

        return closest;
    }

    private void UpdateHistorySlider()
    {
        _settings.HistoryTokenBudget =
            NormalizeHistoryBudget(
                _settings.HistoryTokenBudget);

        int index = Array.IndexOf(
            HistoryBudgets,
            _settings.HistoryTokenBudget);

        if (index < 0)
            index = 1;

        HistoryBudgetSlider.Value = index;

        HistoryBudgetLabel.Text =
            $"{_settings.HistoryTokenBudget:N0} tokens";
    }

    private void HistoryBudgetSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing ||
            HistoryBudgetLabel is null)
        {
            return;
        }

        int index = Math.Clamp(
            (int)Math.Round(e.NewValue),
            0,
            HistoryBudgets.Length - 1);

        _settings.HistoryTokenBudget =
            HistoryBudgets[index];
        MarkDirty();

        HistoryBudgetLabel.Text =
            $"{_settings.HistoryTokenBudget:N0} tokens";
    }

    private void SaveCurrentHistoryBudget()
    {
        int index = Math.Clamp(
            (int)Math.Round(
                HistoryBudgetSlider.Value),
            0,
            HistoryBudgets.Length - 1);

        _settings.HistoryTokenBudget =
            HistoryBudgets[index];
    }

    private void LoadApiKeys()
    {
        LoadApiKey("Groq");
        LoadApiKey("OpenRouter");
        LoadApiKey("OpenAI");
        LoadApiKey("Gemini");
    }

    private void LoadApiKey(string provider)
    {
        var apiKey =
            _credentialStore.GetApiKey(provider);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys[provider] = apiKey;
        }
    }

    private void ProviderInput_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (ProviderInput.SelectedValue
            is not string provider)
        {
            return;
        }

        if (provider != "Groq" &&
            provider != "OpenRouter" &&
            provider != "OpenAI" &&
            provider != "Gemini")
        {
            return;
        }

        SaveCurrentModel();
        SaveCurrentApiKeyToMemory();

        _settings.AIProvider = provider;
        MarkDirty();
        UpdateProviderUI();
    }

    private void UpdateProviderUI()
    {
        if (_settings.AIProvider == "OpenRouter")
        {
            ModelLabel.Text = "OpenRouter Model";
            ModelInput.Text = _settings.OpenRouterModel;
            ApiKeyLabel.Text = "OpenRouter API Key";
        }
        else if (_settings.AIProvider == "OpenAI")
        {
            ModelLabel.Text = "OpenAI Model";
            ModelInput.Text = _settings.OpenAIModel;
            ApiKeyLabel.Text = "OpenAI API Key";
        }
        else if (_settings.AIProvider == "Gemini")
        {
            ModelLabel.Text = "Gemini Model";
            ModelInput.Text = _settings.GeminiModel;
            ApiKeyLabel.Text = "Google AI Studio API Key";
        }
        else
        {
            ModelLabel.Text = "Groq Model";
            ModelInput.Text = _settings.GroqModel;
            ApiKeyLabel.Text = "Groq API Key";
        }

        FetchGeminiModelsButton.Visibility = _settings.AIProvider == "Gemini" ? Visibility.Visible : Visibility.Collapsed;
        LoadCurrentApiKeyToUI();
    }

    private void LoadCurrentApiKeyToUI()
    {
        ApiKeyInput.Password =
            _apiKeys.TryGetValue(
                _settings.AIProvider,
                out var apiKey)
                ? apiKey
                : string.Empty;
    }

    private void SaveCurrentApiKeyToMemory()
    {
        var apiKey =
            ApiKeyInput.Password.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys.Remove(
                _settings.AIProvider);

            return;
        }

        _apiKeys[_settings.AIProvider] =
            apiKey;
    }

    private void SaveApiKeys()
    {
        SaveCurrentApiKeyToMemory();

        SaveApiKey("Groq");
        SaveApiKey("OpenRouter");
        SaveApiKey("OpenAI");
        SaveApiKey("Gemini");
    }

    private void SaveApiKey(string provider)
    {
        if (_apiKeys.TryGetValue(
                provider,
                out var apiKey) &&
            !string.IsNullOrWhiteSpace(apiKey))
        {
            _credentialStore.SaveApiKey(
                provider,
                apiKey);

            return;
        }

        _credentialStore.DeleteApiKey(provider);
    }

    private void SaveCurrentModel()
    {
        var model =
            ModelInput.Text.Trim();

        if (_settings.AIProvider == "OpenRouter")
            _settings.OpenRouterModel = model;
        else if (_settings.AIProvider == "OpenAI")
            _settings.OpenAIModel = model;
        else if (_settings.AIProvider == "Gemini")
            _settings.GeminiModel = model;
        else
            _settings.GroqModel = model;
    }

    private void BrowseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.OpenFileDialog
            {
                Title =
                    "Select an executable",

                Filter =
                    "Executable files (*.exe)|*.exe",

                CheckFileExists =
                    true
            };

        if (dialog.ShowDialog() == true)
        {
            PathInput.Text = dialog.FileName;
            MarkDirty();
        }
    }

    private static bool IsUnderDirectory(
        string path,
        string directory)
    {
        return path.Equals(
                   directory,
                   StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                directory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                directory + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystem32Path(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string windowsDirectory =
                Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string system32 = Path.GetFullPath(
                Path.Combine(windowsDirectory, "System32"))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string sysWow64 = Path.GetFullPath(
                Path.Combine(windowsDirectory, "SysWOW64"))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            return IsUnderDirectory(fullPath, system32) ||
                IsUnderDirectory(fullPath, sysWow64);
        }
        catch
        {
            return false;
        }
    }

    private void AddShortcutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var command =
            CommandInput.Text
                .Trim()
                .TrimStart('/');

        var path =
            PathInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(command) ||
            string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (IsSystem32Path(path))
        {
            WpfMessageBox.Show(
                this,
                "Shortcuts to System32 are blocked.",
                "Shortcut blocked",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning);
            return;
        }

        if (_editingCommand is not null)
        {
            _settings.Shortcuts.Remove(
                _editingCommand);

            _editingCommand = null;
        }

        _settings.Shortcuts[
            command.ToLowerInvariant()] =
            path;

        CommandInput.Text =
            string.Empty;

        PathInput.Text =
            string.Empty;

        AddShortcutButton.Content =
            "Add Shortcut";

        CancelEditButton.Visibility =
            Visibility.Collapsed;

        RefreshShortcutList();
        MarkDirty();
    }

    private void CancelEditButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _editingCommand = null;

        CommandInput.Text =
            string.Empty;

        PathInput.Text =
            string.Empty;

        AddShortcutButton.Content =
            "Add Shortcut";

        CancelEditButton.Visibility =
            Visibility.Collapsed;
    }

    private void EditShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not WpfButton button ||
            button.Tag is not string command)
        {
            return;
        }

        if (!_settings.Shortcuts.TryGetValue(
                command,
                out var path))
        {
            return;
        }

        _editingCommand = command;

        CommandInput.Text =
            command;

        PathInput.Text =
            path;

        AddShortcutButton.Content =
            "Update Shortcut";

        CancelEditButton.Visibility =
            Visibility.Visible;
    }

    private void DeleteShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not WpfButton button ||
            button.Tag is not string command)
        {
            return;
        }

        _settings.Shortcuts.Remove(command);
        MarkDirty();

        if (_editingCommand == command)
        {
            CancelEditButton_Click(
                sender,
                e);
        }

        RefreshShortcutList();
    }

    private void RefreshShortcutList()
    {
        ShortcutList.Children.Clear();

        foreach (var shortcut in
                 _settings.Shortcuts)
        {
            var row =
                new Grid
                {
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            4)
                };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(140)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        GridLength.Auto
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        GridLength.Auto
                });

            var commandText =
                new TextBlock
                {
                    Text =
                        "/" + shortcut.Key,

                    FontSize = 16,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Foreground =
                        WpfBrushes.Black
                };

            Grid.SetColumn(
                commandText,
                0);

            var pathText =
                new TextBlock
                {
                    Text =
                        Path.GetFileName(
                            shortcut.Value),

                    FontSize = 16,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Foreground =
                        WpfBrushes.Black
                };

            Grid.SetColumn(
                pathText,
                1);

            var editButton =
                new WpfButton
                {
                    Content =
                        "Edit",

                    Tag =
                        shortcut.Key,

                    Margin =
                        new Thickness(
                            8,
                            0,
                            0,
                            0)
                };

            editButton.Click +=
                EditShortcut_Click;

            Grid.SetColumn(
                editButton,
                2);

            var deleteButton =
                new WpfButton
                {
                    Content =
                        "Delete",

                    Tag =
                        shortcut.Key,

                    Margin =
                        new Thickness(
                            8,
                            0,
                            0,
                            0)
                };

            deleteButton.Click +=
                DeleteShortcut_Click;

            Grid.SetColumn(
                deleteButton,
                3);

            row.Children.Add(
                commandText);

            row.Children.Add(
                pathText);

            row.Children.Add(
                editButton);

            row.Children.Add(
                deleteButton);

            ShortcutList.Children.Add(
                row);
        }
    }

    private void ExportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentModel();
        SaveCurrentHistoryBudget();

        _settings.StreamResponses =
            StreamingCheckBox.IsChecked == true;
        _settings.JailbreakEnabled = JailbreakCheckBox.IsChecked == true;
        _settings.JailbreakPrompt = JailbreakPromptInput.Text;

        var dialog =
            new Microsoft.Win32.SaveFileDialog
            {
                Title =
                    "Export Ballknower Settings",

                FileName =
                    "BallknowerSettings.json",

                Filter =
                    "JSON Settings File (*.json)|*.json"
            };

        if (dialog.ShowDialog() != true)
            return;

        var json =
            JsonSerializer.Serialize(
                _settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            dialog.FileName,
            json);
    }

    private void ImportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.OpenFileDialog
            {
                Title =
                    "Import Ballknower Settings",

                Filter =
                    "JSON Settings File (*.json)|*.json",

                CheckFileExists = true
            };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var json =
                File.ReadAllText(
                    dialog.FileName);

            var importedSettings =
                JsonSerializer.Deserialize<AppSettings>(
                    json);

            if (importedSettings is null)
                return;

            if (importedSettings.AIProvider !=
                    "Groq" &&
                importedSettings.AIProvider !=
                    "OpenRouter" &&
                importedSettings.AIProvider !=
                    "OpenAI" &&
                importedSettings.AIProvider !=
                    "Gemini")
            {
                throw new JsonException(
                    "Unsupported AI provider.");
            }

            _isInitializing = true;

            try
            {
                _settings.AIProvider =
                    importedSettings.AIProvider;

                _settings.OpenRouterModel =
                    importedSettings.OpenRouterModel;

                _settings.GroqModel =
                    importedSettings.GroqModel;

                _settings.OpenAIModel = importedSettings.OpenAIModel;
                _settings.GeminiModel = importedSettings.GeminiModel;

                _settings.StreamResponses =
                    importedSettings.StreamResponses;

                _settings.JailbreakEnabled =
                    importedSettings.JailbreakEnabled;
                _settings.JailbreakPrompt =
                    importedSettings.JailbreakPrompt ?? string.Empty;

                _settings.OpeningShortcut =
                    NormalizeOpeningShortcut(
                        importedSettings.OpeningShortcut);

                _settings.HistoryTokenBudget =
                    NormalizeHistoryBudget(
                        importedSettings.HistoryTokenBudget);

                _settings.Shortcuts =
                    importedSettings.Shortcuts ??
                    new Dictionary<string, string>();

                _settings.StylePreset = importedSettings.StylePreset ?? "Default";
                _settings.StyleThemeMode = importedSettings.StyleThemeMode ?? "Unified";
                _settings.LightThemeBackground = importedSettings.LightThemeBackground ?? "#FFFFFFFF";
                _settings.LightThemeForeground = importedSettings.LightThemeForeground ?? "#FF000000";
                _settings.DarkThemeBackground = importedSettings.DarkThemeBackground ?? "#FF000000";
                _settings.DarkThemeForeground = importedSettings.DarkThemeForeground ?? "#FFFFFFFF";
                _settings.StyleFontFamily = importedSettings.StyleFontFamily ?? "Segoe UI";
                _settings.StyleAnimatedEffects = importedSettings.StyleAnimatedEffects;
                _settings.StyleRainbowBorder = importedSettings.StyleRainbowBorder;
                _settings.StyleGlowEffect = importedSettings.StyleGlowEffect;

                ProviderInput.SelectedValue =
                    _settings.AIProvider;

                UpdateProviderUI();

                StreamingCheckBox.IsChecked =
                    _settings.StreamResponses;

                JailbreakCheckBox.IsChecked =
                    _settings.JailbreakEnabled;
                JailbreakPromptInput.Text =
                    _settings.JailbreakPrompt;
                JailbreakPromptPanel.Visibility =
                    _settings.JailbreakEnabled
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                OpeningShortcutInput.SelectedValue =
                    _settings.OpeningShortcut;

                UpdateHistorySlider();

                _editingCommand = null;

                CommandInput.Text =
                    string.Empty;

                PathInput.Text =
                    string.Empty;

                AddShortcutButton.Content =
                    "Add Shortcut";

                CancelEditButton.Visibility =
                    Visibility.Collapsed;

                RefreshShortcutList();
                LoadStyleControls();
                MarkDirty();
            }
            finally
            {
                _isInitializing = false;
            }
            MarkDirty();
        }
        catch (JsonException)
        {
            WpfMessageBox.Show(
                "The selected file is not a valid Ballknower settings file.",
                "Import Failed",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                "Could not import the settings file.\n\n" +
                ex.Message,
                "Import Failed",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }

    private void LoadStyleControls()
    {
        StylePresetInput.SelectedValue = _settings.StylePreset;
        UnifiedThemeRadio.IsChecked = !string.Equals(_settings.StyleThemeMode, "Separate", StringComparison.OrdinalIgnoreCase);
        SeparateThemeRadio.IsChecked = !UnifiedThemeRadio.IsChecked;
        LightBackgroundInput.Text = _settings.LightThemeBackground;
        LightForegroundInput.Text = _settings.LightThemeForeground;
        DarkBackgroundInput.Text = _settings.DarkThemeBackground;
        DarkForegroundInput.Text = _settings.DarkThemeForeground;
        StyleFontInput.SelectedValue = _settings.StyleFontFamily;
        AnimatedEffectsCheckBox.IsChecked = _settings.StyleAnimatedEffects;
        RainbowBorderCheckBox.IsChecked = _settings.StyleRainbowBorder;
        GlowEffectCheckBox.IsChecked = _settings.StyleGlowEffect;
        UpdateThemeEditorState();
    }

    private void StylePresetInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || StylePresetInput.SelectedValue is not string preset) return;
        _settings.StylePreset = preset;
        ApplyStylePreset(preset);
        MarkDirty();
    }

    private void ApplyStylePreset(string preset)
    {
        var values = preset switch
        {
            "Blue" => ("#FFEAF6FF", "#FF09243A", "#FF071A2B", "#FFEAF6FF"),
            "Purple" => ("#FFF5EEFF", "#FF29153D", "#FF1B0D2A", "#FFF5EEFF"),
            "Green" => ("#FFECFFF4", "#FF0C3020", "#FF071F16", "#FFECFFF4"),
            "Sunset" => ("#FFFFF1E8", "#FF3A1B0C", "#FF2A1108", "#FFFFF1E8"),
            _ => ("#FFFFFFFF", "#FF000000", "#FF000000", "#FFFFFFFF")
        };
        _settings.LightThemeBackground = values.Item1;
        _settings.LightThemeForeground = values.Item2;
        _settings.DarkThemeBackground = values.Item3;
        _settings.DarkThemeForeground = values.Item4;
        LightBackgroundInput.Text = values.Item1;
        LightForegroundInput.Text = values.Item2;
        DarkBackgroundInput.Text = values.Item3;
        DarkForegroundInput.Text = values.Item4;
    }

    private void ThemeMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || UnifiedThemeRadio is null) return;
        _settings.StyleThemeMode = UnifiedThemeRadio.IsChecked == true ? "Unified" : "Separate";
        UpdateThemeEditorState();
        MarkDirty();
    }

    private void UpdateThemeEditorState()
    {
        bool separate = SeparateThemeRadio?.IsChecked == true;
        LightBackgroundInput.IsEnabled = separate;
        LightForegroundInput.IsEnabled = separate;
        DarkBackgroundInput.IsEnabled = separate;
        DarkForegroundInput.IsEnabled = separate;
    }

    private void StyleColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.LightThemeBackground = LightBackgroundInput.Text.Trim();
        _settings.LightThemeForeground = LightForegroundInput.Text.Trim();
        _settings.DarkThemeBackground = DarkBackgroundInput.Text.Trim();
        _settings.DarkThemeForeground = DarkForegroundInput.Text.Trim();
        _settings.StylePreset = "Custom";
        MarkDirty();
    }

    private void StyleFontInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || StyleFontInput.SelectedValue is not string font) return;
        _settings.StyleFontFamily = font;
        MarkDirty();
    }

    private void StyleOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.StyleAnimatedEffects = AnimatedEffectsCheckBox.IsChecked == true;
        _settings.StyleRainbowBorder = RainbowBorderCheckBox.IsChecked == true;
        _settings.StyleGlowEffect = GlowEffectCheckBox.IsChecked == true;
        MarkDirty();
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentModel();
        SaveCurrentHistoryBudget();
        SaveApiKeys();

        _settings.StreamResponses =
            StreamingCheckBox.IsChecked == true;
        _settings.JailbreakEnabled =
            JailbreakCheckBox.IsChecked == true;
        _settings.JailbreakPrompt =
            JailbreakPromptInput.Text;

        _targetSettings.AIProvider =
            _settings.AIProvider;

        _targetSettings.OpenRouterModel =
            _settings.OpenRouterModel;

        _targetSettings.GroqModel =
            _settings.GroqModel;

        _targetSettings.OpenAIModel = _settings.OpenAIModel;
        _targetSettings.GeminiModel = _settings.GeminiModel;

        _targetSettings.StreamResponses =
            _settings.StreamResponses;
        _targetSettings.JailbreakEnabled = _settings.JailbreakEnabled;
        _targetSettings.JailbreakPrompt = _settings.JailbreakPrompt;

        _targetSettings.OpeningShortcut =
            _settings.OpeningShortcut;

        _targetSettings.HistoryTokenBudget =
            _settings.HistoryTokenBudget;

        _targetSettings.Shortcuts =
            new Dictionary<string, string>(
                _settings.Shortcuts);

        _targetSettings.StylePreset = _settings.StylePreset;
        _targetSettings.StyleThemeMode = _settings.StyleThemeMode;
        _targetSettings.LightThemeBackground = _settings.LightThemeBackground;
        _targetSettings.LightThemeForeground = _settings.LightThemeForeground;
        _targetSettings.DarkThemeBackground = _settings.DarkThemeBackground;
        _targetSettings.DarkThemeForeground = _settings.DarkThemeForeground;
        _targetSettings.StyleFontFamily = _settings.StyleFontFamily;
        _targetSettings.StyleAnimatedEffects = _settings.StyleAnimatedEffects;
        _targetSettings.StyleRainbowBorder = _settings.StyleRainbowBorder;
        _targetSettings.StyleGlowEffect = _settings.StyleGlowEffect;

        _settingsStore.Save(
            _targetSettings);

        (System.Windows.Application.Current as App)?
            .UpdateOpeningShortcut(
                _settings.OpeningShortcut);
        _isDirty = false;
        _allowClose = true;
        Close();
    }


    private void RefreshGoogleDriveStatus()
    {
        bool connected = _googleDriveService.IsConnected;
        GoogleDriveStatusText.Text = connected ? "● Connected" : "○ Not connected";
        GoogleDriveStatusText.Foreground = connected ? WpfBrushes.LightGreen : WpfBrushes.LightGray;
        GoogleDriveConnectButton.IsEnabled = !connected;
        GoogleDriveTestButton.IsEnabled = connected;
        GoogleDriveDisconnectButton.IsEnabled = connected;
        if (!connected)
            GoogleDriveAccountText.Text = "";
    }

    private async void GoogleDriveConnectButton_Click(object sender, RoutedEventArgs e)
    {
        GoogleDriveConnectButton.IsEnabled = false;
        GoogleDriveTestButton.IsEnabled = false;
        GoogleDriveDisconnectButton.IsEnabled = false;
        GoogleDriveErrorText.Visibility = Visibility.Collapsed;
        GoogleDriveErrorText.Text = "";

        try
        {
            await _googleDriveService.ConnectAsync();
            RefreshGoogleDriveStatus();
            var tested = await _googleDriveService.TestConnectionAsync();
            if (!tested)
                throw new InvalidOperationException("Google Drive connected, but the connection test failed.");
            GoogleDriveStatusText.Text = "● Connected and tested";
            GoogleDriveStatusText.Foreground = WpfBrushes.LightGreen;
        }
        catch (Exception ex)
        {
            GoogleDriveErrorText.Text = "Google Drive connection failed: " + ex.Message;
            GoogleDriveErrorText.Visibility = Visibility.Visible;
            RefreshGoogleDriveStatus();
        }
    }

    private async void GoogleDriveTestButton_Click(object sender, RoutedEventArgs e)
    {
        GoogleDriveErrorText.Visibility = Visibility.Collapsed;
        GoogleDriveTestButton.IsEnabled = false;
        try
        {
            if (!await _googleDriveService.TestConnectionAsync())
                throw new InvalidOperationException("Google Drive did not respond successfully.");
            GoogleDriveStatusText.Text = "● Connected and tested";
            GoogleDriveStatusText.Foreground = WpfBrushes.LightGreen;
        }
        catch (Exception ex)
        {
            GoogleDriveErrorText.Text = "Google Drive test failed: " + ex.Message;
            GoogleDriveErrorText.Visibility = Visibility.Visible;
            RefreshGoogleDriveStatus();
        }
        finally
        {
            GoogleDriveTestButton.IsEnabled = _googleDriveService.IsConnected;
        }
    }

    private async void GoogleDriveDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        GoogleDriveErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await _googleDriveService.DisconnectAsync();
            RefreshGoogleDriveStatus();
        }
        catch (Exception ex)
        {
            GoogleDriveErrorText.Text = "Google Drive disconnect failed: " + ex.Message;
            GoogleDriveErrorText.Visibility = Visibility.Visible;
        }
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}