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
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.App.Rendering;
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
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmLButtonUp = 0x0202;
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
    private const int MaxRoutingPromptExposureHistoryEntries = 6;
    private const int MaxAssistantTurnTimelineEntries = 8;
    private const string SystemConversationId = "chat-system";
    private const string SystemConversationTitle = "System";
    private const string DefaultConversationTitle = "New Chat";
    private const string DefaultLocalModel = "gpt-5.4";
    private const string TransportNative = "native";
    private const string TransportCompatibleHttp = "compatible-http";
    private const string TransportCopilotCli = "copilot-cli";
    private const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";
    private const string DefaultLmStudioBaseUrl = "http://127.0.0.1:1234/v1";
    private static readonly TimeSpan StreamingTranscriptRenderCadence = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PersistDebounceInterval = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan UiPublishCoalesceInterval = TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan ServiceDrivenSessionPublishMinInterval = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan TurnWatchdogTickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TurnWatchdogHintThreshold = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TurnWatchdogAwaitingAckHintThreshold = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TurnWatchdogAwaitingFirstTokenHintThreshold = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan WheelForwardCoalesceInterval = TimeSpan.FromMilliseconds(12);
    private static readonly TimeSpan DragMoveWatchdogInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan StartupInitialPipeConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartupInitialPipeConnectColdStartTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StartupConnectBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DispatchConnectBudget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StartupDispatchPrewarmConnectBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DispatchConnectFailureCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AutoReconnectConnectBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AutoReconnectBusyTurnDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan AutoReconnectPriorityFirstDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan AutoReconnectPriorityDelayCap = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan AutoReconnectTrackedServiceDelayCap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StartupConnectMinAttemptTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StartupConnectRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartupConnectRetryAttemptCapNonInteractive = TimeSpan.FromSeconds(3);
    private const int StartupConnectRecoveryAttemptLimit = 1;
    private static readonly TimeSpan ServiceStartupExitProbeDelay = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan StartupConnectAttemptHardTimeoutGrace = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan StartupConnectAttemptOutlierThreshold = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan AliveProbeCacheTtl = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan AliveProbeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AliveProbeFastTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan[] AutoReconnectBackoffDelays = {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(20)
    };
    private static readonly TimeSpan KickoffCancelAckTimeout = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan KickoffRecoverySettleDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan StartupWebViewBudget = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan StartupDeferredConnectMetadataDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StartupDeferredMetadataPersistedPreviewRefreshDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartupDeferredModelProfileSyncDelay = TimeSpan.FromMilliseconds(1250);
    private static readonly TimeSpan StartupDeferredMetadataPhaseTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StartupDeferredMetadataRequestTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StartupDeferredMetadataStaleWatchdogTimeout = TimeSpan.FromSeconds(35);
    private const int StartupDeferredMetadataFailureAutoRetryLimit = 1;
    private const int StartupDeferredMetadataPersistedPreviewRefreshRetryLimit = 8;
    private static readonly TimeSpan StartupDeferredDispatchPrewarmDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan StartupDeferredInteractiveBackgroundPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartupDeferredInteractiveBackgroundTurnWaitTimeout = TimeSpan.FromMilliseconds(900);
    private const int StartupBootstrapCacheModeUnknown = 0;
    private const int StartupBootstrapCacheModeHit = 1;
    private const int StartupBootstrapCacheModeMiss = 2;
    private const int StartupBootstrapCacheModePersistedPreview = 3;
    private const int StartupDiagnosticsWatchdogClearKindNone = 0;
    private const int StartupDiagnosticsWatchdogClearKindActive = 1;
    private const int StartupDiagnosticsWatchdogClearKindQueued = 2;
    private const int StartupDiagnosticsPhaseResultUnknown = -1;
    private const long StartupDiagnosticsDurationUnknownMs = -1;
    private const int StartupFlowStateIdle = 0;
    private const int StartupFlowStateRunning = 1;
    private const int StartupFlowStateComplete = 2;
    private const int StartupWebViewBudgetFastEnsureThresholdMs = 1200;
    private const int StartupWebViewBudgetMediumEnsureThresholdMs = 2000;
    private const int StartupWebViewBudgetSlowEnsureThresholdMs = 3000;
    private const int StartupWebViewBudgetFastMs = 2200;
    private const int StartupWebViewBudgetMediumMs = 2800;
    private const int StartupWebViewBudgetSlowMs = 3400;
    private const int StartupWebViewBudgetMinimumMs = 2000;
    private const int StartupWebViewBudgetAdaptiveHeadroomMs = 1100;
    private const int StartupWebViewBudgetAdaptiveMaxDownshiftPerRunMs = 300;
    private const int StartupWebViewBudgetAdaptiveMinStableCompletions = 2;
    private const int StartupWebViewBudgetAdaptiveCooldownRunsAfterExhaustion = 2;
    private const int StartupWebViewBudgetAdaptiveMaxStableCompletions = 8;
    private const int StartupWebViewBudgetMaxConsecutiveExhaustions = 8;
    private const long FirstTurnLatencySystemNoticeThresholdMs = 1200;
    private const long SlowTurnSystemNoticeThresholdMs = 4500;
    private const string StartupWebViewBudgetCacheFileName = "startup-webview-budget-cache-v1.json";
    private const string StartupWebViewBudgetReasonCooldownConservative = "cooldown_conservative";
    private const string StartupWebViewBudgetReasonExhaustionConservative = "exhaustion_conservative";
    private const string StartupWebViewBudgetReasonInsufficientStability = "insufficient_stability";
    private const string StartupWebViewBudgetReasonMissingLastEnsure = "missing_last_ensure";
    private const string StartupWebViewBudgetReasonConservativeTier = "conservative_tier";
    private const string StartupWebViewBudgetReasonFastTier = "fast_tier";
    private const string StartupWebViewBudgetReasonMediumTier = "medium_tier";
    private const string StartupWebViewBudgetReasonSlowTier = "slow_tier";
    private const string StartupWebViewBudgetReasonSuffixNew = "_new";
    private const string StartupWebViewBudgetReasonSuffixNondecreasing = "_nondecreasing";
    private const string StartupWebViewBudgetReasonSuffixDownshiftCapped = "_downshift_capped";
    private const string StartupWebViewBudgetReasonSuffixDownshiftFull = "_downshift_full";
    private static readonly TimeSpan[] StartupConnectRetryTimeouts = {
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(14)
    };
    private static readonly Regex UserNameIntentRegex = new(@"\b(?:you can call me|call me|my name is|name is|set my name to|change my name to)\s+(?<value>[^,\.\!\?\r\n]{1,64})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PersonaIntentRegex = new(@"\b(?:assistant\s+persona|persona|style|tone|mode)\s*(?:is|to|=|:)\s*(?<value>[^,\.\!\?\r\n]{2,180})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PersonaUseIntentRegex = new(@"\b(?:use|switch to|go with)\s+(?<value>[^,\.\!\?\r\n]{2,180})\s+(?:persona|style|tone|mode)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ThemeIntentRegex = new(
        $@"\b(?:theme)\s*(?:is|to|=|:)?\s*(?<value>{ThemeContract.ThemeValueRegexAlternation})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ThemeUseIntentRegex = new(
        $@"\b(?:use|switch to|set)\s+(?<value>{ThemeContract.ThemeValueRegexAlternation})\s+theme\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MemoryRememberIntentRegex = new(@"\b(?:remember(?:\s+this|\s+that)?|save\s+this|store\s+this)\b[\s,:-]*(?<value>[^\r\n]{6,240})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MemoryFutureIntentRegex = new(@"\bfor\s+next\s+time\b[\s,:-]*(?<value>[^\r\n]{6,220})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    private readonly MarkdownRendererOptions _markdownOptions;
    private readonly Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?> _webViewEnvironmentTask;
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
    private readonly object _dragMoveWatchdogSync = new();
    private int _dragMoveWatchdogSequence;
    private bool _dragMoveWatchdogInFlight;
    private bool _windowIsActive;
    private bool _nativeWheelObserved;
    private long _wheelPointerQueuedEvents;
    private long _wheelGlobalQueuedEvents;
    private long _wheelZeroDeltaIgnoredEvents;
    private long _wheelDroppedNotReadyEvents;
    private long _wheelForwardedBatches;
    private long _wheelForwardedPointerBatches;
    private long _wheelForwardedGlobalBatches;
    private long _wheelForwardedAbsDelta;
    private long _dragMoveWatchdogArmCount;
    private long _dragMoveWatchdogForcedReleaseCount;
    private static readonly bool VerboseServiceLogs = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_VERBOSE_SERVICE_LOGS"));
    private static readonly bool DetachedServiceMode = ResolveDetachedServiceMode();
    private static readonly GlobalWheelHookMode WheelHookMode = ResolveGlobalWheelHookMode(Environment.GetEnvironmentVariable("IXCHAT_WHEEL_HOOK_MODE"));

    private readonly string _pipeName = "intelligencex.chat";
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _ensureConnectedSync = new();
    private Task<bool>? _ensureConnectedInFlightTask;
    private long _lastDispatchConnectFailureUtcTicks;
    private bool _webViewReady;
    private bool _startupInitialized;
    private int _startupFlowState;
    private readonly object _startupWebViewBudgetCacheSync = new();
    private StartupWebViewBudgetCacheEntry _startupWebViewBudgetCache = StartupWebViewBudgetCacheEntry.Default;
    private int _startupWebViewBudgetExceededThisRun;
    private int _startupBenchAutoSendQueued;
    private int _serviceStagingCleanupInFlight;

    private ChatServiceClient? _client;
    private string? _threadId;
    private int _nextRequestId = 1;
    private (string LoginId, string PromptId, string PromptText)? _pendingLoginPrompt;
    private readonly object _queuedAfterLoginSync = new();
    private readonly Queue<QueuedTurn> _queuedTurnsAfterLogin = new();
    private bool _isAuthenticated;
    private bool _interactiveAuthenticationStateKnown;
    private string? _authenticatedAccountId;
    private bool _loginInProgress;
    private bool _debugMode;
    private bool _showAssistantTurnTrace;
    private bool _showAssistantDraftBubbles;
    private bool _isConnected;
    private string _statusText = SessionStatusFormatter.Format(SessionStatus.Disconnected());
    private SessionStatusTone _statusTone = SessionStatusToneResolver.Resolve(SessionStatus.Disconnected());
    private bool _usageLimitSwitchRecommended;
    private bool _queuedPromptUsageLimitBypassAfterSwitchAccount;
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
    private bool _proactiveModeEnabled;
    private string _exportSaveMode = ExportPreferencesContract.DefaultSaveMode;
    private string _exportDefaultFormat = ExportPreferencesContract.DefaultFormat;
    private string _exportVisualThemeMode = ExportPreferencesContract.DefaultVisualThemeMode;
    private int _exportDocxVisualMaxWidthPx = ExportPreferencesContract.DefaultDocxVisualMaxWidthPx;
    private string? _lastExportDirectory;
    private bool _persistentMemoryEnabled = true;
    private string _localProviderTransport = TransportNative;
    private string? _localProviderBaseUrl;
    private string _localProviderModel = DefaultLocalModel;
    private string _localProviderOpenAIAuthMode = "bearer";
    private string _localProviderOpenAIBasicUsername = string.Empty;
    private string _localProviderOpenAIAccountId = string.Empty;
    private int _activeNativeAccountSlot = 1;
    private readonly string[] _nativeAccountSlots;
    private string _localProviderReasoningEffort = string.Empty;
    private string _localProviderReasoningSummary = string.Empty;
    private string _localProviderTextVerbosity = string.Empty;
    private double? _localProviderTemperature;
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
    private string? _serviceActiveProfileName;
    private SessionPolicyDto? _sessionPolicy;
    private ToolPackInfoDto[] _toolCatalogPacks = Array.Empty<ToolPackInfoDto>();
    private PluginInfoDto[] _toolCatalogPlugins = Array.Empty<PluginInfoDto>();
    private SessionRoutingCatalogDiagnosticsDto? _toolCatalogRoutingCatalog;
    private SessionCapabilitySnapshotDto? _toolCatalogCapabilitySnapshot;
    private SessionCapabilityBackgroundSchedulerDto? _backgroundSchedulerStatusSnapshot;
    private SessionCapabilityBackgroundSchedulerDto? _backgroundSchedulerScopedStatusSnapshot;
    private SessionCapabilityBackgroundSchedulerDto? _backgroundSchedulerGlobalStatusSnapshot;
    private readonly Dictionary<string, bool> _toolStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _toolTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolPackIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolPackNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDefinitionDto> _toolCatalogDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolParameterDto[]> _toolParameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _toolWriteCapabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _toolExecutionAwareness = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolExecutionContractIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolExecutionScopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _toolSupportsLocalExecution = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _toolSupportsRemoteExecution = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolRoutingConfidence = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolRoutingReason = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _toolRoutingScore = new(StringComparer.OrdinalIgnoreCase);
    // Guarded by _turnDiagnosticsSync.
    private RoutingPromptExposureSnapshot? _latestRoutingPromptExposure;
    // Guarded by _turnDiagnosticsSync.
    private readonly List<RoutingPromptExposureSnapshot> _routingPromptExposureHistory = new();
    private int _toolStateHiddenWithoutCatalogLastCount = -1;
    private readonly HashSet<string> _startupToolHealthWarningSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startupUnavailablePackSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startupBootstrapSummarySignatures = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _turnStartupInProgress;
    private readonly object _pendingTurnQueueSync = new();
    private readonly Queue<QueuedTurn> _pendingTurns = new();
    private string? _activeTurnRequestId;
    private string? _latestTurnRequestId;
    private string? _activeKickoffRequestId;
    private string? _cancelRequestedTurnRequestId;
    private CancellationTokenSource? _activeTurnRequestCts;
    private bool _activeTurnFromQueuedAfterLogin;
    private string _activeTurnNormalizedPromptText = string.Empty;
    private string _activeTurnPromptConversationId = string.Empty;
    // Serializes active-turn request id/CTS handoff, cancel, and teardown.
    private readonly object _activeTurnLifecycleSync = new();
    private readonly object _turnDiagnosticsSync = new();
    private readonly List<string> _activityTimeline = new();
    // Guarded by _turnDiagnosticsSync. Nested dictionaries/lists are mutable and must not be touched outside that lock.
    private readonly Dictionary<string, Dictionary<int, AssistantTurnVisualState>> _assistantTurnVisualStateByConversationId =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _activeTurnAssistantConversationId;
    private int _activeTurnAssistantMessageIndex = -1;
    private readonly List<string> _activeTurnAssistantPendingTimeline = new();
    private bool _activeTurnAssistantProvisional;
    private AssistantTurnLifecycleStage _activeTurnLifecycleStage = AssistantTurnLifecycleStage.Idle;
    private AssistantBubbleChannelKind _activeTurnAssistantChannel = AssistantBubbleChannelKind.Final;
    private bool _activeTurnUsesProvisionalEvents;
    private bool _activeTurnInterimResultSeen;
    private string? _activeTurnInterimFingerprint;
    private TurnMetricsSnapshot? _lastTurnMetrics;
    private readonly Dictionary<string, AccountUsageSnapshot> _accountUsageByKey = new(StringComparer.OrdinalIgnoreCase);
    private long? _activeTurnQueueWaitMs;
    private long _activeTurnStartedUtcTicks;
    private readonly object _turnWatchdogSync = new();
    private CancellationTokenSource? _turnWatchdogCts;
    private string _latestServiceActivityText = string.Empty;
    private readonly AssistantStreamingState _assistantStreamingState = new();
    private bool _modelKickoffAttempted;
    private bool _modelKickoffInProgress;
    private bool _autoSignInAttempted;
    private int _startupAuthDeferredQueued;
    private int _startupOnboardingDeferredQueued;
    private int _startupConnectMetadataDeferredQueued;
    private int _startupConnectMetadataDeferredRerunRequested;
    private int _startupConnectMetadataPersistedPreviewRefreshPending;
    private long _startupConnectMetadataDeferredQueuedUtcTicks;
    private int _startupConnectMetadataFailureAutoRetryCount;
    private int _startupConnectMetadataPersistedPreviewRefreshRetryCount;
    private long _startupConnectMetadataFailureRecoveryQueuedCount;
    private long _startupConnectMetadataFailureRecoveryLimitReachedCount;
    private long _startupConnectMetadataFailureLastUtcTicks;
    private string _startupConnectMetadataFailureLastKind = string.Empty;
    private int _startupLoginSuccessMetadataSyncQueued;
    private int _startupModelProfileSyncDeferredQueued;
    private int _startupWebViewPostInitDeferredQueued;
    private int _startupDispatchPrewarmDeferredQueued;
    private int _startupFirstTurnLatencyNoticePending = 1;
    private int _startupInteractivePriorityRequested;
    private readonly object _startupMetadataSyncLock = new();
    private int _startupMetadataSyncInProgress;
    private long _startupMetadataSyncStartedUtcTicks;
    private string _startupMetadataSyncPhase = string.Empty;
    private int _startupBootstrapCacheMode = StartupBootstrapCacheModeUnknown;
    private long _startupBootstrapCacheModeUpdatedUtcTicks;
    private long _startupDiagnosticsHelloDurationMs = StartupDiagnosticsDurationUnknownMs;
    private int _startupDiagnosticsHelloAttempts;
    private int _startupDiagnosticsHelloResult = StartupDiagnosticsPhaseResultUnknown;
    private long _startupDiagnosticsHelloUpdatedUtcTicks;
    private long _startupDiagnosticsListToolsDurationMs = StartupDiagnosticsDurationUnknownMs;
    private int _startupDiagnosticsListToolsAttempts;
    private int _startupDiagnosticsListToolsResult = StartupDiagnosticsPhaseResultUnknown;
    private long _startupDiagnosticsListToolsUpdatedUtcTicks;
    private long _startupDiagnosticsAuthRefreshDurationMs = StartupDiagnosticsDurationUnknownMs;
    private int _startupDiagnosticsAuthRefreshAttempts;
    private int _startupDiagnosticsAuthRefreshResult = StartupDiagnosticsPhaseResultUnknown;
    private long _startupDiagnosticsAuthRefreshUpdatedUtcTicks;
    private long _startupDiagnosticsMetadataSyncDurationMs = StartupDiagnosticsDurationUnknownMs;
    private int _startupDiagnosticsMetadataSyncResult = StartupDiagnosticsPhaseResultUnknown;
    private long _startupDiagnosticsMetadataSyncUpdatedUtcTicks;
    private long _startupDiagnosticsAuthGateWaitStartedUtcTicks;
    private long _startupDiagnosticsAuthGateLastWaitMs = StartupDiagnosticsDurationUnknownMs;
    private long _startupDiagnosticsAuthGateWaitCount;
    private int _startupDiagnosticsAuthGateActive;
    private long _startupDiagnosticsAuthGateLastResolvedUtcTicks;
    private long _startupDiagnosticsWatchdogClearActiveCount;
    private long _startupDiagnosticsWatchdogClearQueuedCount;
    private int _startupDiagnosticsWatchdogLastClearKind = StartupDiagnosticsWatchdogClearKindNone;
    private long _startupDiagnosticsWatchdogLastClearUtcTicks;
    private string _themePreset = "default";
    private string? _sessionUserNameOverride;
    private string? _sessionAssistantPersonaOverride;
    private string? _sessionThemeOverride;
    private bool _queueAutoDispatchEnabled = true;
    private string? _activeRequestConversationId;
    private string _activeConversationId = "chat-default";
    private readonly List<ConversationRuntime> _conversations = new();
    private List<(string Role, string Text, DateTime Time, string? Model)> _messages = new();
    private readonly SemaphoreSlim _transcriptRenderGate = new(1, 1);
    private long _transcriptRenderGeneration;
    private long _transcriptLastRenderUtcTicks;
    private string? _lastTranscriptScriptPayload;
    private readonly object _uiPublishSync = new();
    private bool _uiPublishPumpRunning;
    private bool _pendingSessionStatePublish;
    private bool _pendingOptionsStatePublish;
    private TaskCompletionSource<object?>? _pendingSessionStatePublishTcs;
    private TaskCompletionSource<object?>? _pendingOptionsStatePublishTcs;
    private TaskCompletionSource<object?>? _activeSessionStatePublishTcs;
    private TaskCompletionSource<object?>? _activeOptionsStatePublishTcs;
    private string? _lastPublishedSessionStateJson;
    private string? _lastPublishedOptionsStateJson;
    private string? _lastStatusScriptPayload;
    private string? _lastActivityScriptPayload;
    private string? _lastStatusDrivenSessionStamp;
    private string? _lastStatusDrivenOptionsStamp;
    private readonly object _serviceSessionPublishSync = new();
    private bool _serviceSessionPublishScheduled;
    private bool _serviceSessionPublishPending;
    private long _serviceSessionPublishLastUtcTicks;
    private readonly object _postLoginCompletionSync = new();
    private Task? _postLoginCompletionInFlightTask;
    private long _serviceSessionPublishRequestedCount;
    private long _serviceSessionPublishCoalescedCount;
    private long _serviceSessionPublishExecutedCount;
    private long _serviceSessionPublishFailedCount;
    private long _serviceSessionPublishDelayedCount;
    private long _serviceSessionPublishLastDelayMs;
    private long _serviceSessionPublishMaxDelayMs;
    private long _serviceSessionPublishLastRequestedUtcTicks;
    private long _serviceSessionPublishLastPublishedUtcTicks;
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
    private int _localProviderApplyInFlight;
    private readonly object _localProviderApplySync = new();
    private LocalProviderApplyRequest? _pendingLocalProviderApply;
    private string _runtimeApplyStage = "idle";
    private string _runtimeApplyDetail = string.Empty;
    private bool _runtimeApplyActive;
    private DateTime? _runtimeApplyUpdatedUtc;
    private long _runtimeApplyRequestId;
    private long _runtimeApplyRequestCounter;

    private sealed class ConversationRuntime {
        public required string Id { get; init; }
        public string Title { get; set; } = DefaultConversationTitle;
        public string? ThreadId { get; set; }
        public string? RuntimeLabel { get; set; }
        public string? ModelLabel { get; set; }
        public string? ModelOverride { get; set; }
        public List<(string Role, string Text, DateTime Time, string? Model)> Messages { get; } = new();
        public IReadOnlyList<AssistantPendingAction> PendingActions { get; set; } = Array.Empty<AssistantPendingAction>();
        public string? PendingAssistantQuestionHint { get; set; }
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class AssistantTurnVisualState {
        // Mutable state; always access under _turnDiagnosticsSync.
        public bool IsProvisional { get; set; }
        public AssistantBubbleChannelKind Channel { get; set; } = AssistantBubbleChannelKind.Final;
        public List<string> Timeline { get; } = new();
    }

    internal enum AssistantTurnLifecycleStage {
        Idle = 0,
        Draft = 1,
        Tool = 2,
        Finalized = 3,
        Failed = 4
    }

    private sealed record QueuedTurn(string Text, string? ConversationId, DateTime EnqueuedUtc, bool SkipUserBubbleOnDispatch = false);
    private sealed record LocalProviderApplyRequest(
        string? Transport,
        string? BaseUrl,
        string? Model,
        string? OpenAIAuthMode,
        string? OpenAIBasicUsername,
        string? OpenAIBasicPassword,
        string? OpenAIAccountId,
        int? ActiveNativeAccountSlot,
        string? ActiveSlotAccountId,
        string? ReasoningEffort,
        string? ReasoningSummary,
        string? TextVerbosity,
        string? Temperature,
        string? ApiKey,
        bool ClearBasicAuth,
        bool ClearApiKey,
        bool ForceModelRefresh,
        long RequestId);

    private sealed record TurnMetricsSnapshot(
        string RequestId,
        DateTime CompletedUtc,
        long DurationMs,
        long? TtftMs,
        long? QueueWaitMs,
        long? AuthProbeMs,
        long? ConnectMs,
        long? EnsureThreadMs,
        long? WeightedSubsetSelectionMs,
        long? ResolveModelMs,
        long? DispatchToFirstStatusMs,
        long? DispatchToModelSelectedMs,
        long? DispatchToFirstToolRunningMs,
        long? DispatchToFirstDeltaMs,
        long? DispatchToLastDeltaMs,
        long? StreamDurationMs,
        int ToolCallsCount,
        int ToolRounds,
        int ProjectionFallbackCount,
        string Outcome,
        string? ErrorCode,
        long? PromptTokens,
        long? CompletionTokens,
        long? TotalTokens,
        long? CachedPromptTokens,
        long? ReasoningTokens,
        IReadOnlyList<TurnCounterMetricDto> AutonomyCounters,
        string? Model,
        string? RequestedModel,
        string? Transport,
        string? EndpointHost);

    private sealed record RoutingPromptExposureSnapshot(
        string RequestId,
        string ThreadId,
        string Strategy,
        int SelectedToolCount,
        int TotalToolCount,
        bool Reordered,
        string[] TopToolNames);

    private readonly record struct RoutingMetaPayloadSnapshot(
        string Strategy,
        int SelectedToolCount,
        int TotalToolCount,
        bool PromptExposureReordered,
        string[] PromptExposureTopToolNames);

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
        public string? OpenAIAuthMode { get; init; }
        public string? OpenAIApiKey { get; init; }
        public string? OpenAIBasicUsername { get; init; }
        public string? OpenAIBasicPassword { get; init; }
        public string? OpenAIAccountId { get; init; }
        public bool ClearOpenAIApiKey { get; init; }
        public bool ClearOpenAIBasicAuth { get; init; }
        public bool? OpenAIStreaming { get; init; }
        public bool? OpenAIAllowInsecureHttp { get; init; }
        public string? ReasoningEffort { get; init; }
        public string? ReasoningSummary { get; init; }
        public string? TextVerbosity { get; init; }
        public double? Temperature { get; init; }
        public IReadOnlyList<ServiceLaunchArguments.PackToggle>? PackToggles { get; init; }
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
        _markdownOptions = MarkdownRendererPresets.CreateIntelligenceXTranscriptDesktopShell();
        StartupLogRendererDiagnostics();
        Title = "IntelligenceX Chat";
        _debugMode = VerboseServiceLogs;
        _nativeAccountSlots = new string[ResolveNativeAccountSlotCount()];

        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _webView = new WebView2();
        Content = _webView;
        _webViewEnvironmentTask = CreateWebViewEnvironmentAsync();
        _startupWebViewBudgetCache = LoadStartupWebViewBudgetCache();
        ConfigureWindowPlacement();

        Activated += (_, args) => {
            StartupLog.Write("MainWindow.Activated");
            _windowIsActive = args.WindowActivationState != WindowActivationState.Deactivated;
            RefreshGlobalWheelHookPolicy();
            EnsureRestoredIfMinimized();

            if (Interlocked.CompareExchange(ref _startupFlowState, StartupFlowStateRunning, StartupFlowStateIdle) != StartupFlowStateIdle) {
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
            LogInputReliabilityTelemetry("shutdown");
            UninstallGlobalWheelHook();
            UninstallWindowMessageHook();
            try {
                CleanupStaleVisualPopoutFiles(GetVisualPopoutDirectoryPath());
            } catch {
                // Ignore visual popout cleanup failures during shutdown.
            }
            _stateStore.Dispose();
        };
    }

}
