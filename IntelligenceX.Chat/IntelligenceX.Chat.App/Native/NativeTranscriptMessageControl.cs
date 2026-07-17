using System;
using System.ComponentModel;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native transcript message renderer for projected OfficeIMO/ChartForgeX content.
/// </summary>
public sealed class NativeTranscriptMessageControl : UserControl {
    private readonly Border _shell;
    private readonly Border _accentBar;
    private readonly StackPanel _contentPanel;
    private readonly TextBlock _roleText;
    private readonly TextBlock _statusText;
    private NativeChatTranscriptItem? _item;
    private TextBlock? _streamingText;

    /// <summary>
    /// Initializes the native transcript message control.
    /// </summary>
    public NativeTranscriptMessageControl() {
        _roleText = new TextBlock {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _contentPanel = new StackPanel {
            Spacing = 8
        };
        _statusText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = NativeControlBrushes.TextMuted
        };
        _accentBar = new Border {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = NativeControlBrushes.Accent,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var body = new Grid {
            ColumnSpacing = 12
        };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_accentBar, 0);
        body.Children.Add(_accentBar);
        var stack = new StackPanel {
            Spacing = 7,
            Children = {
                BuildMessageHeader(),
                _contentPanel
            }
        };
        Grid.SetColumn(stack, 1);
        body.Children.Add(stack);

        _shell = new Border {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(7),
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = body
        };
        Content = _shell;

        DataContextChanged += OnDataContextChanged;
    }

    private Grid BuildMessageHeader() {
        var grid = new Grid {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_roleText, 0);
        grid.Children.Add(_roleText);
        Grid.SetColumn(_statusText, 1);
        grid.Children.Add(_statusText);
        return grid;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) {
        if (_item != null) {
            _item.PropertyChanged -= OnItemPropertyChanged;
        }

        _item = args.NewValue as NativeChatTranscriptItem;
        if (_item != null) {
            _item.PropertyChanged += OnItemPropertyChanged;
        }

        Render();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs args) {
        if (_item == null) {
            return;
        }

        if (args.PropertyName is nameof(NativeChatTranscriptItem.Status)) {
            RenderHeaderAndChrome();
            return;
        }

        if (args.PropertyName is nameof(NativeChatTranscriptItem.Content)) {
            RenderBody();
            return;
        }

        if (args.PropertyName is nameof(NativeChatTranscriptItem.Text)
            && _item.Content.Count == 0) {
            UpdateStreamingText();
        }
    }

    private void Render() {
        if (_item == null) {
            _roleText.Text = string.Empty;
            _statusText.Text = string.Empty;
            _contentPanel.Children.Clear();
            _streamingText = null;
            return;
        }

        RenderHeaderAndChrome();
        RenderBody();
    }

    private void RenderHeaderAndChrome() {
        if (_item == null) {
            return;
        }

        _roleText.Text = _item.IsUser ? "OPERATOR" : _item.Role.ToUpperInvariant();
        _statusText.Text = string.IsNullOrWhiteSpace(_item.Status) ? FormatCreatedAt(_item.CreatedAt) : _item.Status;
        ApplyMessageChrome(_item);
    }

    private void RenderBody() {
        if (_item == null) {
            _contentPanel.Children.Clear();
            _streamingText = null;
            return;
        }

        if (_item.Content.Count == 0) {
            UpdateStreamingText();
            return;
        }

        _contentPanel.Children.Clear();
        _streamingText = null;
        foreach (var part in _item.Content) {
            _contentPanel.Children.Add(new NativeTranscriptContentControl(part));
        }
    }

    private void UpdateStreamingText() {
        if (_item == null || string.IsNullOrWhiteSpace(_item.Text)) {
            if (_streamingText != null) {
                _contentPanel.Children.Clear();
                _streamingText = null;
            }

            return;
        }

        if (_streamingText == null) {
            _contentPanel.Children.Clear();
            _streamingText = CreateParagraph(_item.Text);
            _contentPanel.Children.Add(_streamingText);
            return;
        }

        _streamingText.Text = _item.Text;
    }

    private void ApplyMessageChrome(NativeChatTranscriptItem item) {
        if (item.IsUser) {
            _shell.HorizontalAlignment = HorizontalAlignment.Stretch;
            _shell.MaxWidth = double.PositiveInfinity;
            _shell.Margin = new Thickness(84, 0, 0, 12);
            _shell.Background = NativeControlBrushes.UserBubble;
            _shell.BorderBrush = NativeControlBrushes.UserBorder;
            _accentBar.Background = NativeControlBrushes.Accent;
            _roleText.Foreground = NativeControlBrushes.Accent;
            return;
        }

        _shell.HorizontalAlignment = HorizontalAlignment.Stretch;
        _shell.MaxWidth = double.PositiveInfinity;
        _shell.Margin = new Thickness(0, 0, 72, 12);
        _shell.Background = NativeControlBrushes.Surface;
        _shell.BorderBrush = NativeControlBrushes.Border;
        _accentBar.Background = item.IsAssistant
            ? NativeControlBrushes.AssistantAccent
            : NativeControlBrushes.SystemAccent;
        _roleText.Foreground = item.IsAssistant
            ? NativeControlBrushes.AssistantText
            : NativeControlBrushes.SystemText;
    }

    private static string FormatCreatedAt(DateTimeOffset value) =>
        value.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    private static TextBlock CreateParagraph(string text) =>
        new TextBlock {
            Text = text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 14,
            LineHeight = 21,
            Foreground = NativeControlBrushes.TextPrimary
        };
}
