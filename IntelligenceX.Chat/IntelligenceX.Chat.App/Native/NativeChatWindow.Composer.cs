using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildComposer() {
        _composerShell = new Border {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = NativeControlBrushes.BorderStrong,
            Background = NativeControlBrushes.Surface,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(20, 8, 20, 14),
            Visibility = Visibility.Collapsed
        };
        var grid = new Grid {
            ColumnSpacing = 8,
            RowSpacing = 2
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _composerShell.Child = grid;

        var hint = new TextBlock {
            Text = "Enter to send · Shift+Enter for a new line",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = NativeControlBrushes.TextSecondary
        };
        Grid.SetColumn(hint, 0);
        Grid.SetColumnSpan(hint, 2);
        Grid.SetRow(hint, 1);
        grid.Children.Add(hint);

        _composer = new TextBox {
            Name = "ComposerBox",
            AcceptsReturn = true,
            MinHeight = 48,
            MaxHeight = 120,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Message IX Chat",
            Padding = new Thickness(4, 4, 4, 4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            UseSystemFocusVisuals = false,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };
        AutomationProperties.SetName(_composer, "Message");
        _composer.Text = _viewModel.Draft;
        _composer.TextChanged += (_, _) => {
            if (!string.Equals(_viewModel.Draft, _composer.Text, StringComparison.Ordinal)) {
                _viewModel.Draft = _composer.Text;
            }
        };
        // TextBox handles Enter internally when AcceptsReturn is enabled. Observe the
        // preview route so plain Enter can submit before the control inserts a newline.
        _composer.PreviewKeyDown += OnComposerKeyDown;
        _composer.GotFocus += (_, _) => _composerShell.BorderBrush = NativeControlBrushes.Accent;
        _composer.LostFocus += (_, _) => _composerShell.BorderBrush = NativeControlBrushes.BorderStrong;
        Grid.SetColumn(_composer, 0);
        Grid.SetRow(_composer, 0);
        grid.Children.Add(_composer);

        _sendButton = new Button {
            Name = "SendButton",
            Content = new FontIcon {
                Glyph = "\uE724",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17
            },
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(21),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = NativeControlBrushes.Accent,
            Foreground = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Accent
        };
        _sendButton.Click += async (_, _) => await SendAsync().ConfigureAwait(true);
        AutomationProperties.SetName(_sendButton, "Send message");
        ToolTipService.SetToolTip(_sendButton, "Send message");
        Grid.SetColumn(_sendButton, 1);
        Grid.SetRow(_sendButton, 0);
        grid.Children.Add(_sendButton);

        _stopButton = new Button {
            Name = "StopButton",
            Content = new TextBlock { Text = "■", FontSize = 15 },
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(21),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = NativeControlBrushes.SurfaceMuted,
            BorderBrush = NativeControlBrushes.BorderStrong,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _stopButton.Click += (_, _) => _viewModel.CancelActiveTurn();
        AutomationProperties.SetName(_stopButton, "Stop response");
        ToolTipService.SetToolTip(_stopButton, "Stop response");
        Grid.SetColumn(_stopButton, 1);
        Grid.SetRow(_stopButton, 0);
        grid.Children.Add(_stopButton);

        return _composerShell;
    }

    private async Task SendAsync() {
        UpdateCommandState();
        var sendTask = _viewModel.SendDraftAsync();
        _activeSendTask = sendTask;
        _ = await sendTask.ConfigureAwait(true);
        if (_lifetimeCts.IsCancellationRequested) {
            return;
        }
        UpdateCommandState();
        _composer.Focus(FocusState.Programmatic);
    }

    private async void OnComposerKeyDown(object sender, KeyRoutedEventArgs args) {
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (!ShouldSendComposerKey(args.Key, shiftState)) {
            return;
        }

        args.Handled = true;
        await SendAsync().ConfigureAwait(true);
    }

    internal static bool ShouldSendComposerKey(VirtualKey key, CoreVirtualKeyStates shiftState) =>
        key == VirtualKey.Enter && (shiftState & CoreVirtualKeyStates.Down) == 0;

    private void UpdateCommandState() {
        _sendButton.IsEnabled = _viewModel.CanSend;
        _sendButton.Visibility = _viewModel.CanStop ? Visibility.Collapsed : Visibility.Visible;
        _stopButton.IsEnabled = _viewModel.CanStop;
        _stopButton.Visibility = _viewModel.CanStop ? Visibility.Visible : Visibility.Collapsed;
        _composerShell.Visibility = _viewModel.AuthenticationState == NativeAuthenticationState.SignedIn || _viewModel.CanStop
            ? Visibility.Visible : Visibility.Collapsed;
        _checkSignInButton.IsEnabled = _viewModel.CanCheckSignIn;
        _signInButton.IsEnabled = _viewModel.CanStartSignIn;
        _runQueuedTurnButton.IsEnabled = _viewModel.CanRunQueuedTurn;
        _clearQueuedTurnsButton.IsEnabled = _viewModel.CanClearQueuedTurns;
        var showEmptySignIn = _viewModel.Transcript.Count == 0
                              && _viewModel.AuthenticationState is NativeAuthenticationState.Failed or NativeAuthenticationState.Required;
        _checkSignInButton.Visibility = showEmptySignIn ? Visibility.Collapsed : Visibility.Visible;
        _signInButton.Visibility = showEmptySignIn ? Visibility.Collapsed : Visibility.Visible;
        _signInStatusChip.Visibility = showEmptySignIn ? Visibility.Collapsed : Visibility.Visible;
        _runtimeStatusChip.Visibility = ShouldShowRuntimeStatus(
            _viewModel.AuthenticationState,
            _viewModel.Transcript.Count > 0,
            _viewModel.StatusText)
            ? Visibility.Visible : Visibility.Collapsed;
        _exportButton.IsEnabled = _viewModel.Transcript.Count > 0;
    }

    internal static bool ShouldShowRuntimeStatus(NativeAuthenticationState state, bool hasTranscript, string? status) {
        if (string.IsNullOrWhiteSpace(status)) {
            return false;
        }
        if (hasTranscript) {
            return true;
        }
        // The failed empty state already renders StatusText as its body. Required
        // has generic copy, so unrelated errors still need the status banner.
        return state switch {
            NativeAuthenticationState.Failed => false,
            NativeAuthenticationState.Required => status is not ("Ready" or "Sign-in canceled" or "Sign-in required."),
            _ => true
        };
    }

    private void UpdateViewStateFromViewModel(bool refreshEmptyState) {
        _signInText.Text = _viewModel.SignInText;
        _runtimeStatusText.Text = _viewModel.StatusText;
        ApplyAuthenticationChrome();
        UpdateCommandState();
        if (!string.Equals(_composer.Text, _viewModel.Draft, StringComparison.Ordinal)) {
            _composer.Text = _viewModel.Draft;
        }

        if (refreshEmptyState && _viewModel.Transcript.Count == 0) {
            RenderTranscript();
        }
    }
}
