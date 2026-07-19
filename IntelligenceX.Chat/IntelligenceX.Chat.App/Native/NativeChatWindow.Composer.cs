using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildComposer() {
        var shell = new Border {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = NativeControlBrushes.Border,
            Background = NativeControlBrushes.Surface,
            Padding = new Thickness(24, 14, 24, 14),
            MinHeight = 94
        };
        var grid = new Grid {
            ColumnSpacing = 10,
            RowSpacing = 6
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = grid;

        var label = new TextBlock {
            Text = "Message  ·  Enter to send  ·  Shift+Enter for a new line",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextSecondary
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumnSpan(label, 3);
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        _composer = new TextBox {
            Name = "ComposerBox",
            AcceptsReturn = true,
            MinHeight = 52,
            MaxHeight = 120,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Ask, investigate, run a safe check, or prepare an export...",
            Padding = new Thickness(12),
            Background = NativeControlBrushes.SurfaceMuted,
            BorderBrush = NativeControlBrushes.BorderStrong,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };
        _composer.Text = _viewModel.Draft;
        _composer.TextChanged += (_, _) => {
            if (!string.Equals(_viewModel.Draft, _composer.Text, StringComparison.Ordinal)) {
                _viewModel.Draft = _composer.Text;
            }
        };
        // TextBox handles Enter internally when AcceptsReturn is enabled. Observe the
        // preview route so plain Enter can submit before the control inserts a newline.
        _composer.PreviewKeyDown += OnComposerKeyDown;
        Grid.SetColumn(_composer, 0);
        Grid.SetRow(_composer, 1);
        grid.Children.Add(_composer);

        _sendButton = new Button {
            Name = "SendButton",
            Content = "Send",
            MinWidth = 78,
            MinHeight = 42,
            Background = NativeControlBrushes.Accent,
            Foreground = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Accent
        };
        _sendButton.Click += async (_, _) => await SendAsync().ConfigureAwait(true);
        Grid.SetColumn(_sendButton, 1);
        Grid.SetRow(_sendButton, 1);
        grid.Children.Add(_sendButton);

        _stopButton = new Button {
            Name = "StopButton",
            Content = "Stop",
            MinWidth = 70,
            MinHeight = 42,
            Background = NativeControlBrushes.SurfaceMuted,
            BorderBrush = NativeControlBrushes.BorderStrong,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _stopButton.Click += (_, _) => _viewModel.CancelActiveTurn();
        Grid.SetColumn(_stopButton, 2);
        Grid.SetRow(_stopButton, 1);
        grid.Children.Add(_stopButton);

        return shell;
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
        _stopButton.IsEnabled = _viewModel.CanStop;
        _checkSignInButton.IsEnabled = _viewModel.CanCheckSignIn;
        _signInButton.IsEnabled = _viewModel.CanStartSignIn;
        _runQueuedTurnButton.IsEnabled = _viewModel.CanRunQueuedTurn;
        _clearQueuedTurnsButton.IsEnabled = _viewModel.CanClearQueuedTurns;
        _checkSignInButton.Visibility = Visibility.Visible;
        _signInButton.Visibility = Visibility.Visible;
        _signInStatusChip.Visibility = Visibility.Visible;
        _runtimeStatusChip.Visibility = Visibility.Visible;
        _exportButton.IsEnabled = _viewModel.Transcript.Count > 0;
    }

    private void UpdateViewStateFromViewModel(bool refreshEmptyState) {
        _signInText.Text = _viewModel.SignInText;
        _runtimeStatusText.Text = _viewModel.StatusText;
        ApplyAuthenticationChrome();
        if (!string.Equals(_composer.Text, _viewModel.Draft, StringComparison.Ordinal)) {
            _composer.Text = _viewModel.Draft;
        }

        if (refreshEmptyState && _viewModel.Transcript.Count == 0) {
            RenderTranscript();
        }
    }
}
