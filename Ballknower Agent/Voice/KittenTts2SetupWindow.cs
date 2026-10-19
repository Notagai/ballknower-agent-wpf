using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ballknower.Voice;

public sealed class KittenTts2SetupWindow : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _detail;
    private readonly ProgressBar _progress;
    private readonly Button _closeButton;

    public KittenTts2SetupWindow()
    {
        Title = "KittenTTS 2 Setup";
        Width = 470;
        Height = 205;
        MinWidth = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(27, 27, 30));
        Foreground = Brushes.White;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Preparing KittenTTS 2",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        _status = new TextBlock
        {
            Text = "Checking whether KittenTTS 2 needs to download, update, or load…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(_status);
        _progress = new ProgressBar
        {
            Height = 8,
            IsIndeterminate = true,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(_progress);
        _detail = new TextBlock
        {
            Text = "The first setup may take several minutes and download model files.",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(_detail);
        _closeButton = new Button
        {
            Content = "Close",
            Padding = new Thickness(14, 5, 14, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = false
        };
        _closeButton.Click += (_, _) => Close();
        panel.Children.Add(_closeButton);
        Content = panel;
    }

    public void SetStatus(string message) => _status.Text = message;

    public void SetReady()
    {
        _status.Text = "KittenTTS 2 is ready.";
        _detail.Text = "The model loaded successfully. Speech can now be generated locally.";
        _progress.IsIndeterminate = false;
        _progress.Value = 100;
        _closeButton.IsEnabled = true;
        _closeButton.Content = "Done";
    }

    public void SetFailure(string message)
    {
        _status.Text = "KittenTTS 2 could not initialize.";
        _detail.Text = message;
        _progress.IsIndeterminate = false;
        _progress.Value = 0;
        _closeButton.IsEnabled = true;
        _closeButton.Content = "Close";
        Height = 260;
    }
}
