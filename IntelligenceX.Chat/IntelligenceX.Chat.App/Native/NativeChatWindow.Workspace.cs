using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private FrameworkElement BuildChatWorkspace() {
        var workspace = new Grid {
            RowSpacing = 0,
            Background = NativeControlBrushes.AppBackground
        };
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildWorkspaceHeader();
        Grid.SetRow(header, 0);
        workspace.Children.Add(header);

        _transcriptItems = new ItemsRepeater {
            Name = "TranscriptItems",
            ItemsSource = _viewModel.Transcript,
            ItemTemplate = new NativeTranscriptElementFactory(),
            Layout = new StackLayout { Spacing = 0 },
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _transcriptScroll = new ScrollViewer {
            Name = "TranscriptScroll",
            Content = _transcriptItems,
            Padding = new Thickness(24, 18, 24, 18),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        _transcriptScroll.ViewChanged += OnTranscriptViewChanged;
        _transcriptScroll.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnTranscriptPointerWheelChanged),
            handledEventsToo: true);
        _transcriptItems.SizeChanged += OnTranscriptItemsSizeChanged;
        _emptyTranscriptHost = new Grid {
            Padding = new Thickness(24, 18, 24, 18)
        };
        var transcriptHost = new Grid {
            Children = {
                _transcriptScroll,
                _emptyTranscriptHost
            }
        };
        var transcriptSurface = new Border {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            Background = NativeControlBrushes.AppBackground,
            Child = transcriptHost
        };
        Grid.SetRow(transcriptSurface, 1);
        workspace.Children.Add(transcriptSurface);

        var composer = BuildComposer();
        Grid.SetRow(composer, 2);
        workspace.Children.Add(composer);

        RenderTranscript();

        return workspace;
    }

    private FrameworkElement BuildWorkspaceHeader() {
        var shell = new Border {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = NativeControlBrushes.Border,
            Background = NativeControlBrushes.Surface,
            Padding = new Thickness(24, 16, 24, 16)
        };
        var grid = new Grid {
            ColumnSpacing = 16
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = grid;

        var stack = new StackPanel {
            Spacing = 2
        };
        _workspaceTitleText = new TextBlock {
            Text = _viewModel.ActiveConversation.Title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        };
        stack.Children.Add(_workspaceTitleText);
        _workspaceSubtitleText = new TextBlock {
            Text = "New conversation",
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        };
        stack.Children.Add(_workspaceSubtitleText);
        Grid.SetColumn(stack, 0);
        grid.Children.Add(stack);

        var actions = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        _exportButton = new Button {
            Content = "Export",
            MinWidth = 74,
            MinHeight = 32
        };
        _exportButton.Click += async (_, _) => await ExportNativeTranscriptAsync().ConfigureAwait(true);
        actions.Children.Add(_exportButton);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return shell;
    }

    private void RenderTranscript() {
        if (_viewModel.Transcript.Count == 0) {
            _emptyTranscriptHost.Children.Clear();
            _emptyTranscriptHost.Children.Add(BuildEmptyTranscriptState());
            _emptyTranscriptHost.Visibility = Visibility.Visible;
            _transcriptItems.Visibility = Visibility.Collapsed;
            return;
        }

        _emptyTranscriptHost.Children.Clear();
        _emptyTranscriptHost.Visibility = Visibility.Collapsed;
        _transcriptItems.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildEmptyTranscriptState() {
        var title = ResolveEmptyStateTitle();
        var body = ResolveEmptyStateBody();
        var detail = ResolveEmptyStateDetail();
        var stack = new StackPanel {
            Spacing = 10,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock {
            Text = title,
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = NativeControlBrushes.TextPrimary
        });
        stack.Children.Add(new TextBlock {
            Text = body,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NativeControlBrushes.TextSecondary
        });
        stack.Children.Add(new Border {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 0),
            Background = NativeControlBrushes.Border
        });
        stack.Children.Add(new TextBlock {
            Text = detail,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NativeControlBrushes.TextSecondary
        });
        var actions = BuildEmptyStateActions();
        if (actions != null) {
            stack.Children.Add(actions);
        }

        return new Grid {
            MinHeight = 420,
            Children = {
                stack
            }
        };
    }

    private string ResolveEmptyStateTitle() {
        return _viewModel.AuthenticationState switch {
            NativeAuthenticationState.Checking => "Checking account and runtime",
            NativeAuthenticationState.SignedIn => "Ready for live chat",
            NativeAuthenticationState.Required => "Sign in to start live chat",
            NativeAuthenticationState.Failed => "Sign-in needs attention",
            _ => "Preparing native chat"
        };
    }

    private string ResolveEmptyStateBody() {
        return _viewModel.AuthenticationState switch {
            NativeAuthenticationState.Checking => "The native app is checking whether the chat service already has an authenticated account.",
            NativeAuthenticationState.SignedIn => "Ask a question, run a safe check, or request AD and Microsoft 365 evidence.",
            NativeAuthenticationState.Required => "Use Sign in in the header, then continue with a normal operator request.",
            NativeAuthenticationState.Failed => _viewModel.StatusText,
            _ => "The native shell is active. Account and runtime status will appear in the header."
        };
    }

    private string ResolveEmptyStateDetail() {
        return _viewModel.AuthenticationState switch {
            NativeAuthenticationState.SignedIn => "Pick a starter or type a request.",
            NativeAuthenticationState.Required => "Authentication is required before live requests can run.",
            NativeAuthenticationState.Failed => "Retry sign-in or recheck the existing session.",
            NativeAuthenticationState.Checking => "This usually completes in a moment.",
            _ => "Runtime status appears in the header."
        };
    }

    private FrameworkElement? BuildEmptyStateActions() {
        var stack = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        switch (_viewModel.AuthenticationState) {
            case NativeAuthenticationState.SignedIn:
                stack.Children.Add(BuildDraftStarterButton("Risky admins", "Review risky inactive admins and show the evidence as tables."));
                stack.Children.Add(BuildDraftStarterButton("M365 exceptions", "Review Microsoft 365 MFA exceptions and summarize remediation options."));
                break;
            case NativeAuthenticationState.Required:
            case NativeAuthenticationState.Failed:
            case NativeAuthenticationState.Unknown:
                stack.Children.Add(BuildActionButton("Sign in", async () => await StartSignInFromNativeAsync().ConfigureAwait(true), primary: true));
                stack.Children.Add(BuildActionButton("Recheck", async () => await CheckSignInFromNativeAsync().ConfigureAwait(true), primary: false));
                break;
            case NativeAuthenticationState.Checking:
                stack.Children.Add(BuildActionButton("Checking", () => Task.CompletedTask, primary: false, enabled: false));
                break;
        }

        return stack.Children.Count == 0 ? null : stack;
    }

    private Button BuildDraftStarterButton(string label, string draft) =>
        BuildActionButton(label, () => {
            _viewModel.Draft = draft;
            _composer.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            return Task.CompletedTask;
        }, primary: false);

    private static Button BuildActionButton(string label, Func<Task> action, bool primary, bool enabled = true) {
        var button = new Button {
            Content = label,
            IsEnabled = enabled,
            MinHeight = 34,
            MinWidth = 92,
            Padding = new Thickness(12, 6, 12, 6)
        };
        if (primary) {
            button.Background = NativeControlBrushes.Accent;
            button.BorderBrush = NativeControlBrushes.Accent;
            button.Foreground = NativeControlBrushes.Surface;
        }

        button.Click += async (_, _) => await action().ConfigureAwait(true);
        return button;
    }

    private void ScrollTranscriptToEnd() {
        if (_viewModel.Transcript.Count == 0) {
            return;
        }

        _isTranscriptFollowingEnd = true;
        QueueTranscriptFollowToEnd();
    }

    private void OnTranscriptViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) {
        var scrollableHeight = _transcriptScroll.ScrollableHeight;
        var extentGrew = scrollableHeight > _lastTranscriptScrollableHeight + 0.5;
        _lastTranscriptScrollableHeight = scrollableHeight;

        if (_transcriptManualScrollInProgress) {
            if (!args.IsIntermediate) {
                _transcriptManualScrollInProgress = false;
                _isTranscriptFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(
                    _transcriptScroll.VerticalOffset,
                    scrollableHeight);
            }

            return;
        }

        if (_transcriptFollowChangeInProgress) {
            if (!args.IsIntermediate) {
                CompleteTranscriptFollowChange();
            }

            return;
        }

        if (_isTranscriptFollowingEnd && extentGrew) {
            QueueTranscriptFollowToEnd();
            return;
        }

        _isTranscriptFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(
            _transcriptScroll.VerticalOffset,
            scrollableHeight);
    }

    private void OnTranscriptItemsSizeChanged(object sender, SizeChangedEventArgs args) {
        if (args.NewSize.Height <= args.PreviousSize.Height + 0.5) {
            return;
        }

        QueueTranscriptFollowToEnd();
    }

    private void OnTranscriptPointerWheelChanged(object sender, PointerRoutedEventArgs args) {
        var properties = args.GetCurrentPoint(_transcriptScroll).Properties;
        if (!NativeTranscriptScrollBehavior.ShouldHandleWheel(
                properties.IsHorizontalMouseWheel,
                properties.MouseWheelDelta)) {
            return;
        }

        args.Handled = HandleTranscriptWheelDelta(properties.MouseWheelDelta);
    }

    private bool HandleTranscriptWheelDelta(int wheelDelta) {
        var currentOffset = _transcriptScroll.VerticalOffset;
        var scrollableHeight = _transcriptScroll.ScrollableHeight;
        var target = NativeTranscriptScrollBehavior.CalculateWheelTarget(
            currentOffset,
            scrollableHeight,
            wheelDelta);
        if (Math.Abs(target - currentOffset) < 0.5) {
            return false;
        }

        _isTranscriptFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(
            target,
            _transcriptScroll.ScrollableHeight);
        _transcriptManualScrollInProgress = true;
        var viewChangeAccepted = _transcriptScroll.ChangeView(
            horizontalOffset: null,
            verticalOffset: target,
            zoomFactor: null,
            disableAnimation: true);
        if (!viewChangeAccepted) {
            _transcriptManualScrollInProgress = false;
        }

        return viewChangeAccepted;
    }

    private void QueueTranscriptFollowToEnd() {
        if (!_isTranscriptFollowingEnd
            || _viewModel.Transcript.Count == 0
            || _transcriptFollowUpdateQueued
            || _transcriptFollowChangeInProgress
            || !NativeTranscriptScrollBehavior.RequiresFollowUpdate(
                _transcriptScroll.VerticalOffset,
                _transcriptScroll.ScrollableHeight)) {
            return;
        }

        _transcriptFollowUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => {
                _transcriptFollowUpdateQueued = false;
                if (!_isTranscriptFollowingEnd
                    || _viewModel.Transcript.Count == 0
                    || !NativeTranscriptScrollBehavior.RequiresFollowUpdate(
                        _transcriptScroll.VerticalOffset,
                        _transcriptScroll.ScrollableHeight)) {
                    return;
                }

                _transcriptFollowChangeInProgress = true;
                if (!_transcriptScroll.ChangeView(
                        horizontalOffset: null,
                        verticalOffset: _transcriptScroll.ScrollableHeight,
                        zoomFactor: null,
                        disableAnimation: true)) {
                    CompleteTranscriptFollowChange();
                }
            })) {
            _transcriptFollowUpdateQueued = false;
        }
    }

    private void CompleteTranscriptFollowChange() {
        if (!_transcriptFollowChangeInProgress) {
            return;
        }

        _transcriptFollowChangeInProgress = false;
        QueueTranscriptFollowToEnd();
    }

}
