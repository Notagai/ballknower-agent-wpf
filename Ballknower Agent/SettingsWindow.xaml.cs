using Ballknower.Config;
using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.IO;
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

    private string? _editingCommand;
    private bool _isInitializing;
    private bool _isDirty;
    private bool _allowClose;
    private static readonly HttpClient TestHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        _isInitializing = true;

        _targetSettings = settings;

        _settings = new AppSettings
        {
            AIProvider = settings.AIProvider,
            OpenRouterModel = settings.OpenRouterModel,
            GroqModel = settings.GroqModel,
            StreamResponses = settings.StreamResponses,
            HistoryTokenBudget = settings.HistoryTokenBudget,
            Shortcuts = new Dictionary<string, string>(
                settings.Shortcuts)
        };

        _settingsStore = new SettingsStore();
        _credentialStore = new CredentialStore();
        _apiKeys = new Dictionary<string, string>();

        LoadApiKeys();

        ProviderInput.SelectedValue =
            _settings.AIProvider;

        UpdateProviderUI();

        StreamingCheckBox.IsChecked =
            _settings.StreamResponses;

        UpdateHistorySlider();
        RefreshShortcutList();

        _isInitializing = false;
    }

    private void SettingsChanged(object sender, RoutedEventArgs e) => MarkDirty();
    private void SettingsChanged(object sender, RoutedEventArgs e, bool unused) => MarkDirty();
    private void MarkDirty()
    {
        if (_isInitializing) return;
        _isDirty = true;
        if (UnsavedChangesText is not null) UnsavedChangesText.Visibility = Visibility.Visible;
        KeyTestStatus.Text = "○ Not tested"; PromptTestStatus.Text = "○ Not tested";
    }

    private async void TestKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { KeyTestStatus.Text = "✗ Enter an API key"; return; }
        KeyTestStatus.Text = "Testing…";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _settings.AIProvider == "Groq" ? "https://api.groq.com/openai/v1/models" : "https://openrouter.ai/api/v1/auth/key");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
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
            var endpoint = _settings.AIProvider == "Groq" ? "https://api.groq.com/openai/v1/chat/completions" : "https://openrouter.ai/api/v1/chat/completions";
            var body = JsonSerializer.Serialize(new { model, messages = new[] { new { role = "user", content = "Reply with exactly: Ballknower test OK" } }, max_tokens = 20, stream = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (_settings.AIProvider == "OpenRouter") request.Headers.Add("X-Title", "Ballknower");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await TestHttp.SendAsync(request);
            PromptTestStatus.Text = response.IsSuccessStatusCode ? "✓ Prompt succeeded" : $"✗ Failed ({(int)response.StatusCode})";
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
            provider != "OpenRouter")
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
        if (_settings.AIProvider ==
            "OpenRouter")
        {
            ModelLabel.Text =
                "OpenRouter Model";

            ModelInput.Text =
                _settings.OpenRouterModel;

            ApiKeyLabel.Text =
                "OpenRouter API Key";
        }
        else
        {
            ModelLabel.Text =
                "Groq Model";

            ModelInput.Text =
                _settings.GroqModel;

            ApiKeyLabel.Text =
                "Groq API Key";
        }

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

        if (_settings.AIProvider ==
            "OpenRouter")
        {
            _settings.OpenRouterModel =
                model;
        }
        else
        {
            _settings.GroqModel =
                model;
        }
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
                        WpfBrushes.White
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
                    "OpenRouter")
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

                _settings.StreamResponses =
                    importedSettings.StreamResponses;

                _settings.HistoryTokenBudget =
                    NormalizeHistoryBudget(
                        importedSettings.HistoryTokenBudget);

                _settings.Shortcuts =
                    importedSettings.Shortcuts ??
                    new Dictionary<string, string>();

                ProviderInput.SelectedValue =
                    _settings.AIProvider;

                UpdateProviderUI();

                StreamingCheckBox.IsChecked =
                    _settings.StreamResponses;

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

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentModel();
        SaveCurrentHistoryBudget();
        SaveApiKeys();

        _settings.StreamResponses =
            StreamingCheckBox.IsChecked == true;

        _targetSettings.AIProvider =
            _settings.AIProvider;

        _targetSettings.OpenRouterModel =
            _settings.OpenRouterModel;

        _targetSettings.GroqModel =
            _settings.GroqModel;

        _targetSettings.StreamResponses =
            _settings.StreamResponses;

        _targetSettings.HistoryTokenBudget =
            _settings.HistoryTokenBudget;

        _targetSettings.Shortcuts =
            new Dictionary<string, string>(
                _settings.Shortcuts);

        _settingsStore.Save(
            _targetSettings);
        _isDirty = false;
        _allowClose = true;
        Close();
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}