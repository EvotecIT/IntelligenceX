using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildHeader() {
        var shell = new Border {
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(22, 12, 22, 12)
        };
        var header = new Grid {
            ColumnSpacing = 16
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = header;

        var titleGrid = new Grid {
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 560
        };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logo = new Border {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = NativeControlBrushes.Accent,
            Child = new TextBlock {
                Text = "IX",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = NativeControlBrushes.Surface,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(logo, 0);
        titleGrid.Children.Add(logo);

        var titleStack = new StackPanel {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 500
        };
        titleStack.Children.Add(new TextBlock {
            Text = "IntelligenceX Chat",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextPrimary
        });
        titleStack.Children.Add(new TextBlock {
            Text = "Operator workspace for chat, evidence, and native artifacts",
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = NativeControlBrushes.TextSecondary
        });
        Grid.SetColumn(titleStack, 1);
        titleGrid.Children.Add(titleStack);
        Grid.SetColumn(titleGrid, 0);
        header.Children.Add(titleGrid);

        var rightStack = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        rightStack.Children.Add(BuildTopBarChip("Model router", NativeControlBrushes.AccentSoft, NativeControlBrushes.Accent));

        var settingsButton = new Button {
            Content = "Settings",
            MinWidth = 76,
            MinHeight = 30
        };
        ToolTipService.SetToolTip(settingsButton, "Open the shared profile, provider, model, endpoint, credential, and tool-pack settings workspace.");
        settingsButton.Click += async (_, _) => await OpenSharedSettingsWorkspaceAsync().ConfigureAwait(true);
        rightStack.Children.Add(settingsButton);

        _signInText = new TextBlock {
            Text = _viewModel.SignInText,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _signInStatusChip = new Border {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.SurfaceMuted,
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Child = _signInText
        };
        rightStack.Children.Add(_signInStatusChip);

        _checkSignInButton = new Button {
            Content = "Refresh",
            MinWidth = 70,
            MinHeight = 30
        };
        _checkSignInButton.Click += async (_, _) => await CheckSignInFromNativeAsync().ConfigureAwait(true);
        rightStack.Children.Add(_checkSignInButton);

        _signInButton = new Button {
            Content = "Sign in",
            MinWidth = 70,
            MinHeight = 30
        };
        _signInButton.Click += async (_, _) => await StartSignInFromNativeAsync().ConfigureAwait(true);
        rightStack.Children.Add(_signInButton);

        _runtimeStatusText = new TextBlock {
            Text = _viewModel.StatusText,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 190,
            Foreground = NativeControlBrushes.Success
        };
        _runtimeStatusChip = new Border {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.SuccessSoft,
            BorderBrush = NativeControlBrushes.Rgb(174, 222, 197),
            BorderThickness = new Thickness(1),
            Child = _runtimeStatusText
        };
        rightStack.Children.Add(_runtimeStatusChip);
        Grid.SetColumn(rightStack, 2);
        header.Children.Add(rightStack);

        return shell;
    }

    private async Task CheckSignInFromNativeAsync() {
        _ = await _viewModel.CheckSignInAsync().ConfigureAwait(true);
        UpdateCommandState();
    }

    private async Task StartSignInFromNativeAsync() {
        _ = await _viewModel.StartSignInAsync().ConfigureAwait(true);
        UpdateCommandState();
    }

    private void ApplyAuthenticationChrome() {
        var (background, border, foreground) = _viewModel.AuthenticationState switch {
            NativeAuthenticationState.SignedIn => (NativeControlBrushes.SuccessSoft, NativeControlBrushes.Rgb(174, 222, 197), NativeControlBrushes.Success),
            NativeAuthenticationState.Checking => (NativeControlBrushes.AccentSoft, NativeControlBrushes.Rgb(191, 210, 252), NativeControlBrushes.Accent),
            NativeAuthenticationState.Failed => (NativeControlBrushes.Rgb(255, 237, 237), NativeControlBrushes.Rgb(248, 184, 184), NativeControlBrushes.Rgb(172, 45, 45)),
            NativeAuthenticationState.Required => (NativeControlBrushes.Rgb(255, 247, 224), NativeControlBrushes.Rgb(242, 211, 143), NativeControlBrushes.Rgb(138, 91, 18)),
            _ => (NativeControlBrushes.SurfaceMuted, NativeControlBrushes.Border, NativeControlBrushes.TextSecondary)
        };

        _signInStatusChip.Background = background;
        _signInStatusChip.BorderBrush = border;
        _signInText.Foreground = foreground;
    }

    private static Border BuildTopBarChip(string text, Microsoft.UI.Xaml.Media.Brush background, Microsoft.UI.Xaml.Media.Brush foreground) =>
        new() {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = background,
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Child = new TextBlock {
                Text = text,
                FontSize = 12,
                Foreground = foreground
            }
        };
}
