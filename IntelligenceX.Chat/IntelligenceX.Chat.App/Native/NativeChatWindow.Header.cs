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
            ColumnSpacing = 16,
            RowSpacing = 10
        };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Text = "IX Chat",
            FontSize = 21,
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
            TextWrapping = TextWrapping.Wrap,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _runtimeStatusChip = new Border {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.SurfaceMuted,
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Child = _runtimeStatusText
        };
        Grid.SetRow(_runtimeStatusChip, 1);
        Grid.SetColumnSpan(_runtimeStatusChip, 2);
        header.Children.Add(_runtimeStatusChip);
        Grid.SetColumn(rightStack, 1);
        header.Children.Add(rightStack);

        return shell;
    }

    private async Task CheckSignInFromNativeAsync() {
        NativeLoginResult login;
        try {
            login = await _viewModel.CheckSignInAsync(_lifetimeCts.Token).ConfigureAwait(true);
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            return;
        }
        if (_lifetimeCts.IsCancellationRequested) {
            return;
        }

        if (login.IsAuthenticated) {
            await RefreshRuntimeReadinessAsync().ConfigureAwait(true);
        }
        UpdateCommandState();
    }

    private Task RefreshRuntimeReadinessAsync(bool force = false, bool synchronizeSelectedProfile = false) {
        if (_lifetimeCts.IsCancellationRequested) {
            return Task.CompletedTask;
        }

        var activeRefresh = _runtimeReadinessTask;
        if (!activeRefresh.IsCompleted) {
            if (!force) {
                return activeRefresh;
            }

            _runtimeReadinessTask = RefreshRuntimeReadinessAfterAsync(activeRefresh, synchronizeSelectedProfile);
            return _runtimeReadinessTask;
        }

        _runtimeReadinessTask = RefreshRuntimeReadinessCoreAsync(synchronizeSelectedProfile);
        return _runtimeReadinessTask;
    }

    private async Task RefreshRuntimeReadinessAfterAsync(
        Task activeRefresh,
        bool synchronizeSelectedProfile) {
        await activeRefresh.ConfigureAwait(true);
        if (!_lifetimeCts.IsCancellationRequested) {
            await RefreshRuntimeReadinessCoreAsync(synchronizeSelectedProfile).ConfigureAwait(true);
        }
    }

    private async Task RefreshRuntimeReadinessCoreAsync(bool synchronizeSelectedProfile) {
        try {
            _viewModel.SetHostStatus("Loading tool packs...");
            if (synchronizeSelectedProfile) {
                await _runtime.SynchronizeSelectedProfileAndRefreshSessionPolicyAsync(_lifetimeCts.Token)
                    .ConfigureAwait(true);
            } else {
                await _runtime.RefreshSessionPolicyAsync(_lifetimeCts.Token).ConfigureAwait(true);
            }
            if (_viewModel.AuthenticationState == NativeAuthenticationState.SignedIn) {
                _viewModel.SetHostStatus(string.Empty);
            }
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            // Window shutdown owns cancellation of the background readiness refresh.
        } catch (Exception ex) {
            StartupLog.Write("Native runtime readiness refresh failed: " + ex);
            if (!_lifetimeCts.IsCancellationRequested) {
                _viewModel.SetHostStatus("Signed in, but tools could not be loaded: " + ex.Message);
            }
        }
    }

    private async Task StartSignInFromNativeAsync() {
        var signInTask = _viewModel.StartSignInAsync(_lifetimeCts.Token);
        _interactiveSignInTask = signInTask;
        NativeLoginResult login;
        try {
            login = await signInTask.ConfigureAwait(true);
        } catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) {
            return;
        }
        if (_lifetimeCts.IsCancellationRequested) {
            return;
        }

        if (login.IsAuthenticated) {
            await RefreshRuntimeReadinessAsync().ConfigureAwait(true);
        }
        UpdateCommandState();
    }

    private void ApplyAuthenticationChrome() {
        var (background, border, foreground) = _viewModel.AuthenticationState switch {
            NativeAuthenticationState.SignedIn => (NativeControlBrushes.SuccessSoft, NativeControlBrushes.SuccessBorder, NativeControlBrushes.Success),
            NativeAuthenticationState.Checking => (NativeControlBrushes.AccentSoft, NativeControlBrushes.InfoBorder, NativeControlBrushes.Accent),
            NativeAuthenticationState.Failed => (NativeControlBrushes.ErrorSoft, NativeControlBrushes.ErrorBorder, NativeControlBrushes.ErrorText),
            NativeAuthenticationState.Required => (NativeControlBrushes.WarningSoft, NativeControlBrushes.WarningBorder, NativeControlBrushes.WarningText),
            _ => (NativeControlBrushes.SurfaceMuted, NativeControlBrushes.Border, NativeControlBrushes.TextSecondary)
        };

        _signInStatusChip.Background = background;
        _signInStatusChip.BorderBrush = border;
        _signInText.Foreground = foreground;
        var needsSignIn = _viewModel.AuthenticationState is NativeAuthenticationState.Failed or NativeAuthenticationState.Required;
        _runtimeStatusChip.Background = needsSignIn ? NativeControlBrushes.WarningSoft : NativeControlBrushes.SurfaceMuted;
        _runtimeStatusChip.BorderBrush = needsSignIn ? NativeControlBrushes.WarningBorder : NativeControlBrushes.Border;
        _runtimeStatusText.Foreground = needsSignIn ? NativeControlBrushes.WarningText : NativeControlBrushes.TextSecondary;
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
