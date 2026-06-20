using System;
using System.ComponentModel;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        if (args.PropertyName is nameof(NativeChatTranscriptItem.Text)
            or nameof(NativeChatTranscriptItem.Status)
            or nameof(NativeChatTranscriptItem.Content)) {
            Render();
        }
    }

    private void Render() {
        _contentPanel.Children.Clear();
        if (_item == null) {
            _roleText.Text = string.Empty;
            _statusText.Text = string.Empty;
            return;
        }

        _roleText.Text = _item.IsUser ? "OPERATOR" : _item.Role.ToUpperInvariant();
        _statusText.Text = string.IsNullOrWhiteSpace(_item.Status) ? FormatCreatedAt(_item.CreatedAt) : _item.Status;
        ApplyMessageChrome(_item);

        if (_item.Content.Count == 0) {
            if (!string.IsNullOrWhiteSpace(_item.Text)) {
                _contentPanel.Children.Add(CreateParagraph(_item.Text));
            }

            return;
        }

        foreach (var part in _item.Content) {
            _contentPanel.Children.Add(CreateContentElement(part));
        }
    }

    private void ApplyMessageChrome(NativeChatTranscriptItem item) {
        if (item.IsUser) {
            _shell.HorizontalAlignment = HorizontalAlignment.Stretch;
            _shell.MaxWidth = double.PositiveInfinity;
            _shell.Margin = new Thickness(96, 0, 0, 10);
            _shell.Background = NativeControlBrushes.Rgb(248, 251, 255);
            _shell.BorderBrush = NativeControlBrushes.Rgb(202, 216, 244);
            _accentBar.Background = NativeControlBrushes.Accent;
            _roleText.Foreground = NativeControlBrushes.Accent;
            return;
        }

        _shell.HorizontalAlignment = HorizontalAlignment.Stretch;
        _shell.MaxWidth = double.PositiveInfinity;
        _shell.Margin = new Thickness(0, 0, 0, 10);
        _shell.Background = NativeControlBrushes.Surface;
        _shell.BorderBrush = NativeControlBrushes.Border;
        _accentBar.Background = item.IsAssistant
            ? NativeControlBrushes.Rgb(14, 132, 115)
            : NativeControlBrushes.Rgb(132, 77, 192);
        _roleText.Foreground = item.IsAssistant
            ? NativeControlBrushes.Rgb(14, 105, 94)
            : NativeControlBrushes.Rgb(96, 72, 152);
    }

    private static string FormatCreatedAt(DateTimeOffset value) =>
        value.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    private static FrameworkElement CreateContentElement(NativeTranscriptContent content) =>
        content.Kind switch {
            NativeTranscriptContentKind.Code => CreateCode(content),
            NativeTranscriptContentKind.Table => content.Table == null
                ? CreateDiagnostic("Table artifact unavailable.")
                : new NativeTranscriptTablePreviewControl(content.Table),
            NativeTranscriptContentKind.Visual => new NativeVisualArtifactHostControl(content.Visual),
            NativeTranscriptContentKind.Diagnostic => CreateDiagnostic(content.Text),
            _ => CreateParagraph(content.Text)
        };

    private static FrameworkElement CreateParagraph(string text) =>
        new TextBlock {
            Text = text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = NativeControlBrushes.TextPrimary
        };

    private static FrameworkElement CreateCode(NativeTranscriptContent content) =>
        new Border {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.SurfaceMuted,
            Child = new TextBlock {
                Text = content.Text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = NativeControlBrushes.TextPrimary
            }
        };

    private static FrameworkElement CreateDiagnostic(string text) =>
        new Border {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderBrush = NativeControlBrushes.Rgb(235, 198, 95),
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Rgb(255, 249, 230),
            Child = new TextBlock {
                Text = text ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = NativeControlBrushes.Rgb(107, 75, 15)
            }
        };
}
