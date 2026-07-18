using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Launch;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native WinUI chat shell used by the rebuild path.
/// </summary>
internal sealed partial class NativeChatWindow : Window {
    private readonly NativeChatServiceRuntime _runtime;
    private readonly NativeConversationStateStore _conversationStore;
    private readonly NativeChatViewModel _viewModel;
    private TextBox _composer = null!;
    private Button _sendButton = null!;
    private Button _stopButton = null!;
    private Button _checkSignInButton = null!;
    private Button _signInButton = null!;
    private Button _exportButton = null!;
    private Border _signInStatusChip = null!;
    private Border _runtimeStatusChip = null!;
    private TextBlock _signInText = null!;
    private TextBlock _runtimeStatusText = null!;
    private TextBlock _workspaceTitleText = null!;
    private TextBlock _workspaceSubtitleText = null!;
    private ListView _sidebarItemsPanel = null!;
    private TextBox _sidebarSearchBox = null!;
    private TextBlock _selectedContextText = null!;
    private MainWindow? _settingsWindow;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task _settingsReloadTask = Task.CompletedTask;
    private Task _runtimeReadinessTask = Task.CompletedTask;
    private ItemsRepeater _transcriptItems = null!;
    private ScrollViewer _transcriptScroll = null!;
    private Grid _emptyTranscriptHost = null!;
    private Grid _root = null!;
    private readonly NativeTranscriptScrollState _transcriptScrollState = new();
    private DispatcherQueueTimer _transcriptScrollRecoveryTimer = null!;
    private long _transcriptScrollRecoveryVersion;

    public NativeChatWindow() {
        Title = "IntelligenceX Chat";
        var profileName = ChatServiceLaunchProfileMapper.NormalizeProfileName(
            Environment.GetEnvironmentVariable("IXCHAT_PROFILE"));
        _conversationStore = new NativeConversationStateStore(profileName: profileName);
        _runtime = new NativeChatServiceRuntime(
            Environment.GetEnvironmentVariable("IXCHAT_SERVICE_PIPE"),
            _conversationStore.CreateServiceLaunchProfileOptions);
        _viewModel = new NativeChatViewModel(
            _runtime,
            DispatchToUiThread,
            _conversationStore,
            conversation => _conversationStore.CreateChatRequestOptions(
                conversation,
                _runtime.SessionPolicy,
                _runtime.ToolDefinitions),
            () => _runtime.SessionPolicy,
            _runtime.EnsureRequestMetadataAsync,
            _conversationStore.BuildRequestText,
            _conversationStore.NormalizeAssistantTurnAsync) {
            OpenLoginUrlAsync = OpenLoginUrlAsync,
            PromptForLoginInputAsync = PromptForLoginInputAsync
        };

        _conversationStore.EffectiveThemeChanged += ApplyNativeTheme;
        _root = BuildShell();
        Content = _root;
        _root.Loaded += async (_, _) => {
            StartupLog.Write("NativeChatWindow root loaded");
            ConfigureWindowPlacement();
            await _viewModel.InitializeConversationsAsync().ConfigureAwait(true);
            RefreshConversationChrome();
            var login = await _viewModel.CheckSignInAsync().ConfigureAwait(true);
            if (login.IsAuthenticated) {
                _ = RefreshRuntimeReadinessAsync();
            }

            UpdateCommandState();
            _composer.Focus(FocusState.Programmatic);
        };

        _viewModel.PropertyChanged += (_, args) => {
            if (args.PropertyName is nameof(NativeChatViewModel.CanSend)
                or nameof(NativeChatViewModel.CanStop)
                or nameof(NativeChatViewModel.CanCheckSignIn)
                or nameof(NativeChatViewModel.CanStartSignIn)) {
                UpdateCommandState();
            }

            if (args.PropertyName is nameof(NativeChatViewModel.SignInText)
                or nameof(NativeChatViewModel.StatusText)
                or nameof(NativeChatViewModel.AuthenticationState)) {
                UpdateViewStateFromViewModel(refreshEmptyState: true);
            }

            if (args.PropertyName is nameof(NativeChatViewModel.Draft)) {
                UpdateViewStateFromViewModel(refreshEmptyState: false);
            }

            if (args.PropertyName is nameof(NativeChatViewModel.ActiveConversation)) {
                RefreshConversationChrome();
            }
        };
        _viewModel.Conversations.CollectionChanged += (_, _) => RenderSidebarItems();
        _viewModel.Transcript.CollectionChanged += (_, _) => {
            RenderTranscript();
            ScrollTranscriptToEnd();
            RefreshConversationChrome();
            UpdateCommandState();
        };
        UpdateCommandState();

        Closed += async (_, _) => {
            _lifetimeCts.Cancel();
            StopTranscriptScrollRecovery();
            _settingsWindow?.Close();
            try {
                await _settingsReloadTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Native shutdown cancels any pending post-settings refresh.
            }
            try {
                await _runtimeReadinessTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Native shutdown cancels any pending tool-catalog refresh.
            }
            await _runtime.DisposeAsync().ConfigureAwait(false);
            await _conversationStore.DisposeAsync().ConfigureAwait(false);
            _lifetimeCts.Dispose();
        };
    }

    private Grid BuildShell() {
        var root = new Grid {
            Background = NativeControlBrushes.AppBackground,
            DataContext = _viewModel,
            RequestedTheme = NativeControlBrushes.RequestedTheme
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = BuildBody();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        return root;
    }

    private void ApplyNativeTheme(string presetName) {
        DispatchToUiThread(() => {
            NativeControlBrushes.ApplyTheme(presetName);
            _root.RequestedTheme = NativeControlBrushes.RequestedTheme;
            _root.Background = NativeControlBrushes.AppBackground;
            NativeArtifactWindow.ApplyCurrentTheme();
            RenderSidebarItems();
            RenderTranscript();
        });
    }

    private FrameworkElement BuildBody() {
        var grid = new Grid {
            ColumnSpacing = 0,
            Padding = new Thickness(0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var workspace = BuildChatWorkspace();
        Grid.SetColumn(workspace, 1);
        grid.Children.Add(workspace);

        return grid;
    }

    private void DispatchToUiThread(Action action) {
        var dispatcher = DispatcherQueue;
        if (dispatcher.HasThreadAccess) {
            action();
            return;
        }

        if (!dispatcher.TryEnqueue(() => action())) {
            throw new InvalidOperationException("The native chat UI dispatcher is no longer available.");
        }
    }

    private void ConfigureWindowPlacement() {
        try {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) {
                StartupLog.Write("NativeChatWindow placement skipped: hwnd is zero");
                return;
            }

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.MoveAndResize(new RectInt32(20, 36, 1120, 760));
            StartupLog.Write("NativeChatWindow placement resized");
        } catch (Exception ex) {
            StartupLog.Write("NativeChatWindow placement failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private async Task OpenLoginUrlAsync(Uri uri) {
        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private async Task<string?> PromptForLoginInputAsync(NativeLoginPrompt prompt) {
        var input = new TextBox {
            AcceptsReturn = false,
            PlaceholderText = "Paste the redirect URL or code",
            MinWidth = 420
        };
        var dialog = new ContentDialog {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Complete sign-in",
            Content = new StackPanel {
                Spacing = 10,
                Children = {
                    new TextBlock {
                        Text = prompt.PromptText,
                        TextWrapping = TextWrapping.Wrap
                    },
                    input
                }
            },
            PrimaryButtonText = "Submit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? input.Text
            : null;
    }
}
