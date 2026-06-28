using System;
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
    private readonly NativeChatServiceTurnRunner _runner;
    private readonly NativeChatViewModel _viewModel;
    private readonly bool _sampleDataRequested;
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
    private StackPanel _sidebarItemsPanel = null!;
    private TextBox _sidebarSearchBox = null!;
    private NativeSidebarItem _selectedSidebarItem = NativeSidebarItem.Default;
    private TextBlock _selectedContextText = null!;
    private ListView _transcriptList = null!;
    private Grid _emptyTranscriptHost = null!;

    public NativeChatWindow() {
        Title = "IntelligenceX Chat";
        _runner = new NativeChatServiceTurnRunner(Environment.GetEnvironmentVariable("IXCHAT_SERVICE_PIPE"));
        _viewModel = new NativeChatViewModel(_runner, DispatchToUiThread) {
            OpenLoginUrlAsync = OpenLoginUrlAsync,
            PromptForLoginInputAsync = PromptForLoginInputAsync
        };
        _sampleDataRequested = IsSampleDataRequested();
        SeedSampleTranscriptIfRequested();

        var root = BuildShell();
        Content = root;
        root.Loaded += async (_, _) => {
            StartupLog.Write("NativeChatWindow root loaded");
            ConfigureWindowPlacement();
            if (_sampleDataRequested) {
                _viewModel.SetHostStatus("Sample data loaded");
                _viewModel.SetHostSignInText("Sample mode");
            } else {
                _ = await _viewModel.CheckSignInAsync().ConfigureAwait(true);
            }

            UpdateCommandState();
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
        };
        _viewModel.Transcript.CollectionChanged += (_, _) => {
            RenderTranscript();
            ScrollTranscriptToEnd();
            UpdateCommandState();
        };
        UpdateCommandState();

        Closed += async (_, _) => {
            await _runner.DisposeAsync().ConfigureAwait(false);
        };
    }

    private Grid BuildShell() {
        var root = new Grid {
            Background = NativeControlBrushes.AppBackground,
            DataContext = _viewModel,
            RequestedTheme = ElementTheme.Light
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

        _ = dispatcher.TryEnqueue(() => action());
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
