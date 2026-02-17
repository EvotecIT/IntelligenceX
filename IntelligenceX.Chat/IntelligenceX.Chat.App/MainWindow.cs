using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

/// <summary>
/// WebView-first chat window for IntelligenceX desktop MVP.
/// </summary>
public sealed partial class MainWindow : Window {
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmMouseHWheel = 0x020E;
    private const uint WmPointerWheel = 0x024E;
    private const uint WmPointerHWheel = 0x024F;
    private const int WhMouseLl = 14;
    private const int GwlWndProc = -4;
    private const int HtCaption = 0x0002;
    private const int VkLButton = 0x0001;
    private const int MaxConversations = 40;
    private const int MaxMessagesPerConversation = 250;
    private const int MaxQueuedTurns = 8;
    private const int MaxActivityTimelineEntries = 6;
    private const int MaxActivityTimelineLabelChars = 48;
    private const string SystemConversationId = "chat-system";
    private const string SystemConversationTitle = "System";
    private const string DefaultConversationTitle = "New Chat";
    private const string DefaultLocalModel = "gpt-5.3-codex";
    private const string TransportNative = "native";
    private const string TransportCompatibleHttp = "compatible-http";
    private const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";
    private const string DefaultLmStudioBaseUrl = "http://127.0.0.1:1234/v1";
    private static readonly TimeSpan StreamingTranscriptRenderCadence = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PersistDebounceInterval = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan UiPublishCoalesceInterval = TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan TurnWatchdogTickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TurnWatchdogHintThreshold = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WheelForwardCoalesceInterval = TimeSpan.FromMilliseconds(12);
    private static readonly Regex UserNameIntentRegex = new(@"\b(?:you can call me|call me|my name is|name is|set my name to|change my name to)\s+(?<value>[^,\.\!\?\r\n]{1,64})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PersonaIntentRegex = new(@"\b(?:assistant\s+persona|persona|style|tone|mode)\s*(?:is|to|=|:)\s*(?<value>[^,\.\!\?\r\n]{2,180})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PersonaUseIntentRegex = new(@"\b(?:use|switch to|go with)\s+(?<value>[^,\.\!\?\r\n]{2,180})\s+(?:persona|style|tone|mode)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ThemeIntentRegex = new(
        $@"\b(?:theme)\s*(?:is|to|=|:)?\s*(?<value>{ThemeContract.ThemeValueRegexAlternation})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ThemeUseIntentRegex = new(
        $@"\b(?:use|switch to|set)\s+(?<value>{ThemeContract.ThemeValueRegexAlternation})\s+theme\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SessionScopeIntentRegex = new(@"\b(?:for\s+(?:this\s+)?(?:session|chat)|this\s+session(?:\s+only)?|for\s+now|temporar(?:y|ily)|just\s+for\s+now|only\s+for\s+this\s+conversation)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ProfileScopeIntentRegex = new(@"\b(?:save|remember|default(?:\s+profile)?|always|permanent(?:ly)?|persist(?:ent|ently)?|for\s+all\s+future\s+chats)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MemoryRememberIntentRegex = new(@"\b(?:remember(?:\s+this|\s+that)?|save\s+this|store\s+this)\b[\s,:-]*(?<value>[^\r\n]{6,240})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MemoryFutureIntentRegex = new(@"\bfor\s+next\s+time\b[\s,:-]*(?<value>[^\r\n]{6,220})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointNative lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectNative lpRect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private readonly DispatcherQueue _dispatcher;
    private readonly WebView2 _webView;
    private delegate IntPtr WindowProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private WindowProcDelegate? _windowProcDelegate;
    private LowLevelMouseProc? _globalMouseProcDelegate;
    private IntPtr _globalMouseHookHandle = IntPtr.Zero;
    private bool _globalWheelObservedLogged;
    private IntPtr _windowHandle = IntPtr.Zero;
    private readonly object _windowHookSync = new();
    private readonly Dictionary<IntPtr, IntPtr> _hookedWindowProcs = new();
    private bool _windowHookInstalled;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private bool _nativeTitleBarRegionsActive;
    private UiHostRect? _cachedTitleBarRect;
    private readonly List<UiHostRect> _cachedNoDragRects = new();
    private AppWindow? _trackedAppWindow;
    private XamlRoot? _trackedXamlRoot;
    private bool _titleBarMetricsRefreshScheduled;
    private readonly object _wheelForwardSync = new();
    private int _queuedWheelDelta;
    private bool _queuedWheelFromGlobal;
    private bool _queuedWheelFromPointer;
    private bool _wheelForwardFlushScheduled;
    private bool _windowIsActive;
    private bool _nativeWheelObserved;
    private static readonly MarkdownRendererOptions MarkdownOptions = MarkdownRendererPresets.CreateChatStrictMinimal();
    private static readonly bool VerboseServiceLogs = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_VERBOSE_SERVICE_LOGS"));
    private static readonly bool DetachedServiceMode = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_DETACHED_SERVICE"));
    private static readonly GlobalWheelHookMode WheelHookMode = ResolveGlobalWheelHookMode(Environment.GetEnvironmentVariable("IXCHAT_WHEEL_HOOK_MODE"));

    private readonly string _pipeName = "intelligencex.chat";
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private bool _webViewReady;
    private bool _startupInitialized;
    private int _startupFlowState;

    private ChatServiceClient? _client;
    private string? _threadId;
    private int _nextRequestId = 1;
    private (string LoginId, string PromptId, string PromptText)? _pendingLoginPrompt;
    private readonly object _queuedAfterLoginSync = new();
    private readonly Queue<QueuedTurn> _queuedTurnsAfterLogin = new();
    private bool _isAuthenticated;
    private bool _loginInProgress;
    private bool _debugMode;
    private bool _isConnected;
    private string _statusText = SessionStatusFormatter.Format(SessionStatus.Disconnected());
    private SessionStatusTone _statusTone = SessionStatusToneResolver.Resolve(SessionStatus.Disconnected());
    private bool _usageLimitSwitchRecommended;
    private string _timestampMode = ResolveTimestampMode(Environment.GetEnvironmentVariable("IXCHAT_TIME_FORMAT"));
    private string _timestampFormat = ResolveTimestampFormat(Environment.GetEnvironmentVariable("IXCHAT_TIME_FORMAT"));
    private int? _autonomyMaxToolRounds;
    private bool? _autonomyParallelTools;
    private int? _autonomyTurnTimeoutSeconds;
    private int? _autonomyToolTimeoutSeconds;
    private bool? _autonomyWeightedToolRouting;
    private int? _autonomyMaxCandidateTools;
    private bool? _autonomyPlanExecuteReviewLoop;
    private int? _autonomyMaxReviewPasses;
    private int? _autonomyModelHeartbeatSeconds;
    private bool _proactiveModeEnabled = true;
    private string _exportSaveMode = ExportPreferencesContract.DefaultSaveMode;
    private string _exportDefaultFormat = ExportPreferencesContract.DefaultFormat;
    private string? _lastExportDirectory;
    private bool _persistentMemoryEnabled = true;
    private string _localProviderTransport = TransportNative;
    private string? _localProviderBaseUrl;
    private string _localProviderModel = DefaultLocalModel;
    private bool _localRuntimeDetectionRan;
    private bool _localRuntimeLmStudioAvailable;
    private bool _localRuntimeOllamaAvailable;
    private string? _localRuntimeDetectedName;
    private string? _localRuntimeDetectedBaseUrl;
    private string? _localRuntimeDetectionWarning;
    private ModelInfoDto[] _availableModels = Array.Empty<ModelInfoDto>();
    private string[] _favoriteModels = Array.Empty<string>();
    private string[] _recentModels = Array.Empty<string>();
    private bool _modelListIsStale;
    private string? _modelListWarning;
    private string[] _serviceProfileNames = Array.Empty<string>();
    private SessionPolicyDto? _sessionPolicy;
    private readonly Dictionary<string, bool> _toolStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _toolTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolPackIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolPackNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolParameterDto[]> _toolParameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolRoutingConfidence = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolRoutingReason = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _toolRoutingScore = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startupToolHealthWarningSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemorySemanticVectorCacheEntry> _memorySemanticVectorCache = new(StringComparer.OrdinalIgnoreCase);
    // Guards memory semantic cache + memory diagnostics snapshot/history across UI/async paths.
    private readonly object _memoryDiagnosticsSync = new();
    private MemoryDebugSnapshot? _lastMemoryDebugSnapshot;
    private readonly List<MemoryDebugSnapshot> _memoryDebugHistory = new();
    private int _memoryDebugSequence;
    private string _appProfileName = ResolveAppProfileName(Environment.GetEnvironmentVariable("IXCHAT_PROFILE"));
    private readonly ChatAppStateStore _stateStore = new(ChatAppStateStore.GetDefaultDbPath());
    private readonly SemaphoreSlim _stateWriteGate = new(1, 1);
    private readonly SemaphoreSlim _onboardingGate = new(1, 1);
    private ChatAppState _appState = new();
    private readonly HashSet<string> _knownProfiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _appStateLoaded;
    private bool _isSending;
    private readonly object _pendingTurnQueueSync = new();
    private readonly Queue<QueuedTurn> _pendingTurns = new();
    private string? _activeTurnRequestId;
    private string? _latestTurnRequestId;
    private string? _activeKickoffRequestId;
    private string? _cancelRequestedTurnRequestId;
    private readonly object _turnDiagnosticsSync = new();
    private readonly List<string> _activityTimeline = new();
    private TurnMetricsSnapshot? _lastTurnMetrics;
    private long? _activeTurnQueueWaitMs;
    private long _activeTurnStartedUtcTicks;
    private readonly object _turnWatchdogSync = new();
    private CancellationTokenSource? _turnWatchdogCts;
    private string _latestServiceActivityText = string.Empty;
    private bool _activeTurnReceivedDelta;
    private bool _modelKickoffAttempted;
    private bool _modelKickoffInProgress;
    private bool _autoSignInAttempted;
    private string _themePreset = "default";
    private string? _sessionUserNameOverride;
    private string? _sessionAssistantPersonaOverride;
    private string? _sessionThemeOverride;
    private bool _queueAutoDispatchEnabled = true;
    private string? _activeRequestConversationId;
    private string _activeConversationId = "chat-default";
    private readonly List<ConversationRuntime> _conversations = new();
    private List<(string Role, string Text, DateTime Time)> _messages = new();
    private readonly StringBuilder _assistantStreaming = new();
    private readonly SemaphoreSlim _transcriptRenderGate = new(1, 1);
    private long _transcriptRenderGeneration;
    private long _transcriptLastRenderUtcTicks;
    private readonly object _uiPublishSync = new();
    private bool _uiPublishPumpRunning;
    private bool _pendingSessionStatePublish;
    private bool _pendingOptionsStatePublish;
    private TaskCompletionSource<object?>? _pendingSessionStatePublishTcs;
    private TaskCompletionSource<object?>? _pendingOptionsStatePublishTcs;
    private TaskCompletionSource<object?>? _activeSessionStatePublishTcs;
    private TaskCompletionSource<object?>? _activeOptionsStatePublishTcs;
    private CancellationTokenSource? _uiPublishPumpCts;
    private readonly object _persistDebounceSync = new();
    private CancellationTokenSource? _persistDebounceCts;
    private bool _persistDebounceWorkerRunning;
    private bool _persistDebounceRequested;
    private Task? _persistDebounceWorkerTask;
    private volatile bool _shutdownRequested;

    private Process? _serviceProcess;
    private string? _servicePipeName;
    private string? _stagedServiceDir;
    private ServiceLaunchProfileOptions? _pendingServiceLaunchProfileOptions;
    private readonly object _aliveProbeSync = new();
    private ChatServiceClient? _aliveProbeClient;
    private long _aliveProbeTicksUtc;
    private readonly object _autoReconnectSync = new();
    private CancellationTokenSource? _autoReconnectCts;
    private Task? _autoReconnectTask;

    private sealed class ConversationRuntime {
        public required string Id { get; init; }
        public string Title { get; set; } = DefaultConversationTitle;
        public string? ThreadId { get; set; }
        public List<(string Role, string Text, DateTime Time)> Messages { get; } = new();
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed record QueuedTurn(string Text, string? ConversationId, DateTime EnqueuedUtc);

    private sealed record TurnMetricsSnapshot(
        DateTime CompletedUtc,
        long DurationMs,
        long? TtftMs,
        long? QueueWaitMs,
        int ToolCallsCount,
        int ToolRounds,
        int ProjectionFallbackCount,
        string Outcome,
        string? ErrorCode);

    private sealed class UserProfileIntent {
        public string? UserName { get; set; }
        public bool HasUserName { get; set; }
        public string? AssistantPersona { get; set; }
        public bool HasAssistantPersona { get; set; }
        public string? ThemePreset { get; set; }
        public bool HasThemePreset { get; set; }
        public ProfileUpdateScope Scope { get; set; }
    }

    private sealed class MemorySemanticVectorCacheEntry {
        public required string Signature { get; init; }
        public required Dictionary<int, double> Vector { get; init; }
    }

    private sealed class ServiceLaunchProfileOptions {
        public string? LoadProfileName { get; init; }
        public string? SaveProfileName { get; init; }
        public string? Model { get; init; }
        public string? OpenAITransport { get; init; }
        public string? OpenAIBaseUrl { get; init; }
        public string? OpenAIApiKey { get; init; }
        public bool ClearOpenAIApiKey { get; init; }
        public bool? OpenAIStreaming { get; init; }
        public bool? OpenAIAllowInsecureHttp { get; init; }
    }

    private sealed class MemoryDebugSnapshot {
        public DateTime UpdatedUtc { get; init; }
        public int Sequence { get; init; }
        public int AvailableFacts { get; init; }
        public int CandidateFacts { get; init; }
        public int SelectedFacts { get; init; }
        public int UserTokenCount { get; init; }
        public double TopScore { get; init; }
        public double TopSemanticSimilarity { get; init; }
        public double AverageSelectedSimilarity { get; init; }
        public double AverageSelectedRelevance { get; init; }
        public int CacheEntries { get; init; }
        public string Quality { get; init; } = "none";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum GlobalWheelHookMode {
        Auto,
        Always,
        Off
    }

    private readonly struct UiHostRect {
        public UiHostRect(double x, double y, double width, double height) {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookStruct {
        public PointNative Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    /// <summary>
    /// Initializes the desktop chat window.
    /// </summary>
    public MainWindow() {
        StartupLog.Write("MainWindow.ctor enter");
        Title = "IntelligenceX Chat";
        _debugMode = VerboseServiceLogs;

        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _webView = new WebView2();
        Content = _webView;
        ConfigureWindowPlacement();

        Activated += (_, args) => {
            StartupLog.Write("MainWindow.Activated");
            _windowIsActive = args.WindowActivationState != WindowActivationState.Deactivated;
            RefreshGlobalWheelHookPolicy();
            EnsureRestoredIfMinimized();

            if (Interlocked.CompareExchange(ref _startupFlowState, 1, 0) != 0) {
                return;
            }

            _ = RunStartupFlowAsync();
        };

        Closed += async (_, _) => {
            StopAutoReconnectLoop();
            await CancelQueuedPersistAppStateAsync().ConfigureAwait(false);
            await PersistAppStateAsync(allowDuringShutdown: true).ConfigureAwait(false);
            CancelQueuedUiPublishesForShutdown();
            await DisposeClientAsync().ConfigureAwait(false);
            StopServiceIfOwned();
            DetachNativeTitleBarEventSubscriptions();
            UninstallGlobalWheelHook();
            UninstallWindowMessageHook();
            _stateStore.Dispose();
        };
    }

    private async Task RunStartupFlowAsync() {
        try {
            StartupLog.Write("MainWindow.StartupFlow begin");
            await EnsureWebViewInitializedAsync().ConfigureAwait(false);
            await EnsureAppStateLoadedAsync().ConfigureAwait(false);
            await EnsureStartupConnectedAsync().ConfigureAwait(false);
            await EnsureFirstRunAuthenticatedAsync().ConfigureAwait(false);
            await EnsureOnboardingStartedAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _startupFlowState, 2);
            StartupLog.Write("MainWindow.StartupFlow done");
        } catch (Exception ex) {
            Interlocked.Exchange(ref _startupFlowState, 0);
            StartupLog.Write("MainWindow.StartupFlow failed: " + ex);
        }
    }

    private void EnsureRestoredIfMinimized() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped
                && overlapped.State == OverlappedPresenterState.Minimized) {
                overlapped.Restore();
            }
        } catch {
            // Ignore.
        }
    }

    private void ConfigureWindowPlacement() {
        try {
            var appWindow = AppWindow;
            if (appWindow is null) {
                return;
            }

            const int width = 760;
            const int height = 900;
            appWindow.Resize(new SizeInt32(width, height));

            var iconPath = EnsureAppIcon();
            if (!string.IsNullOrEmpty(iconPath)) {
                appWindow.SetIcon(iconPath);
            }

            var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            var x = area.X + Math.Max(0, (area.Width - width) / 2);
            var y = area.Y + Math.Max(0, (area.Height - height) / 2);
            appWindow.Move(new PointInt32(x, y));

            if (appWindow.Presenter is OverlappedPresenter overlapped) {
                overlapped.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            }

            EnsureNativeTitleBarRegionSupport();
        } catch (Exception ex) {
            StartupLog.Write("ConfigureWindowPlacement failed: " + ex.Message);
        }
    }

    private async Task EnsureWebViewInitializedAsync() {
        if (_webViewReady) {
            return;
        }

        try {
            Task navReadyTask = Task.CompletedTask;
            await RunOnUiThreadAsync(async () => {
                if (_webViewReady) {
                    return;
                }

                StartupLog.Write("EnsureWebViewInitializedAsync begin");
                InstallWindowMessageHook();
                RefreshGlobalWheelHookPolicy();
                await _webView.EnsureCoreWebView2Async().AsTask().ConfigureAwait(false);
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ConfigureWebViewLocalAssetMapping();

                // WebView2 in WinUI 3 can swallow wheel events before they
                // reach web content. Intercept at the XAML level and forward.
                _webView.AddHandler(UIElement.PointerWheelChangedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => {
                        var delta = e.GetCurrentPoint(_webView).Properties.MouseWheelDelta;
                        if (delta != 0 && _webViewReady) {
                            RecordNativeWheelObserved();
                            QueueWheelForward(delta, fromGlobalHook: false);
                            // Keep native WebView wheel delivery as fallback for device-specific paths.
                            e.Handled = false;
                        }
                    }), true);

                var navTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnNavigationCompleted(WebView2 _, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs __) {
                    _webView.NavigationCompleted -= OnNavigationCompleted;
                    navTcs.TrySetResult(null);
                }
                _webView.NavigationCompleted += OnNavigationCompleted;
                _webView.NavigateToString(BuildShellHtml());
                navReadyTask = navTcs.Task;
                _webViewReady = true;
                EnsureNativeTitleBarEventSubscriptions();
                RequestTitleBarMetricsRefresh();
            }).ConfigureAwait(false);

            await Task.WhenAny(navReadyTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => {
                InstallWindowMessageHook();
                EnsureNativeTitleBarEventSubscriptions();
                RequestTitleBarMetricsRefresh();
                RefreshGlobalWheelHookPolicy();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            await RenderTranscriptAsync().ConfigureAwait(false);
            await PublishOptionsStateAsync().ConfigureAwait(false);
            StartupLog.Write("EnsureWebViewInitializedAsync ok");
        } catch (Exception ex) {
            StartupLog.Write("EnsureWebViewInitializedAsync failed: " + ex);
            throw;
        }
    }

    private void ConfigureWebViewLocalAssetMapping() {
        try {
            var uiDir = Path.Combine(AppContext.BaseDirectory, "Ui");
            if (!Directory.Exists(uiDir)) {
                return;
            }

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ixchat.local",
                uiDir,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        } catch (Exception ex) {
            StartupLog.Write("ConfigureWebViewLocalAssetMapping failed: " + ex.Message);
        }
    }

    private void InstallWindowMessageHook() {
        try {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (_windowHandle == IntPtr.Zero) {
                StartupLog.Write("InstallWindowMessageHook skipped: hwnd not ready");
                return;
            }

            if (_windowProcDelegate is null) {
                _windowProcDelegate = WindowMessageProc;
            }

            var installedAny = false;
            if (TryHookWindow(_windowHandle)) {
                installedAny = true;
            }

            if (EnumChildWindows(_windowHandle, (child, _) => {
                TryHookWindow(child);
                return true;
            }, IntPtr.Zero)) {
                installedAny = true;
            }

            lock (_windowHookSync) {
                _windowHookInstalled = _hookedWindowProcs.Count > 0;
            }

            if (installedAny || _windowHookInstalled) {
                StartupLog.Write("InstallWindowMessageHook ok");
            }
        } catch (Exception ex) {
            StartupLog.Write("InstallWindowMessageHook failed: " + ex.Message);
        }
    }

    private void InstallGlobalWheelHook() {
        try {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (_windowHandle == IntPtr.Zero) {
                StartupLog.Write("InstallGlobalWheelHook skipped: hwnd not ready");
                return;
            }

            if (_globalMouseHookHandle != IntPtr.Zero) {
                return;
            }

            if (_globalMouseProcDelegate is null) {
                _globalMouseProcDelegate = GlobalMouseHookProc;
            }

            var moduleHandle = GetModuleHandle(null);
            _globalMouseHookHandle = SetWindowsHookEx(WhMouseLl, _globalMouseProcDelegate, moduleHandle, 0);
            if (_globalMouseHookHandle == IntPtr.Zero) {
                var err = Marshal.GetLastWin32Error();
                StartupLog.Write("InstallGlobalWheelHook failed: " + err.ToString(CultureInfo.InvariantCulture));
                return;
            }

            StartupLog.Write("InstallGlobalWheelHook ok");
        } catch (Exception ex) {
            StartupLog.Write("InstallGlobalWheelHook failed: " + ex.Message);
        }
    }

    private void UninstallGlobalWheelHook() {
        try {
            if (_globalMouseHookHandle != IntPtr.Zero) {
                _ = UnhookWindowsHookEx(_globalMouseHookHandle);
                _globalMouseHookHandle = IntPtr.Zero;
            }
        } catch (Exception ex) {
            StartupLog.Write("UninstallGlobalWheelHook failed: " + ex.Message);
        } finally {
            _globalMouseProcDelegate = null;
        }
    }

    private bool TryHookWindow(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero) {
            return false;
        }

        lock (_windowHookSync) {
            if (_hookedWindowProcs.ContainsKey(hWnd)) {
                return false;
            }
        }

        if (_windowProcDelegate is null) {
            return false;
        }

        var procPtr = Marshal.GetFunctionPointerForDelegate(_windowProcDelegate);
        var previousProc = SetWindowLongPtr(hWnd, GwlWndProc, procPtr);
        var err = Marshal.GetLastWin32Error();
        if (previousProc == IntPtr.Zero && err != 0) {
            StartupLog.Write("TryHookWindow failed for hwnd " + hWnd + ": " + err.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        lock (_windowHookSync) {
            _hookedWindowProcs[hWnd] = previousProc;
        }

        return true;
    }

    private void UninstallWindowMessageHook() {
        try {
            List<KeyValuePair<IntPtr, IntPtr>> hooks;
            lock (_windowHookSync) {
                hooks = new List<KeyValuePair<IntPtr, IntPtr>>(_hookedWindowProcs);
                _hookedWindowProcs.Clear();
                _windowHookInstalled = false;
            }

            for (var i = 0; i < hooks.Count; i++) {
                var hwnd = hooks[i].Key;
                var originalProc = hooks[i].Value;
                if (hwnd == IntPtr.Zero || originalProc == IntPtr.Zero) {
                    continue;
                }

                _ = SetWindowLongPtr(hwnd, GwlWndProc, originalProc);
            }
        } catch (Exception ex) {
            StartupLog.Write("UninstallWindowMessageHook failed: " + ex.Message);
        } finally {
            _windowHookInstalled = false;
            _windowProcDelegate = null;
            _windowHandle = IntPtr.Zero;
        }
    }

    private IntPtr GlobalMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            var message = (uint)wParam.ToInt64();
            var isWheelMessage = message == WmMouseWheel || message == WmMouseHWheel;
            if (nCode >= 0 && _webViewReady && isWheelMessage && lParam != IntPtr.Zero && IsForegroundOwnedByCurrentProcess()) {
                var hook = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
                if (!ShouldAcceptGlobalWheelEvent(hook.Point)) {
                    return CallNextHookEx(_globalMouseHookHandle, nCode, wParam, lParam);
                }

                var delta = (short)((hook.MouseData >> 16) & 0xFFFF);
                if (delta != 0) {
                    if (!_globalWheelObservedLogged) {
                        _globalWheelObservedLogged = true;
                        StartupLog.Write("GlobalMouseHookProc observed first wheel event");
                    }

                    QueueWheelForward(delta, fromGlobalHook: true);
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("GlobalMouseHookProc failed: " + ex.Message);
        }

        return CallNextHookEx(_globalMouseHookHandle, nCode, wParam, lParam);
    }

    private static bool IsForegroundOwnedByCurrentProcess() {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0) {
            return false;
        }

        return processId == (uint)Environment.ProcessId;
    }

    private IntPtr WindowMessageProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
        try {
            if (_webViewReady && (msg == WmMouseWheel || msg == WmMouseHWheel || msg == WmPointerWheel || msg == WmPointerHWheel)) {
                var delta = ExtractWheelDelta(wParam);
                if (delta != 0) {
                    RecordNativeWheelObserved();
                    QueueWheelForward(delta, fromGlobalHook: false);
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("WindowMessageProc failed: " + ex.Message);
        }

        IntPtr originalProc;
        lock (_windowHookSync) {
            if (!_hookedWindowProcs.TryGetValue(hWnd, out originalProc)) {
                originalProc = IntPtr.Zero;
            }
        }

        if (originalProc != IntPtr.Zero) {
            return CallWindowProc(originalProc, hWnd, msg, wParam, lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static int ExtractWheelDelta(IntPtr wParam) {
        var value = wParam.ToInt64();
        return (short)((value >> 16) & 0xFFFF);
    }

    private void QueueWheelForward(int delta, bool fromGlobalHook) {
        if (delta == 0) {
            return;
        }

        bool shouldScheduleFlush;
        lock (_wheelForwardSync) {
            _queuedWheelDelta += delta;
            _queuedWheelFromGlobal |= fromGlobalHook;
            _queuedWheelFromPointer |= !fromGlobalHook;
            shouldScheduleFlush = !_wheelForwardFlushScheduled;
            if (shouldScheduleFlush) {
                _wheelForwardFlushScheduled = true;
            }
        }

        if (!shouldScheduleFlush) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(WheelForwardCoalesceInterval).ConfigureAwait(false);
                await RunOnUiThreadAsync(() => {
                    FlushQueuedWheelForward();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Ignore.
            } catch (Exception ex) {
                StartupLog.Write("QueueWheelForward flush failed: " + ex.Message);
            }
        });
    }

    private void FlushQueuedWheelForward() {
        int delta;
        bool fromGlobal;
        bool fromPointer;
        lock (_wheelForwardSync) {
            delta = _queuedWheelDelta;
            fromGlobal = _queuedWheelFromGlobal;
            fromPointer = _queuedWheelFromPointer;
            _queuedWheelDelta = 0;
            _queuedWheelFromGlobal = false;
            _queuedWheelFromPointer = false;
            _wheelForwardFlushScheduled = false;
        }

        if (!_webViewReady || delta == 0) {
            return;
        }

        if (fromPointer) {
            _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelDiagnosticRecordScript(delta));
        }

        if (fromGlobal) {
            _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelGlobalDiagnosticRecordScript(delta));
        }

        _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelForwardScript(delta));
    }

    private void RecordNativeWheelObserved() {
        if (_nativeWheelObserved) {
            return;
        }

        _nativeWheelObserved = true;
        if (WheelHookMode == GlobalWheelHookMode.Auto) {
            StartupLog.Write("Native wheel path observed; disabling global wheel hook (auto mode).");
            RefreshGlobalWheelHookPolicy();
        }
    }

    private void RefreshGlobalWheelHookPolicy() {
        if (_shutdownRequested) {
            UninstallGlobalWheelHook();
            return;
        }

        switch (WheelHookMode) {
            case GlobalWheelHookMode.Off:
                UninstallGlobalWheelHook();
                break;
            case GlobalWheelHookMode.Always:
                InstallGlobalWheelHook();
                break;
            case GlobalWheelHookMode.Auto:
            default:
                if (_webViewReady && _windowIsActive && !_nativeWheelObserved) {
                    InstallGlobalWheelHook();
                } else {
                    UninstallGlobalWheelHook();
                }
                break;
        }
    }

    private bool ShouldAcceptGlobalWheelEvent(PointNative point) {
        if (!_windowIsActive || !_webViewReady) {
            return false;
        }

        var hwnd = _windowHandle;
        if (hwnd == IntPtr.Zero) {
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _windowHandle = hwnd;
        }

        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) {
            return true;
        }

        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    private void EnsureNativeTitleBarEventSubscriptions() {
        try {
            var appWindow = AppWindow;
            if (!ReferenceEquals(_trackedAppWindow, appWindow)) {
                if (_trackedAppWindow is not null) {
                    _trackedAppWindow.Changed -= OnAppWindowChanged;
                }

                _trackedAppWindow = appWindow;
                if (_trackedAppWindow is not null) {
                    _trackedAppWindow.Changed += OnAppWindowChanged;
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("EnsureNativeTitleBarEventSubscriptions(appWindow) failed: " + ex.Message);
        }

        try {
            var xamlRoot = _webView.XamlRoot;
            if (!ReferenceEquals(_trackedXamlRoot, xamlRoot)) {
                if (_trackedXamlRoot is not null) {
                    _trackedXamlRoot.Changed -= OnWebViewXamlRootChanged;
                }

                _trackedXamlRoot = xamlRoot;
                if (_trackedXamlRoot is not null) {
                    _trackedXamlRoot.Changed += OnWebViewXamlRootChanged;
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("EnsureNativeTitleBarEventSubscriptions(xamlRoot) failed: " + ex.Message);
        }
    }

    private void DetachNativeTitleBarEventSubscriptions() {
        try {
            if (_trackedAppWindow is not null) {
                _trackedAppWindow.Changed -= OnAppWindowChanged;
            }
        } catch (Exception ex) {
            StartupLog.Write("DetachNativeTitleBarEventSubscriptions(appWindow) failed: " + ex.Message);
        } finally {
            _trackedAppWindow = null;
        }

        try {
            if (_trackedXamlRoot is not null) {
                _trackedXamlRoot.Changed -= OnWebViewXamlRootChanged;
            }
        } catch (Exception ex) {
            StartupLog.Write("DetachNativeTitleBarEventSubscriptions(xamlRoot) failed: " + ex.Message);
        } finally {
            _trackedXamlRoot = null;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) {
        if (_shutdownRequested) {
            return;
        }

        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) {
            RequestTitleBarMetricsRefresh();
        }
    }

    private void OnWebViewXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) {
        if (_shutdownRequested) {
            return;
        }

        ReapplyCachedNativeTitleBarRegions();
        RequestTitleBarMetricsRefresh();
    }

    private void RequestTitleBarMetricsRefresh() {
        if (!_webViewReady || _shutdownRequested) {
            return;
        }

        if (_titleBarMetricsRefreshScheduled) {
            return;
        }

        _titleBarMetricsRefreshScheduled = true;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(32).ConfigureAwait(false);
                await RunOnUiThreadAsync(() => {
                    _titleBarMetricsRefreshScheduled = false;
                    if (_webViewReady) {
                        _ = _webView.ExecuteScriptAsync("window.ixPostTitlebarMetrics && window.ixPostTitlebarMetrics();");
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            } catch {
                _titleBarMetricsRefreshScheduled = false;
            }
        });
    }

    private async Task EnsureStartupConnectedAsync() {
        if (_startupInitialized) {
            return;
        }

        _startupInitialized = true;
        await ConnectAsync(fromUserAction: false).ConfigureAwait(false);
    }

    private async Task EnsureFirstRunAuthenticatedAsync() {
        if (_autoSignInAttempted) {
            return;
        }

        if (_appState.OnboardingCompleted || AnyConversationHasMessages() || _isAuthenticated || _loginInProgress) {
            return;
        }

        _autoSignInAttempted = true;
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
    }

    private async Task EnsureAppStateLoadedAsync() {
        if (_appStateLoaded) {
            return;
        }

        _appStateLoaded = true;

        try {
            var names = await _stateStore.ListProfileNamesAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var name in names) {
                if (!string.IsNullOrWhiteSpace(name)) {
                    _knownProfiles.Add(name.Trim());
                }
            }

            await LoadProfileStateAsync(_appProfileName, render: true).ConfigureAwait(false);
        } catch (Exception ex) {
            AppendSystem(SystemNotice.StateLoadFailed(ex.Message));
            _appState = new ChatAppState { ProfileName = _appProfileName };
        }
    }

    private async Task LoadProfileStateAsync(string profileName, bool render) {
        var normalized = ResolveAppProfileName(profileName);
        var loaded = await _stateStore.GetAsync(normalized, CancellationToken.None).ConfigureAwait(false);

        _appProfileName = normalized;
        _knownProfiles.Add(normalized);
        _appState = loaded ?? new ChatAppState { ProfileName = normalized };
        _sessionUserNameOverride = null;
        _sessionAssistantPersonaOverride = null;
        _sessionThemeOverride = null;

        _themePreset = NormalizeTheme(_appState.ThemePreset) ?? "default";
        _appState.ThemePreset = _themePreset;
        _localProviderTransport = NormalizeLocalProviderTransport(_appState.LocalProviderTransport);
        _localProviderBaseUrl = NormalizeLocalProviderBaseUrl(_appState.LocalProviderBaseUrl, _localProviderTransport);
        _localProviderModel = NormalizeLocalProviderModel(_appState.LocalProviderModel, _localProviderTransport);
        _appState.LocalProviderTransport = _localProviderTransport;
        _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
        _appState.LocalProviderModel = _localProviderModel;
        _localRuntimeDetectionRan = false;
        _localRuntimeLmStudioAvailable = false;
        _localRuntimeOllamaAvailable = false;
        _localRuntimeDetectedName = null;
        _localRuntimeDetectedBaseUrl = null;
        _localRuntimeDetectionWarning = null;
        _availableModels = Array.Empty<ModelInfoDto>();
        _favoriteModels = Array.Empty<string>();
        _recentModels = Array.Empty<string>();
        _modelListIsStale = false;
        _modelListWarning = null;
        _serviceProfileNames = Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(_appState.TimestampMode)) {
            _timestampMode = ResolveTimestampMode(_appState.TimestampMode);
            _timestampFormat = ResolveTimestampFormat(_appState.TimestampMode);
        }
        _autonomyMaxToolRounds = NormalizeAutonomyInt(_appState.AutonomyMaxToolRounds, min: 1, max: 64);
        _autonomyParallelTools = _appState.AutonomyParallelTools;
        _autonomyTurnTimeoutSeconds = NormalizeAutonomyInt(_appState.AutonomyTurnTimeoutSeconds, min: 0, max: 3600);
        _autonomyToolTimeoutSeconds = NormalizeAutonomyInt(_appState.AutonomyToolTimeoutSeconds, min: 0, max: 3600);
        _autonomyWeightedToolRouting = _appState.AutonomyWeightedToolRouting;
        _autonomyMaxCandidateTools = NormalizeAutonomyInt(_appState.AutonomyMaxCandidateTools, min: 0, max: 64);
        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _exportSaveMode = ExportPreferencesContract.NormalizeSaveMode(_appState.ExportSaveMode);
        _appState.ExportSaveMode = _exportSaveMode;
        _exportDefaultFormat = ExportPreferencesContract.NormalizeFormat(_appState.ExportDefaultFormat);
        _appState.ExportDefaultFormat = _exportDefaultFormat;
        _lastExportDirectory = ExportPreferencesContract.NormalizeDirectory(_appState.ExportLastDirectory);
        _appState.ExportLastDirectory = _lastExportDirectory;
        _queueAutoDispatchEnabled = _appState.QueueAutoDispatchEnabled;
        _appState.QueueAutoDispatchEnabled = _queueAutoDispatchEnabled;
        _proactiveModeEnabled = _appState.ProactiveModeEnabled;
        _appState.ProactiveModeEnabled = _proactiveModeEnabled;
        _persistentMemoryEnabled = _appState.PersistentMemoryEnabled;
        _appState.PersistentMemoryEnabled = _persistentMemoryEnabled;
        _appState.MemoryFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
        ResetMemoryDiagnosticsState();

        var repairedLegacyTranscriptState = LoadConversationsFromState(_appState);
        ActivateConversation(ResolveInitialConversationId(_appState));
        if (repairedLegacyTranscriptState) {
            await PersistAppStateAsync().ConfigureAwait(false);
        }

        var knownToolNames = new List<string>(_toolDescriptions.Keys);
        _modelKickoffAttempted = _messages.Count > 0;
        _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();

        _toolStates.Clear();
        ClearToolRoutingInsights();
        foreach (var toolName in knownToolNames) {
            _toolStates[toolName] = true;
        }

        if (_appState.DisabledTools is { Count: > 0 }) {
            foreach (var tool in _appState.DisabledTools) {
                if (!string.IsNullOrWhiteSpace(tool)) {
                    _toolStates[tool.Trim()] = false;
                }
            }
        }

        if (!render) {
            return;
        }

        await RenderTranscriptAsync().ConfigureAwait(false);
        await ApplyThemeFromStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

    private bool AnyConversationHasMessages() {
        foreach (var conversation in _conversations) {
            if (conversation.Messages.Count > 0) {
                return true;
            }
        }

        return false;
    }
}
