using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable {
    private const int DefaultRefreshIntervalSeconds = 120;
    private const int UsageStartupRefreshStalenessSeconds = 1800;
    private const int UsageRootSafetySweepSeconds = 21600;
    private const int UsageChangeDebounceSeconds = 15;
    private const int RefreshHistoryDepth = 4;
    private const int FreshLimitSnapshotWindowSeconds = 75;
    private const int GitHubWatchAutoSyncMinimumIntervalSeconds = 1800;
    private const int GitHubWatchSnapshotFreshnessSeconds = 21600;
    private const int GitHubWatchForkFreshnessSeconds = 86400;
    private const int GitHubWatchStargazerFreshnessSeconds = 86400;
    private const double LimitWarningThresholdPercent = 90d;
    private const double LimitExhaustedThresholdPercent = 100d;

    private readonly UsageTelemetrySnapshotService _usageService;
    private readonly ProviderLimitSnapshotService _limitService;
    private readonly GitHubService _gitHubService;
    private readonly GitCodeChurnSummaryService _gitCodeChurnSummaryService;
    private readonly GitHubObservabilitySummaryService _gitHubObservabilitySummaryService;
    private readonly GitHubRepositoryWatchAutoSyncService _gitHubWatchAutoSyncService;
    private readonly TrayPreferencesStore _preferencesStore;
    private readonly TrayUsageSnapshotStore _usageSnapshotStore;
    private readonly TrayPreferences _preferences;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _loadingStatusTimer;
    private readonly DispatcherTimer _usageDirtyRefreshTimer;
    private readonly UsageChangeWatcher _usageChangeWatcher;
    private readonly HashSet<string> _activeLimitNotificationKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _favoriteProviderIds;
    private readonly Dictionary<string, ProviderLimitSnapshot> _latestLimitSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<SourceRootRecord>> _latestSourceRootsByProvider = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<UsageEventRecord>> _displayedProviderEvents = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProviderRefreshSnapshot> _previousProviderSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<ProviderRefreshSnapshot>> _providerRefreshHistory = new(StringComparer.OrdinalIgnoreCase);
    private UsageTelemetrySnapshotHealth? _latestUsageHealth;
    private GitCodeChurnSummaryData _latestGitCodeChurnSummary = GitCodeChurnSummaryData.Empty;
    private GitHubObservabilitySummaryData _latestGitHubObservabilitySummary = GitHubObservabilitySummaryData.Empty;
    private GitHubLocalActivityCorrelationSummaryData _latestGitHubLocalActivityCorrelationSummary = GitHubLocalActivityCorrelationSummaryData.Empty;
    private GitHubRepositoryClusterSummaryData _latestGitHubRepositoryClusterSummary = GitHubRepositoryClusterSummaryData.Empty;
    private ProviderViewModel? _selectedProvider;
    private bool _isLoading;
    private string _statusText = "Initializing...";
    private string _loadingDetailText = "Preparing tray refresh...";
    private double _loadingProgressValue;
    private double _loadingProgressMaximum = 1d;
    private bool _loadingProgressIsIndeterminate = true;
    private DateTimeOffset _loadingProgressUpdatedAtUtc;
    private DateTimeOffset _lastRefreshed;
    private DateTimeOffset _lastLimitRefreshUtc;
    private int _gitHubRefreshVersion;
    private CancellationTokenSource? _gitHubRefreshCts;
    private int _limitRefreshVersion;
    private CancellationTokenSource? _limitRefreshCts;
    private string _themeMode = TrayThemeService.SystemMode;
    private string _accentPreset = TrayThemeService.DefaultAccentPreset;
    private int _autoRefreshIntervalSeconds;
    private bool _gitHubWatchAutoSyncEnabled;
    private bool _notificationsEnabled;
    private bool _closeHidesToTray;
    private bool _startWithWindows;
    private bool _hasCompletedInitialRefresh;
    private DateTimeOffset _lastGitHubWatchAutoSyncAttemptUtc;
    private DateTimeOffset _lastUsageSnapshotScannedAtUtc;
    private DateTimeOffset _lastUsageRootDiscoveryUtc;
    private DateTimeOffset _usageDirtyAtUtc;
    private string? _latestUsageChangePath;

    internal MainViewModel(
        UsageTelemetrySnapshotService usageService,
        ProviderLimitSnapshotService limitService,
        GitHubService gitHubService,
        GitCodeChurnSummaryService gitCodeChurnSummaryService,
        GitHubObservabilitySummaryService gitHubObservabilitySummaryService,
        GitHubRepositoryWatchAutoSyncService gitHubWatchAutoSyncService,
        TrayPreferencesStore preferencesStore,
        TrayUsageSnapshotStore usageSnapshotStore) {
        _usageService = usageService;
        _limitService = limitService;
        _gitHubService = gitHubService;
        _gitCodeChurnSummaryService = gitCodeChurnSummaryService;
        _gitHubObservabilitySummaryService = gitHubObservabilitySummaryService;
        _gitHubWatchAutoSyncService = gitHubWatchAutoSyncService;
        _preferencesStore = preferencesStore;
        _usageSnapshotStore = usageSnapshotStore;
        _preferences = _preferencesStore.Load();
        _themeMode = TrayThemeService.NormalizeThemeMode(_preferences.ThemeMode);
        _preferences.ThemeMode = _themeMode;
        _accentPreset = TrayThemeService.NormalizeAccentPreset(_preferences.AccentPreset);
        _preferences.AccentPreset = _accentPreset;
        _autoRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(_preferences.AutoRefreshIntervalSeconds);
        _gitHubWatchAutoSyncEnabled = _preferences.GitHubWatchAutoSyncEnabled;
        _notificationsEnabled = _preferences.NotificationsEnabled;
        _closeHidesToTray = _preferences.CloseHidesToTray;
        _startWithWindows = _preferences.StartWithWindows;
        _favoriteProviderIds = new HashSet<string>(
            (_preferences.FavoriteProviderIds ?? [])
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId)),
            StringComparer.OrdinalIgnoreCase);

        GitHub = new GitHubViewModel {
            UsernameInput = _preferences.GitHubUsername ?? string.Empty
        };
        GitHub.PropertyChanged += OnGitHubPropertyChanged;

        RefreshCommand = new RelayCommand(() => RefreshAsync());
        RefreshGitHubCommand = new RelayCommand(RefreshGitHubCurrentAsync);
        OpenOpenAiCacheCommand = new RelayCommand(OpenOpenAiCacheAsync);
        CycleThemeModeCommand = new RelayCommand(CycleThemeModeAsync);
        ToggleSelectedProviderFavoriteCommand = new RelayCommand(ToggleSelectedProviderFavoriteAsync, () => CanToggleFavoriteSelectedProvider);

        _refreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _autoRefreshIntervalSeconds <= 0 ? DefaultRefreshIntervalSeconds : _autoRefreshIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAutoAsync();

        _usageChangeWatcher = new UsageChangeWatcher();
        _usageChangeWatcher.Changed += OnUsageChangeDetected;
        _usageDirtyRefreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(UsageChangeDebounceSeconds)
        };
        _usageDirtyRefreshTimer.Tick += async (_, _) => {
            _usageDirtyRefreshTimer.Stop();
            await RefreshUsageIfDirtyAsync();
        };

        _loadingStatusTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(1)
        };
        _loadingStatusTimer.Tick += (_, _) => {
            if (IsLoading) {
                OnPropertyChanged(nameof(LoadingDetailDisplayText));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        };
    }

    public event EventHandler<TrayNotificationRequestedEventArgs>? NotificationRequested;
    public event EventHandler? ThemeModeChanged;
    public event EventHandler? AccentPresetChanged;

    public ObservableCollection<ProviderViewModel> Providers { get; } = [];
    public GitHubViewModel GitHub { get; }

    public ProviderViewModel? SelectedProvider {
        get => _selectedProvider;
        set {
            if (SetProperty(ref _selectedProvider, value)) {
                value?.ClearRefreshBadge();
                RefreshProviderSelectionState();
                OnPropertyChanged(nameof(CanToggleFavoriteSelectedProvider));
                OnPropertyChanged(nameof(FavoriteSelectedProviderLabel));
                ToggleSelectedProviderFavoriteCommand.RaiseCanExecuteChanged();
                SaveSelectedProviderPreference(value?.ProviderId);
            }
        }
    }

    private bool HasGitHubProvider => Providers.Any(provider => provider.ProviderId == "__github__");
    private bool HasUsageProviders => Providers.Any(provider => provider.ProviderId != "__github__");
    public bool IsGitHubTabSelected => SelectedProvider?.ProviderId == "__github__";

    public bool ShowUsageContent => SelectedProvider is { ProviderId: not "__github__" };
    public bool ShowGitHubContent => IsGitHubTabSelected || (!HasUsageProviders && HasGitHubProvider);
    public bool ShowCombinedGitHubPulse => ShowUsageContent
                                           && SelectedProvider is { ProviderId: "__all__" }
                                           && GitHub.HasObservabilitySummary;
    public bool ShowLoadingOverlay => IsLoading && ShowUsageContent && !HasUsageProviders;

    public string HeaderTitle {
        get {
            if (ShowGitHubContent) {
                return "GitHub";
            }

            if (SelectedProvider == null || SelectedProvider.ProviderId == "__all__") {
                return "Usage Monitor";
            }

            return SelectedProvider.DisplayName;
        }
    }

    public bool IsLoading {
        get => _isLoading;
        set {
            if (SetProperty(ref _isLoading, value)) {
                if (value) {
                    _loadingProgressUpdatedAtUtc = DateTimeOffset.UtcNow;
                    if (!_loadingStatusTimer.IsEnabled) {
                        _loadingStatusTimer.Start();
                    }
                } else {
                    _loadingProgressUpdatedAtUtc = default;
                    if (_loadingStatusTimer.IsEnabled) {
                        _loadingStatusTimer.Stop();
                    }
                    OnPropertyChanged(nameof(LoadingDetailDisplayText));
                }
                OnPropertyChanged(nameof(ShowLoadingOverlay));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public string StatusText {
        get => _statusText;
        set {
            if (SetProperty(ref _statusText, value)) {
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public string LoadingDetailText {
        get => _loadingDetailText;
        set {
            if (SetProperty(ref _loadingDetailText, value)) {
                OnPropertyChanged(nameof(LoadingDetailDisplayText));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public string LoadingDetailDisplayText => BuildLoadingStatusText(LoadingDetailText);

    public double LoadingProgressValue {
        get => _loadingProgressValue;
        set => SetProperty(ref _loadingProgressValue, value);
    }

    public double LoadingProgressMaximum {
        get => _loadingProgressMaximum;
        set => SetProperty(ref _loadingProgressMaximum, value <= 0d ? 1d : value);
    }

    public bool LoadingProgressIsIndeterminate {
        get => _loadingProgressIsIndeterminate;
        set => SetProperty(ref _loadingProgressIsIndeterminate, value);
    }

    public string FooterStatusText => IsLoading && !string.IsNullOrWhiteSpace(LoadingDetailText)
        ? BuildLoadingStatusText(LoadingDetailText)
        : StatusText;

    public DateTimeOffset LastRefreshed {
        get => _lastRefreshed;
        set {
            if (SetProperty(ref _lastRefreshed, value)) {
                OnPropertyChanged(nameof(LastRefreshedFormatted));
            }
        }
    }

    public string LastRefreshedFormatted => LastRefreshed == default
        ? "Never"
        : LastRefreshed.ToLocalTime().ToString("HH:mm:ss");

    public bool HasData => Providers.Count > 0 && SelectedProvider != null;
    public bool CanToggleFavoriteSelectedProvider => SelectedProvider is { ProviderId: not "__all__" };
    public string FavoriteSelectedProviderLabel => SelectedProvider?.IsFavorite == true ? "Pinned" : "Pin";
    public ICommand HeaderRefreshCommand => ShowGitHubContent ? RefreshGitHubCommand : RefreshCommand;
    public string HeaderRefreshLabel => ShowGitHubContent ? "Load" : "Refresh";

    public int AutoRefreshIntervalSeconds {
        get => _autoRefreshIntervalSeconds;
        private set {
            if (SetProperty(ref _autoRefreshIntervalSeconds, value)) {
                OnPropertyChanged(nameof(RefreshModeLabel));
            }
        }
    }

    public bool GitHubWatchAutoSyncEnabled {
        get => _gitHubWatchAutoSyncEnabled;
        private set => SetProperty(ref _gitHubWatchAutoSyncEnabled, value);
    }

    public string ThemeMode {
        get => _themeMode;
        private set {
            if (SetProperty(ref _themeMode, value)) {
                OnPropertyChanged(nameof(ThemeButtonLabel));
                OnPropertyChanged(nameof(ThemeToolTip));
            }
        }
    }

    public string AccentPreset {
        get => _accentPreset;
        private set {
            if (SetProperty(ref _accentPreset, value)) {
                OnPropertyChanged(nameof(AccentSummaryLabel));
                OnPropertyChanged(nameof(AccentToolTip));
            }
        }
    }

    public bool NotificationsEnabled {
        get => _notificationsEnabled;
        private set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool CloseHidesToTray {
        get => _closeHidesToTray;
        private set => SetProperty(ref _closeHidesToTray, value);
    }

    public bool StartWithWindows {
        get => _startWithWindows;
        private set => SetProperty(ref _startWithWindows, value);
    }

    public string RefreshModeLabel => AutoRefreshIntervalSeconds <= 0
        ? "Manual refresh"
        : "Auto " + FormatRefreshInterval(AutoRefreshIntervalSeconds);
    public string ThemeButtonLabel => TrayThemeService.GetDisplayName(ThemeMode);
    public string ThemeToolTip => "Theme: " + TrayThemeService.GetDisplayName(ThemeMode) + ". Click to cycle Auto, Dark, Light.";
    public string AccentSummaryLabel => TrayThemeService.GetAccentDisplayName(AccentPreset);
    public string AccentToolTip => "Accent: " + TrayThemeService.GetAccentDisplayName(AccentPreset) + ". Use the tray context menu to switch presets.";

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshGitHubCommand { get; }
    public RelayCommand OpenOpenAiCacheCommand { get; }
    public RelayCommand CycleThemeModeCommand { get; }
    public RelayCommand ToggleSelectedProviderFavoriteCommand { get; }

    public async Task InitializeAsync() {
        var loadedCachedUsageSnapshot = ApplyCachedUsageSnapshot();
        ConfigureRefreshTimer();
        _ = RefreshGitHubAsync(GitHub.UsernameInput);
        if (loadedCachedUsageSnapshot) {
            _ = RefreshLightweightAutoAsync();
            if (ShouldRunStartupUsageRefresh()) {
                _ = RefreshAsync();
            }
            return;
        }

        await RefreshAsync(startupWarmup: true);
        _ = RefreshAsync();
    }

    private async Task RefreshAutoAsync() {
        if (IsLoading) {
            return;
        }

        if (ShouldRunFullAutomaticUsageRefresh()) {
            await RefreshAsync();
            return;
        }

        await RefreshLightweightAutoAsync();
    }

    public async Task RefreshAsync(bool startupWarmup = false) {
        if (IsLoading) {
            return;
        }

        var refreshStartedAtUtc = DateTimeOffset.UtcNow;
        var hasVisibleUsageData = HasUsageProviders;
        var showBusyOverlay = !hasVisibleUsageData;
        IsLoading = true;
        _loadingProgressUpdatedAtUtc = DateTimeOffset.UtcNow;
        LoadingProgressIsIndeterminate = true;
        LoadingProgressValue = 0d;
        LoadingProgressMaximum = 1d;
        if (showBusyOverlay) {
            StatusText = startupWarmup ? "Loading recent usage snapshot..." : "Starting usage refresh...";
            LoadingDetailText = startupWarmup
                ? "Preparing a quick startup snapshot from recent artifacts."
                : "Preparing local usage scan and cache lookup.";
        } else {
            StatusText = "Refreshing usage in background...";
        }

        try {
            var codeChurnTask = Task.Run(LoadGitCodeChurnSummarySafe);
            var discoveredProviderIds = new List<string>();
            var progressiveProviderEvents = new Dictionary<string, List<UsageEventRecord>>(StringComparer.OrdinalIgnoreCase);
            var dispatcher = Application.Current.Dispatcher;
            var progressGate = new object();
            UsageTelemetryScanProgress? pendingScanProgress = null;
            var scanProgressDispatchQueued = 0;

            void ApplyScanProgressUpdate(UsageTelemetryScanProgress progress) {
                if (!IsLoading) {
                    return;
                }

                _loadingProgressUpdatedAtUtc = DateTimeOffset.UtcNow;
                OnPropertyChanged(nameof(LoadingDetailDisplayText));
                OnPropertyChanged(nameof(FooterStatusText));
                StatusText = progress.StatusText;
                if (!string.IsNullOrWhiteSpace(progress.DetailText)) {
                    LoadingDetailText = progress.DetailText!;
                } else if (showBusyOverlay) {
                    LoadingDetailText = "Scanning local usage data.";
                }

                if (progress.ProviderArtifactProgress is { } providerArtifactProgress && providerArtifactProgress.ArtifactCount > 0) {
                    LoadingProgressIsIndeterminate = false;
                    LoadingProgressMaximum = providerArtifactProgress.ArtifactCount;
                    LoadingProgressValue = Math.Min(providerArtifactProgress.ArtifactOrdinal, providerArtifactProgress.ArtifactCount);
                } else {
                    LoadingProgressIsIndeterminate = true;
                }

                var usageShapeChanged = false;
                if (progress.DiscoveredProviderIds is { Count: > 0 }) {
                    foreach (var providerId in progress.DiscoveredProviderIds) {
                        if (string.IsNullOrWhiteSpace(providerId)) {
                            continue;
                        }

                        if (discoveredProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase)) {
                            continue;
                        }

                        discoveredProviderIds.Add(providerId);
                        usageShapeChanged = true;
                    }
                }

                if (progress.CompletedProvider is { } completedProvider) {
                    if (!discoveredProviderIds.Contains(completedProvider.ProviderId, StringComparer.OrdinalIgnoreCase)) {
                        discoveredProviderIds.Add(completedProvider.ProviderId);
                    }

                    progressiveProviderEvents[completedProvider.ProviderId] = completedProvider.Events.ToList();
                    usageShapeChanged = true;
                }

                if (usageShapeChanged) {
                    ApplyProgressiveUsageProviders(discoveredProviderIds, progressiveProviderEvents);
                }
            }

            void ScheduleScanProgressFlush() {
                if (Interlocked.CompareExchange(ref scanProgressDispatchQueued, 1, 0) != 0) {
                    return;
                }

                _ = dispatcher.InvokeAsync(() => {
                    try {
                        UsageTelemetryScanProgress? progressToApply;
                        lock (progressGate) {
                            progressToApply = pendingScanProgress;
                            pendingScanProgress = null;
                        }

                        if (progressToApply is not null) {
                            ApplyScanProgressUpdate(progressToApply);
                        }
                    } finally {
                        Interlocked.Exchange(ref scanProgressDispatchQueued, 0);
                        lock (progressGate) {
                            if (pendingScanProgress is not null) {
                                ScheduleScanProgressFlush();
                            }
                        }
                    }
                }, DispatcherPriority.Background);
            }

            using var scanProgressTimer = new Timer(_ => ScheduleScanProgressFlush(), null, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150));
            var scanProgress = new Progress<UsageTelemetryScanProgress>(progress => {
                lock (progressGate) {
                    pendingScanProgress = progress;
                }

                if (progress.CompletedProvider is not null || progress.ProviderArtifactProgress is null) {
                    ScheduleScanProgressFlush();
                }
            });
            var refreshData = await Task.Run(async () => {
                var snapshot = await _usageService.ScanAsync(progress: scanProgress, startupWarmup: startupWarmup);
                var events = snapshot.Events;
                var rawEvents = snapshot.RawEvents.Count > 0 ? snapshot.RawEvents : snapshot.Events;

                var byProvider = events
                    .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .OrderBy(g => ProviderMetadata.Resolve(g.Key).SortOrder)
                    .ToList();

                var rawByProvider = rawEvents
                    .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.ToList(),
                        StringComparer.OrdinalIgnoreCase);

                var info = $"{events.Count} rollups, {byProvider.Count} providers";
                if (snapshot.ScanDurationMs > 0) {
                    info += $" ({snapshot.ScanDurationMs / 1000.0:F1}s)";
                }

                if (snapshot.Errors.Count > 0) {
                    info += $" [{snapshot.Errors.Count} errors]";
                }

                return new RefreshComputationResult(
                    events.ToList(),
                    rawEvents.ToList(),
                    byProvider.Select(group => new ProviderRefreshData(
                        group.Key,
                        group.ToList(),
                        rawByProvider.TryGetValue(group.Key, out var providerRawEvents) ? providerRawEvents : [])).ToList(),
                    snapshot.ScannedAtUtc,
                    info,
                    snapshot.DiscoveredProviderIds,
                    snapshot.SourceRoots,
                    snapshot.Health);
            });
            ScheduleScanProgressFlush();
            var mergedProviderData = BuildMergedProviderData(
                refreshData.ByProvider,
                refreshData.DiscoveredProviderIds.Count > 0 ? refreshData.DiscoveredProviderIds : discoveredProviderIds);
            var providerIds = mergedProviderData
                .Select(static group => group.ProviderId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var providerDelta = BuildProviderComparisonDelta(mergedProviderData, _previousProviderSnapshots);
            var currentProviderSnapshots = mergedProviderData.ToDictionary(
                static group => group.ProviderId,
                static group => ProviderRefreshSnapshot.FromEvents(group.Events),
                StringComparer.OrdinalIgnoreCase);
            UpdateProviderRefreshHistory(currentProviderSnapshots);
            var providerHistory = BuildProviderComparisonHistory(_providerRefreshHistory);
            UpdateLatestSourceRoots(refreshData.SourceRoots);
            var newProviders = BuildUsageProviders(
                mergedProviderData,
                refreshData.AllEvents,
                refreshData.ScannedAtUtc,
                refreshData.Health,
                providerDelta,
                providerHistory);
            var codeChurnSummary = await codeChurnTask;
            _latestGitCodeChurnSummary = codeChurnSummary;
            ApplyCodeChurnSummary(newProviders, codeChurnSummary);
            ReplaceProviders(newProviders);
            UpdateDisplayedProviderEvents(mergedProviderData);
            RefreshGitHubLocalActivityCorrelationSummary();
            _latestUsageHealth = refreshData.Health;
            LastRefreshed = refreshData.ScannedAtUtc.ToLocalTime();
            _lastUsageSnapshotScannedAtUtc = refreshData.ScannedAtUtc;
            _lastUsageRootDiscoveryUtc = DateTimeOffset.UtcNow;
            if (_usageDirtyAtUtc <= refreshStartedAtUtc) {
                _usageDirtyAtUtc = default;
                _latestUsageChangePath = null;
            }
            _usageChangeWatcher.SetRoots(refreshData.SourceRoots);
            StatusText = startupWarmup
                ? refreshData.ScanInfo + " • Recent startup snapshot ready."
                : BuildUsageRefreshStatus(refreshData.ScanInfo, providerIds);
            _usageSnapshotStore.Save(
                refreshData.ScannedAtUtc,
                refreshData.AllEvents,
                refreshData.DiscoveredProviderIds,
                refreshData.SourceRoots,
                refreshData.Health,
                refreshData.RawEvents);

            _previousProviderSnapshots = new Dictionary<string, ProviderRefreshSnapshot>(currentProviderSnapshots, StringComparer.OrdinalIgnoreCase);
            if (!startupWarmup) {
                EvaluateLimitNotifications(_latestLimitSnapshots);
                _ = RefreshProviderLimitsAsync(providerIds, refreshData.ScanInfo);
                var ghLogin = GitHub.UsernameInput;
                _ = RefreshGitHubAsync(ghLogin);
                _hasCompletedInitialRefresh = true;
            }
        } catch (Exception ex) {
            StatusText = $"Error: {ex.Message}";
            LoadingDetailText = ex.Message;
        } finally {
            IsLoading = false;
            LoadingProgressIsIndeterminate = true;
            LoadingProgressValue = 0d;
            LoadingProgressMaximum = 1d;
            _loadingProgressUpdatedAtUtc = default;
            if (showBusyOverlay) {
                LoadingDetailText = string.Empty;
            }
        }
    }

    private bool ShouldRunFullAutomaticUsageRefresh() {
        if (!HasUsageProviders || _lastUsageSnapshotScannedAtUtc == default) {
            return true;
        }

        if (!_usageChangeWatcher.HasActiveWatchers) {
            return true;
        }

        if (HasPendingUsageChanges()) {
            return true;
        }

        if (_lastUsageRootDiscoveryUtc == default) {
            return true;
        }

        var age = DateTimeOffset.UtcNow - _lastUsageRootDiscoveryUtc;
        return age.TotalSeconds >= UsageRootSafetySweepSeconds;
    }

    private async Task RefreshLightweightAutoAsync() {
        var providerIds = Providers
            .Where(static provider => provider.ProviderId != "__all__" && provider.ProviderId != "__github__")
            .Select(static provider => provider.ProviderId)
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var backgroundTasks = new List<Task> {
            RefreshGitHubAsync(GitHub.UsernameInput)
        };

        if (providerIds.Length > 0) {
            backgroundTasks.Add(RefreshProviderLimitsAsync(providerIds, "Usage snapshot current"));
        }

        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
    }

    private bool ShouldRunStartupUsageRefresh() {
        if (!HasUsageProviders || _lastUsageSnapshotScannedAtUtc == default) {
            return true;
        }

        if (!_usageChangeWatcher.HasActiveWatchers) {
            return true;
        }

        var age = DateTimeOffset.UtcNow - _lastUsageSnapshotScannedAtUtc;
        return age.TotalSeconds >= UsageStartupRefreshStalenessSeconds;
    }

    private bool HasPendingUsageChanges() {
        return _usageDirtyAtUtc != default && _usageDirtyAtUtc > _lastUsageSnapshotScannedAtUtc;
    }

    private async Task RefreshUsageIfDirtyAsync() {
        if (AutoRefreshIntervalSeconds <= 0 || IsLoading || !HasPendingUsageChanges()) {
            return;
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    private string BuildLoadingStatusText(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        if (!IsLoading || _loadingProgressUpdatedAtUtc == default) {
            return value!;
        }

        var elapsed = DateTimeOffset.UtcNow - _loadingProgressUpdatedAtUtc;
        if (elapsed < TimeSpan.FromSeconds(3)) {
            return value!;
        }

        return value + " • " + Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + "s";
    }

    public Task RefreshGitHubCurrentAsync() {
        return RefreshGitHubAsync(GitHub.UsernameInput);
    }

    public void ToggleFavoriteProvider(string? providerId) {
        if (string.IsNullOrWhiteSpace(providerId) || string.Equals(providerId, "__all__", StringComparison.Ordinal)) {
            return;
        }

        if (!_favoriteProviderIds.Add(providerId)) {
            _favoriteProviderIds.Remove(providerId);
        }

        _preferences.FavoriteProviderIds = _favoriteProviderIds
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var provider in Providers) {
            provider.IsFavorite = IsFavoriteProvider(provider.ProviderId);
        }

        if (Providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal)) is { } allProvider) {
            allProvider.SetProviderComparisonFavorites(_favoriteProviderIds);
        }

        ReorderProviders();
        OnPropertyChanged(nameof(FavoriteSelectedProviderLabel));
        ToggleSelectedProviderFavoriteCommand.RaiseCanExecuteChanged();
        SavePreferences();
        StatusText = IsFavoriteProvider(providerId)
            ? "Pinned " + ResolveProviderDisplayName(providerId) + "."
            : "Unpinned " + ResolveProviderDisplayName(providerId) + ".";
    }

    public void FocusProvider(string? providerId) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            return;
        }

        var provider = Providers.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.Ordinal));
        if (provider is not null) {
            SelectedProvider = provider;
        }
    }

    public void SetAutoRefreshIntervalSeconds(int seconds) {
        seconds = NormalizeRefreshIntervalSeconds(seconds);
        if (AutoRefreshIntervalSeconds == seconds) {
            return;
        }

        AutoRefreshIntervalSeconds = seconds;
        _preferences.AutoRefreshIntervalSeconds = seconds;
        SavePreferences();
        ConfigureRefreshTimer();
        StatusText = AutoRefreshIntervalSeconds <= 0
            ? "Auto refresh paused."
            : "Auto refresh set to " + FormatRefreshInterval(AutoRefreshIntervalSeconds) + ".";
    }

    public void SetNotificationsEnabled(bool enabled) {
        if (NotificationsEnabled == enabled) {
            return;
        }

        NotificationsEnabled = enabled;
        _preferences.NotificationsEnabled = enabled;
        SavePreferences();
        if (!enabled) {
            _activeLimitNotificationKeys.Clear();
        }

        StatusText = enabled ? "Limit notifications enabled." : "Limit notifications paused.";
    }

    public void SetGitHubWatchAutoSyncEnabled(bool enabled) {
        if (GitHubWatchAutoSyncEnabled == enabled) {
            return;
        }

        GitHubWatchAutoSyncEnabled = enabled;
        _preferences.GitHubWatchAutoSyncEnabled = enabled;
        SavePreferences();
        if (enabled) {
            _lastGitHubWatchAutoSyncAttemptUtc = default;
        }

        StatusText = enabled
            ? "Watched GitHub repo auto sync enabled."
            : "Watched GitHub repo auto sync paused.";
    }

    public void SetCloseHidesToTray(bool enabled) {
        if (CloseHidesToTray == enabled) {
            return;
        }

        CloseHidesToTray = enabled;
        _preferences.CloseHidesToTray = enabled;
        SavePreferences();
        StatusText = enabled
            ? "Close button now hides the tray popup instead of exiting."
            : "Close button now exits the tray app.";
    }

    public void SetStartWithWindows(bool enabled) {
        if (StartWithWindows == enabled) {
            return;
        }

        StartWithWindows = enabled;
        _preferences.StartWithWindows = enabled;
        SavePreferences();
        StatusText = enabled
            ? "Tray app will start with Windows."
            : "Tray app will no longer start with Windows.";
    }

    public void SyncStartWithWindowsState(bool enabled) {
        if (StartWithWindows == enabled) {
            return;
        }

        StartWithWindows = enabled;
        _preferences.StartWithWindows = enabled;
        SavePreferences();
    }

    public void SetThemeMode(string mode) {
        var normalizedMode = TrayThemeService.NormalizeThemeMode(mode);
        if (string.Equals(ThemeMode, normalizedMode, StringComparison.Ordinal)) {
            return;
        }

        ThemeMode = normalizedMode;
        _preferences.ThemeMode = normalizedMode;
        SavePreferences();
        ThemeModeChanged?.Invoke(this, EventArgs.Empty);
        StatusText = "Theme set to " + TrayThemeService.GetDisplayName(normalizedMode) + ".";
    }

    public void SetAccentPreset(string accentPreset) {
        var normalizedPreset = TrayThemeService.NormalizeAccentPreset(accentPreset);
        if (string.Equals(AccentPreset, normalizedPreset, StringComparison.Ordinal)) {
            return;
        }

        AccentPreset = normalizedPreset;
        _preferences.AccentPreset = normalizedPreset;
        SavePreferences();
        AccentPresetChanged?.Invoke(this, EventArgs.Empty);
        StatusText = "Accent set to " + TrayThemeService.GetAccentDisplayName(normalizedPreset) + ".";
    }

    public Task OpenOpenAiCacheAsync() {
        try {
            var cachePath = ChatGptUsageCache.ResolveCachePath();
            var directory = Path.GetDirectoryName(cachePath);

            if (File.Exists(cachePath)) {
                Process.Start(new ProcessStartInfo {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + cachePath + "\"",
                    UseShellExecute = true
                });
                return Task.CompletedTask;
            }

            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo {
                    FileName = directory,
                    UseShellExecute = true
                });
                return Task.CompletedTask;
            }

            MessageBox.Show(
                "The OpenAI usage cache path could not be resolved.",
                "Usage Cache",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        } catch (Exception ex) {
            MessageBox.Show(
                "Unable to open the OpenAI usage cache.\n\n" + ex.Message,
                "Usage Cache",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return Task.CompletedTask;
    }

    private async Task RefreshGitHubAsync(string? ghLogin) {
        var dispatcher = Application.Current.Dispatcher;
        var currentVersion = Interlocked.Increment(ref _gitHubRefreshVersion);
        using var refreshCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _gitHubRefreshCts, refreshCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        var cancellationToken = refreshCts.Token;
        var gitHubCredentials = await IntelligenceX.Telemetry.GitHub.GitHubCredentialResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var resolvedGitHubToken = gitHubCredentials.Token;
        var hasToken = gitHubCredentials.HasToken;
        var effectiveLogin = string.IsNullOrWhiteSpace(ghLogin) ? null : ghLogin.Trim();
        var preserveExistingData = ShouldPreserveGitHubDataDuringRefresh(effectiveLogin, hasToken);
        var observabilityTask = LoadGitHubObservabilitySummaryAsync(resolvedGitHubToken, cancellationToken);

        await dispatcher.InvokeAsync(() => {
            if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                return;
            }

            if (effectiveLogin is not null) {
                GitHub.UsernameInput = effectiveLogin;
            }

            if (!preserveExistingData && !hasToken && !GitHub.HasData) {
                GitHub.ClearProfileData();
            }

            GitHub.HasToken = hasToken;
            GitHub.IsLoading = hasToken || !string.IsNullOrWhiteSpace(effectiveLogin);
            GitHub.ErrorMessage = string.Empty;
        });

        if (!hasToken && string.IsNullOrWhiteSpace(effectiveLogin)) {
            var observabilityRefresh = await observabilityTask.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => {
                if (!IsLatestGitHubRefresh(currentVersion)) {
                    return;
                }

                _latestGitHubObservabilitySummary = observabilityRefresh.Summary;
                GitHub.ApplyObservabilitySummary(observabilityRefresh.Summary);
                RefreshGitHubLocalActivityCorrelationSummary();
                TryApplyGitHubAutoSyncStatus(observabilityRefresh.AutoSyncResult);
                GitHub.IsLoading = false;
            });
            Interlocked.CompareExchange(ref _gitHubRefreshCts, null, refreshCts);
            return;
        }

        try {
            var ghData = await _gitHubService.FetchAsync(effectiveLogin, resolvedGitHubToken, cancellationToken).ConfigureAwait(false);
            var observabilityRefresh = await observabilityTask.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                _latestGitHubObservabilitySummary = observabilityRefresh.Summary;
                GitHub.ApplyObservabilitySummary(observabilityRefresh.Summary);
                RefreshGitHubLocalActivityCorrelationSummary();
                TryApplyGitHubAutoSyncStatus(observabilityRefresh.AutoSyncResult);
                if (ghData is not null) {
                    GitHub.Apply(ghData);
                    return;
                }

                if (!preserveExistingData) {
                    GitHub.ClearProfileData();
                    if (effectiveLogin is not null) {
                        GitHub.UsernameInput = effectiveLogin;
                    }
                }

                if (!hasToken && !string.IsNullOrWhiteSpace(effectiveLogin)) {
                    GitHub.ErrorMessage = $"No public GitHub data was returned for '{effectiveLogin}'.";
                }
            });
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // A newer refresh superseded this one.
        } catch (Exception ghEx) {
            var observabilityRefresh = await observabilityTask.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                _latestGitHubObservabilitySummary = observabilityRefresh.Summary;
                GitHub.ApplyObservabilitySummary(observabilityRefresh.Summary);
                RefreshGitHubLocalActivityCorrelationSummary();
                TryApplyGitHubAutoSyncStatus(observabilityRefresh.AutoSyncResult);
                if (!preserveExistingData) {
                    GitHub.ClearProfileData();
                    if (effectiveLogin is not null) {
                        GitHub.UsernameInput = effectiveLogin;
                    }
                }

                GitHub.ErrorMessage = ghEx.Message;
            });
        } finally {
            await dispatcher.InvokeAsync(() => {
                if (!IsLatestGitHubRefresh(currentVersion)) {
                    return;
                }

                GitHub.IsLoading = false;
            });

            if (ReferenceEquals(Interlocked.CompareExchange(ref _gitHubRefreshCts, null, refreshCts), refreshCts)) {
                // cleared
            }
        }
    }

    private bool ShouldPreserveGitHubDataDuringRefresh(string? requestedLogin, bool hasToken) {
        if (!GitHub.HasData) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedLogin)) {
            return hasToken;
        }

        return string.Equals(GitHub.Login, requestedLogin, StringComparison.OrdinalIgnoreCase);
    }

    private void OnGitHubPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (string.Equals(e.PropertyName, nameof(GitHubViewModel.UsernameInput), StringComparison.Ordinal)) {
            _preferences.GitHubUsername = GitHub.UsernameInput?.Trim() ?? string.Empty;
            SavePreferences();
        }

        OnPropertyChanged(nameof(ShowCombinedGitHubPulse));
    }

    private void OnProviderPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not ProviderViewModel provider) {
            return;
        }

        if (!IsPersistedProviderProperty(e.PropertyName)) {
            return;
        }

        _preferences.Providers[provider.ProviderId] = provider.CaptureExplorerPreferences();
        SavePreferences();
    }

    private static bool IsPersistedProviderProperty(string? propertyName) {
        return string.Equals(propertyName, nameof(ProviderViewModel.SelectedRange), StringComparison.Ordinal)
               || string.Equals(propertyName, nameof(ProviderViewModel.SelectedEventSort), StringComparison.Ordinal)
               || string.Equals(propertyName, nameof(ProviderViewModel.SelectedProviderComparisonSort), StringComparison.Ordinal)
               || string.Equals(propertyName, nameof(ProviderViewModel.SelectedAccountFilter), StringComparison.Ordinal)
               || string.Equals(propertyName, nameof(ProviderViewModel.SelectedModelFilter), StringComparison.Ordinal)
               || string.Equals(propertyName, nameof(ProviderViewModel.SelectedSurfaceFilter), StringComparison.Ordinal);
    }

    private static string GetNextThemeMode(string mode) {
        return TrayThemeService.NormalizeThemeMode(mode) switch {
            TrayThemeService.SystemMode => TrayThemeService.DarkMode,
            TrayThemeService.DarkMode => TrayThemeService.LightMode,
            _ => TrayThemeService.SystemMode
        };
    }

    private void ConfigureRefreshTimer() {
        if (AutoRefreshIntervalSeconds <= 0) {
            _refreshTimer.Stop();
            _usageDirtyRefreshTimer.Stop();
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromSeconds(AutoRefreshIntervalSeconds);
        _refreshTimer.Start();
        if (HasPendingUsageChanges()) {
            _usageDirtyRefreshTimer.Stop();
            _usageDirtyRefreshTimer.Start();
        }
    }

    private ProviderExplorerPreferences? GetProviderExplorerPreferences(string providerId) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            return null;
        }

        return _preferences.Providers.TryGetValue(providerId, out var preferences)
            ? preferences
            : null;
    }

    private void SaveSelectedProviderPreference(string? providerId) {
        _preferences.SelectedProviderId = providerId;
        SavePreferences();
    }

    private Task CycleThemeModeAsync() {
        SetThemeMode(GetNextThemeMode(ThemeMode));
        return Task.CompletedTask;
    }

    public TrayWindowPlacement? GetSavedWindowPlacement() {
        if (_preferences.WindowPlacement is not { } placement) {
            return null;
        }

        return new TrayWindowPlacement {
            Left = placement.Left,
            Top = placement.Top
        };
    }

    public void SaveWindowPlacement(double left, double top) {
        if (!double.IsFinite(left) || !double.IsFinite(top)) {
            return;
        }

        if (_preferences.WindowPlacement is { } existingPlacement
            && Math.Abs(existingPlacement.Left - left) < 0.5d
            && Math.Abs(existingPlacement.Top - top) < 0.5d) {
            return;
        }

        _preferences.WindowPlacement = new TrayWindowPlacement {
            Left = left,
            Top = top
        };
        SavePreferences();
    }

    private void SavePreferences() {
        try {
            _preferencesStore.Save(_preferences);
        } catch {
            // Keep the tray responsive even if settings persistence fails.
        }
    }

    private void EvaluateLimitNotifications(IReadOnlyDictionary<string, ProviderLimitSnapshot> limitSnapshots) {
        if (!NotificationsEnabled) {
            return;
        }

        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in limitSnapshots.Values) {
            foreach (var window in snapshot.Windows) {
                var level = GetNotificationLevel(window.UsedPercent);
                if (level is null) {
                    continue;
                }

                var key = BuildLimitNotificationKey(snapshot.ProviderId, window, level.Value);
                activeKeys.Add(key);

                if (!_hasCompletedInitialRefresh) {
                    _activeLimitNotificationKeys.Add(key);
                    continue;
                }

                if (!_activeLimitNotificationKeys.Add(key)) {
                    continue;
                }

                NotificationRequested?.Invoke(this, new TrayNotificationRequestedEventArgs(
                    title: level == LimitNotificationLevel.Exhausted
                        ? snapshot.DisplayName + " limit exhausted"
                        : snapshot.DisplayName + " limit warning",
                    message: BuildLimitNotificationMessage(snapshot, window),
                    isCritical: level == LimitNotificationLevel.Exhausted,
                    providerId: snapshot.ProviderId));
            }
        }

        _activeLimitNotificationKeys.RemoveWhere(key => !activeKeys.Contains(key));
    }

    private async Task RefreshProviderLimitsAsync(IReadOnlyCollection<string> providerIds, string usageScanInfo) {
        if (providerIds.Count == 0) {
            return;
        }

        if (HasFreshLimitSnapshotsFor(providerIds)) {
            ApplyLatestLimitSnapshotsToProviders();
            return;
        }

        var dispatcher = Application.Current.Dispatcher;
        var currentVersion = Interlocked.Increment(ref _limitRefreshVersion);
        using var refreshCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _limitRefreshCts, refreshCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        var cancellationToken = refreshCts.Token;

        try {
            var limitSnapshots = await _limitService.FetchAsync(providerIds, cancellationToken).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentLimitRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                foreach (var (providerId, snapshot) in limitSnapshots) {
                    _latestLimitSnapshots[providerId] = snapshot;
                }

                _lastLimitRefreshUtc = DateTimeOffset.UtcNow;
                ApplyLatestLimitSnapshotsToProviders();
                EvaluateLimitNotifications(limitSnapshots);
                if (!IsLoading) {
                    StatusText = usageScanInfo + " • Live limits updated.";
                }
            });
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // A newer live-limit refresh superseded this one.
        } catch (Exception ex) {
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentLimitRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                if (!IsLoading) {
                    StatusText = usageScanInfo + " • Live limits unavailable: " + ex.Message;
                }
            });
        } finally {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _limitRefreshCts, null, refreshCts), refreshCts)) {
                // cleared
            }
        }
    }

    private bool HasFreshLimitSnapshotsFor(IEnumerable<string> providerIds) {
        if (_latestLimitSnapshots.Count == 0 || _lastLimitRefreshUtc == default) {
            return false;
        }

        var age = DateTimeOffset.UtcNow - _lastLimitRefreshUtc;
        if (age.TotalSeconds > FreshLimitSnapshotWindowSeconds) {
            return false;
        }

        foreach (var providerId in providerIds) {
            if (!_latestLimitSnapshots.ContainsKey(providerId)) {
                return false;
            }
        }

        return true;
    }

    private void ApplyLatestLimitSnapshotsToProviders() {
        foreach (var provider in Providers.Where(static provider => provider.ProviderId != "__all__")) {
            var snapshot = _latestLimitSnapshots.TryGetValue(provider.ProviderId, out var limitSnapshot)
                ? limitSnapshot
                : null;
            provider.ApplyLimitSnapshot(snapshot);
            provider.ApplyUsageScopeSummary(BuildUsageScopeSummary(provider.ProviderId, snapshot));
        }

        if (Providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal)) is { } allProvider) {
            allProvider.SetProviderComparisonHealth(BuildProviderComparisonHealth(Providers));
            allProvider.ApplyCodeChurnSummary(_latestGitCodeChurnSummary);
            allProvider.ApplyGitHubLocalActivityCorrelationSummary(_latestGitHubLocalActivityCorrelationSummary);
        }
    }

    private string BuildUsageRefreshStatus(string scanInfo, IReadOnlyCollection<string> providerIds) {
        if (providerIds.Count == 0) {
            return scanInfo;
        }

        if (HasFreshLimitSnapshotsFor(providerIds)) {
            return scanInfo + " • Live limits current.";
        }

        if (_latestLimitSnapshots.Count > 0) {
            return scanInfo + " • Refreshing live limits in background...";
        }

        return scanInfo + " • Loading live limits in background...";
    }

    private static LimitNotificationLevel? GetNotificationLevel(double? usedPercent) {
        if (!usedPercent.HasValue) {
            return null;
        }

        if (usedPercent.Value >= LimitExhaustedThresholdPercent) {
            return LimitNotificationLevel.Exhausted;
        }

        if (usedPercent.Value >= LimitWarningThresholdPercent) {
            return LimitNotificationLevel.Warning;
        }

        return null;
    }

    private static string BuildLimitNotificationKey(string providerId, ProviderLimitWindow window, LimitNotificationLevel level) {
        var resetToken = window.ResetsAt?.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) ?? "no-reset";
        return string.Join("|", providerId, window.Key, resetToken, level);
    }

    private static string BuildLimitNotificationMessage(ProviderLimitSnapshot snapshot, ProviderLimitWindow window) {
        var parts = new List<string> {
            window.Label + " is at " + (window.UsedPercent ?? 0d).ToString("0.#", CultureInfo.InvariantCulture) + "%"
        };

        if (!string.IsNullOrWhiteSpace(snapshot.AccountLabel)) {
            parts.Add(snapshot.AccountLabel);
        }

        parts.Add(FormatResetText(window.ResetsAt));
        return string.Join(" • ", parts);
    }

    private static string FormatResetText(DateTimeOffset? resetsAt) {
        if (!resetsAt.HasValue) {
            return "reset unknown";
        }

        var local = resetsAt.Value.ToLocalTime();
        var remaining = local - DateTimeOffset.Now;
        if (remaining.TotalMinutes > 0 && remaining.TotalHours < 24) {
            if (remaining.TotalHours >= 1) {
                return "resets in "
                       + Math.Floor(remaining.TotalHours).ToString(CultureInfo.InvariantCulture)
                       + "h " + remaining.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            }

            return "resets in " + Math.Max(1, remaining.Minutes).ToString(CultureInfo.InvariantCulture) + "m";
        }

        return "resets " + local.ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
    }

    private static int NormalizeRefreshIntervalSeconds(int seconds) {
        if (seconds < 0) {
            return DefaultRefreshIntervalSeconds;
        }

        return seconds;
    }

    private static string FormatRefreshInterval(int seconds) {
        if (seconds < 60) {
            return seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        if (seconds % 60 == 0) {
            var minutes = seconds / 60;
            return minutes == 1
                ? "1 minute"
                : minutes.ToString(CultureInfo.InvariantCulture) + " minutes";
        }

        return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private bool IsCurrentGitHubRefresh(int version, CancellationToken cancellationToken) {
        return !cancellationToken.IsCancellationRequested && IsLatestGitHubRefresh(version);
    }

    private bool IsLatestGitHubRefresh(int version) {
        return version == Volatile.Read(ref _gitHubRefreshVersion);
    }

    private bool IsCurrentLimitRefresh(int version, CancellationToken cancellationToken) {
        return !cancellationToken.IsCancellationRequested && IsLatestLimitRefresh(version);
    }

    private bool IsLatestLimitRefresh(int version) {
        return version == Volatile.Read(ref _limitRefreshVersion);
    }

    private void OnUsageChangeDetected(object? sender, UsageChangeDetectedEventArgs e) {
        _usageDirtyAtUtc = e.ChangedAtUtc;
        _latestUsageChangePath = !string.IsNullOrWhiteSpace(e.ChangedPath)
            ? Path.GetFileName(e.ChangedPath)
            : Path.GetFileName(e.RootPath);

        if (!IsLoading) {
            StatusText = string.IsNullOrWhiteSpace(_latestUsageChangePath)
                ? "Usage changes detected. Refresh queued."
                : "Usage changes detected (" + _latestUsageChangePath + "). Refresh queued.";
        }

        if (AutoRefreshIntervalSeconds <= 0) {
            _usageDirtyRefreshTimer.Stop();
            return;
        }

        _usageDirtyRefreshTimer.Stop();
        _usageDirtyRefreshTimer.Start();
    }

    private void RefreshProviderSelectionState() {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(IsGitHubTabSelected));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ShowUsageContent));
        OnPropertyChanged(nameof(ShowGitHubContent));
        OnPropertyChanged(nameof(ShowCombinedGitHubPulse));
        OnPropertyChanged(nameof(ShowLoadingOverlay));
        OnPropertyChanged(nameof(HeaderRefreshCommand));
        OnPropertyChanged(nameof(HeaderRefreshLabel));
    }

    private GitHubObservabilitySummaryData LoadGitHubObservabilitySummarySafe() {
        try {
            return _gitHubObservabilitySummaryService.Load();
        } catch {
            return GitHubObservabilitySummaryData.Empty;
        }
    }

    private async Task<GitHubObservabilityRefreshResult> LoadGitHubObservabilitySummaryAsync(
        string? gitHubToken,
        CancellationToken cancellationToken) {
        GitHubRepositoryWatchAutoSyncResult? autoSyncResult = null;
        try {
            autoSyncResult = await TryAutoSyncGitHubObservabilityAsync(gitHubToken, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch {
            // Keep GitHub profile refresh resilient even if background watch sync fails.
        }

        return new GitHubObservabilityRefreshResult(LoadGitHubObservabilitySummarySafe(), autoSyncResult);
    }

    private async Task<GitHubRepositoryWatchAutoSyncResult?> TryAutoSyncGitHubObservabilityAsync(
        string? gitHubToken,
        CancellationToken cancellationToken) {
        if (!GitHubWatchAutoSyncEnabled || string.IsNullOrWhiteSpace(gitHubToken)) {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (_lastGitHubWatchAutoSyncAttemptUtc != default &&
            nowUtc - _lastGitHubWatchAutoSyncAttemptUtc < TimeSpan.FromSeconds(GitHubWatchAutoSyncMinimumIntervalSeconds)) {
            return null;
        }

        _lastGitHubWatchAutoSyncAttemptUtc = nowUtc;
        return await _gitHubWatchAutoSyncService.SyncIfNeededAsync(
            gitHubToken,
            new GitHubRepositoryWatchAutoSyncOptions {
                SnapshotFreshnessWindow = TimeSpan.FromSeconds(GitHubWatchSnapshotFreshnessSeconds),
                ForkFreshnessWindow = TimeSpan.FromSeconds(GitHubWatchForkFreshnessSeconds),
                IncludeForks = true,
                ForkLimit = 10,
                StargazerFreshnessWindow = TimeSpan.FromSeconds(GitHubWatchStargazerFreshnessSeconds),
                IncludeStargazers = true,
                StargazerLimit = 200
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void TryApplyGitHubAutoSyncStatus(GitHubRepositoryWatchAutoSyncResult? result) {
        if (result is null || !result.ShouldSurfaceStatus || IsLoading) {
            return;
        }

        StatusText = result.Message;
    }

    private GitCodeChurnSummaryData LoadGitCodeChurnSummarySafe() {
        try {
            return _gitCodeChurnSummaryService.Load();
        } catch {
            return GitCodeChurnSummaryData.Empty;
        }
    }

    private GitHubLocalActivityCorrelationSummaryData BuildGitHubLocalActivityCorrelationSummary() {
        var usageEvents = _displayedProviderEvents
            .Where(static pair => !string.Equals(pair.Key, "github", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static pair => pair.Value)
            .ToArray();
        return GitHubLocalActivityCorrelationSummaryBuilder.Build(
            _latestGitCodeChurnSummary,
            usageEvents,
            _latestGitHubObservabilitySummary);
    }

    private GitHubRepositoryClusterSummaryData BuildGitHubRepositoryClusterSummary() {
        return GitHubRepositoryClusterSummaryBuilder.Build(
            _latestGitHubObservabilitySummary,
            _latestGitHubLocalActivityCorrelationSummary);
    }

    private void RefreshGitHubLocalActivityCorrelationSummary(IEnumerable<ProviderViewModel>? providers = null) {
        _latestGitHubLocalActivityCorrelationSummary = BuildGitHubLocalActivityCorrelationSummary();
        _latestGitHubRepositoryClusterSummary = BuildGitHubRepositoryClusterSummary();
        GitHub.ApplyLocalActivityCorrelationSummary(_latestGitHubLocalActivityCorrelationSummary);
        GitHub.ApplyRepositoryClusterSummary(_latestGitHubRepositoryClusterSummary);
        ApplyGitHubLocalActivityCorrelationSummary(
            providers ?? Providers,
            _latestGitHubLocalActivityCorrelationSummary);
        ApplyGitHubRepositoryClusterSummary(
            providers ?? Providers,
            _latestGitHubRepositoryClusterSummary);
    }

    private static void ApplyCodeChurnSummary(IEnumerable<ProviderViewModel> providers, GitCodeChurnSummaryData summary) {
        foreach (var provider in providers.Where(static provider =>
                     string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal))) {
            provider.ApplyCodeChurnSummary(summary);
        }
    }

    private static void ApplyGitHubLocalActivityCorrelationSummary(
        IEnumerable<ProviderViewModel> providers,
        GitHubLocalActivityCorrelationSummaryData summary) {
        foreach (var provider in providers.Where(static provider =>
                     string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal))) {
            provider.ApplyGitHubLocalActivityCorrelationSummary(summary);
        }
    }

    private static void ApplyGitHubRepositoryClusterSummary(
        IEnumerable<ProviderViewModel> providers,
        GitHubRepositoryClusterSummaryData summary) {
        foreach (var provider in providers.Where(static provider =>
                     string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal))) {
            provider.ApplyGitHubRepositoryClusterSummary(summary);
        }
    }

    private sealed class GitHubObservabilityRefreshResult {
        public GitHubObservabilityRefreshResult(
            GitHubObservabilitySummaryData summary,
            GitHubRepositoryWatchAutoSyncResult? autoSyncResult) {
            Summary = summary ?? GitHubObservabilitySummaryData.Empty;
            AutoSyncResult = autoSyncResult;
        }

        public GitHubObservabilitySummaryData Summary { get; }
        public GitHubRepositoryWatchAutoSyncResult? AutoSyncResult { get; }
    }

    private Task ToggleSelectedProviderFavoriteAsync() {
        ToggleFavoriteProvider(SelectedProvider?.ProviderId);
        return Task.CompletedTask;
    }

    private bool IsFavoriteProvider(string? providerId) {
        return !string.IsNullOrWhiteSpace(providerId) && _favoriteProviderIds.Contains(providerId);
    }

    private List<ProviderViewModel> OrderProviders(IEnumerable<ProviderViewModel> providers) {
        return providers
            .OrderBy(GetProviderGroupOrder)
            .ThenByDescending(static provider => provider.IsFavorite)
            .ThenBy(static provider => provider.SortOrder)
            .ThenBy(static provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private int GetProviderGroupOrder(ProviderViewModel provider) {
        if (string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal)) {
            return 0;
        }

        if (provider.IsFavorite) {
            return 1;
        }

        if (string.Equals(provider.ProviderId, "__github__", StringComparison.Ordinal)) {
            return 3;
        }

        return 2;
    }

    private void ReorderProviders() {
        var selectedProvider = SelectedProvider;
        var orderedProviders = OrderProviders(Providers);
        Providers.Clear();
        foreach (var provider in orderedProviders) {
            Providers.Add(provider);
        }

        if (selectedProvider is not null) {
            SelectedProvider = Providers.FirstOrDefault(provider => ReferenceEquals(provider, selectedProvider)) ?? Providers.FirstOrDefault();
        }
    }

    private string ResolveProviderDisplayName(string providerId) {
        return Providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, providerId, StringComparison.Ordinal))?.DisplayName
               ?? ProviderMetadata.Resolve(providerId).DisplayName;
    }

    private void UnsubscribeProviders() {
        foreach (var provider in Providers) {
            provider.PropertyChanged -= OnProviderPropertyChanged;
        }
    }

    private bool ApplyCachedUsageSnapshot() {
        var cachedSnapshot = _usageSnapshotStore.Load();
        var serviceSnapshot = _usageService.TryLoadCachedSnapshot();
        if (serviceSnapshot is not null && serviceSnapshot.Events.Count > 0) {
            var serviceCache = new TrayUsageSnapshotStore.TrayUsageSnapshotCache(
                serviceSnapshot.ScannedAtUtc,
                serviceSnapshot.DiscoveredProviderIds.ToList(),
                serviceSnapshot.SourceRoots.ToList(),
                serviceSnapshot.Events.ToList(),
                serviceSnapshot.RawEvents.ToList(),
                serviceSnapshot.Health);
            if (ShouldPreferCachedSnapshot(serviceCache, cachedSnapshot)) {
                cachedSnapshot = serviceCache;
                _usageSnapshotStore.Save(
                    serviceSnapshot.ScannedAtUtc,
                    serviceSnapshot.Events,
                    serviceSnapshot.DiscoveredProviderIds,
                    serviceSnapshot.SourceRoots,
                    serviceSnapshot.Health,
                    serviceSnapshot.RawEvents);
            }
        }

        if (cachedSnapshot is null || cachedSnapshot.Events.Count == 0) {
            return false;
        }

        var byProvider = cachedSnapshot.Events
            .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => ProviderMetadata.Resolve(group.Key).SortOrder)
            .Select(group => new ProviderRefreshData(
                group.Key,
                group.ToList(),
                cachedSnapshot.RawEvents
                    .Where(rawEvent => string.Equals(rawEvent.ProviderId, group.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList()))
            .ToList();
        var mergedProviderData = BuildMergedProviderData(byProvider, cachedSnapshot.DiscoveredProviderIds);
        var providerSnapshots = mergedProviderData.ToDictionary(
            static group => group.ProviderId,
            static group => ProviderRefreshSnapshot.FromEvents(group.Events),
            StringComparer.OrdinalIgnoreCase);
        UpdateLatestSourceRoots(cachedSnapshot.SourceRoots);
        var cachedProviders = BuildUsageProviders(
            mergedProviderData,
            cachedSnapshot.Events,
            cachedSnapshot.ScannedAtUtc,
            cachedSnapshot.Health,
            providerDelta: null,
            providerHistory: null);
        _latestGitCodeChurnSummary = LoadGitCodeChurnSummarySafe();
        ApplyCodeChurnSummary(cachedProviders, _latestGitCodeChurnSummary);
        ReplaceProviders(cachedProviders);
        UpdateDisplayedProviderEvents(mergedProviderData);
        RefreshGitHubLocalActivityCorrelationSummary();
        _previousProviderSnapshots = new Dictionary<string, ProviderRefreshSnapshot>(providerSnapshots, StringComparer.OrdinalIgnoreCase);
        _latestUsageHealth = cachedSnapshot.Health;
        _lastUsageSnapshotScannedAtUtc = cachedSnapshot.ScannedAtUtc;
        _lastUsageRootDiscoveryUtc = cachedSnapshot.ScannedAtUtc;
        _usageChangeWatcher.SetRoots(cachedSnapshot.SourceRoots);
        LastRefreshed = cachedSnapshot.ScannedAtUtc.ToLocalTime();
        StatusText = "Loaded last usage snapshot from disk. Watching for local changes.";
        LoadingDetailText = "Showing cached usage while providers refresh.";
        return true;
    }

    private static bool ShouldPreferCachedSnapshot(
        TrayUsageSnapshotStore.TrayUsageSnapshotCache candidate,
        TrayUsageSnapshotStore.TrayUsageSnapshotCache? current) {
        if (current is null || current.Events.Count == 0) {
            return true;
        }

        var candidateLatestEventUtc = GetLatestEventTimestampUtc(candidate.Events);
        var currentLatestEventUtc = GetLatestEventTimestampUtc(current.Events);
        if (candidateLatestEventUtc != currentLatestEventUtc) {
            return candidateLatestEventUtc > currentLatestEventUtc;
        }

        var candidateProviderCount = candidate.DiscoveredProviderIds.Count > 0
            ? candidate.DiscoveredProviderIds.Count
            : candidate.Events
                .Select(static item => item.ProviderId)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        var currentProviderCount = current.DiscoveredProviderIds.Count > 0
            ? current.DiscoveredProviderIds.Count
            : current.Events
                .Select(static item => item.ProviderId)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        if (candidateProviderCount != currentProviderCount) {
            return candidateProviderCount > currentProviderCount;
        }

        if (candidate.Events.Count != current.Events.Count) {
            return candidate.Events.Count > current.Events.Count;
        }

        return candidate.ScannedAtUtc > current.ScannedAtUtc;
    }

    private static DateTimeOffset GetLatestEventTimestampUtc(IEnumerable<UsageEventRecord> events) {
        var latestEventUtc = DateTimeOffset.MinValue;
        foreach (var usageEvent in events) {
            if (usageEvent.TimestampUtc > latestEventUtc) {
                latestEventUtc = usageEvent.TimestampUtc;
            }
        }

        return latestEventUtc;
    }

    private void ApplyProgressiveUsageProviders(
        IReadOnlyCollection<string> discoveredProviderIds,
        IReadOnlyDictionary<string, List<UsageEventRecord>> providerEventsById) {
        if (discoveredProviderIds.Count == 0 && providerEventsById.Count == 0) {
            return;
        }

        var currentProviderData = _displayedProviderEvents
            .Select(static pair => new ProviderRefreshData(pair.Key, pair.Value, pair.Value))
            .ToList();
        foreach (var (providerId, events) in providerEventsById) {
            var existingIndex = currentProviderData.FindIndex(group => string.Equals(group.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0) {
                currentProviderData[existingIndex] = new ProviderRefreshData(providerId, events, events);
            } else {
                currentProviderData.Add(new ProviderRefreshData(providerId, events, events));
            }
        }

        var providerData = BuildMergedProviderData(currentProviderData, discoveredProviderIds);
        var allEvents = providerData
            .SelectMany(static provider => provider.Events)
            .OrderByDescending(static usageEvent => usageEvent.TimestampUtc)
            .ToList();
        var progressiveProviders = BuildUsageProviders(
            providerData,
            allEvents,
            DateTimeOffset.UtcNow,
            _latestUsageHealth,
            providerHistory: null,
            providerDelta: null);
        ApplyCodeChurnSummary(progressiveProviders, _latestGitCodeChurnSummary);
        ReplaceProviders(progressiveProviders);
        UpdateDisplayedProviderEvents(providerData);
        RefreshGitHubLocalActivityCorrelationSummary();
    }

    private void UpdateDisplayedProviderEvents(IEnumerable<ProviderRefreshData> providerData) {
        _displayedProviderEvents = providerData.ToDictionary(
            static group => group.ProviderId,
            static group => group.Events.ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateLatestSourceRoots(IReadOnlyList<SourceRootRecord> sourceRoots) {
        _latestSourceRootsByProvider = (sourceRoots ?? Array.Empty<SourceRootRecord>())
            .Where(static root => !string.IsNullOrWhiteSpace(root.ProviderId))
            .GroupBy(static root => root.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private UsageTelemetryScopeSummary BuildUsageScopeSummary(string providerId, ProviderLimitSnapshot? limitSnapshot) {
        _latestSourceRootsByProvider.TryGetValue(providerId, out var providerRoots);
        return UsageTelemetryScopeSummaryBuilder.Build(providerId, providerRoots, limitSnapshot);
    }

    private List<ProviderViewModel> BuildUsageProviders(
        IReadOnlyList<ProviderRefreshData> providerData,
        List<UsageEventRecord> allEvents,
        DateTimeOffset scannedAtUtc,
        UsageTelemetrySnapshotHealth? usageHealth,
        IReadOnlyDictionary<string, ProviderComparisonDeltaInfo>? providerDelta,
        IReadOnlyDictionary<string, ProviderComparisonHistoryInfo>? providerHistory) {
        var newProviders = new List<ProviderViewModel>();
        var shouldShowCombinedProvider = providerData.Count > 0 || allEvents.Count > 0;
        if (shouldShowCombinedProvider) {
            var allVm = BuildCombinedProviderViewModel(
                allEvents,
                providerData.SelectMany(static group => group.RawEvents).ToList(),
                scannedAtUtc);
            ApplyUsageHealth(allVm, usageHealth, providerId: null);
            allVm.ApplyUsageScopeSummary(null);
            allVm.ApplyRefreshDelta(
                providerDelta?.Values.Where(static delta => delta.TokenDelta > 0L).Sum(static delta => delta.TokenDelta) ?? 0L,
                providerDelta?.Values.Where(static delta => delta.EventDelta > 0).Sum(static delta => delta.EventDelta) ?? 0);
            allVm.ApplyExplorerPreferences(GetProviderExplorerPreferences(allVm.ProviderId));
            newProviders.Add(allVm);
        }

        foreach (var group in providerData) {
            var vm = BuildProviderViewModel(group.ProviderId, group.Events, group.RawEvents);
            vm.LastUpdated = scannedAtUtc;
            vm.IsFavorite = IsFavoriteProvider(group.ProviderId);
            ApplyUsageHealth(vm, usageHealth, group.ProviderId);
            if (providerDelta is not null && providerDelta.TryGetValue(group.ProviderId, out var deltaInfo)) {
                vm.ApplyRefreshDelta(deltaInfo.TokenDelta, deltaInfo.EventDelta);
            } else {
                vm.ApplyRefreshDelta(0L, 0);
            }

            if (_latestLimitSnapshots.TryGetValue(group.ProviderId, out var limitSnapshot)) {
                vm.ApplyLimitSnapshot(limitSnapshot);
            }
            vm.ApplyUsageScopeSummary(BuildUsageScopeSummary(group.ProviderId, _latestLimitSnapshots.TryGetValue(group.ProviderId, out var scopeSnapshot) ? scopeSnapshot : null));

            vm.ApplyExplorerPreferences(GetProviderExplorerPreferences(vm.ProviderId));
            newProviders.Add(vm);
        }

        if (newProviders.FirstOrDefault(provider => string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal)) is { } allProvider) {
            allProvider.SetProviderComparisonHealth(BuildProviderComparisonHealth(newProviders));
            allProvider.SetProviderComparisonDelta(providerDelta);
            allProvider.SetProviderComparisonHistory(providerHistory);
            allProvider.SetProviderComparisonFavorites(_favoriteProviderIds);
        }

        newProviders.Add(BuildGitHubProviderViewModel());
        return newProviders;
    }

    private void ReplaceProviders(IEnumerable<ProviderViewModel> providers) {
        var preferredSelection = SelectedProvider?.ProviderId ?? _preferences.SelectedProviderId;
        var orderedProviders = OrderProviders(providers);
        UnsubscribeProviders();
        Providers.Clear();
        foreach (var provider in orderedProviders) {
            provider.RefreshIconGeometry();
            if (provider.ProviderId != "__github__") {
                provider.PropertyChanged += OnProviderPropertyChanged;
            }

            Providers.Add(provider);
        }

        var restored = !string.IsNullOrWhiteSpace(preferredSelection)
            ? Providers.FirstOrDefault(p => string.Equals(p.ProviderId, preferredSelection, StringComparison.Ordinal))
            : null;
        SelectedProvider = restored
                           ?? Providers.FirstOrDefault(static provider => provider.IsFavorite)
                           ?? Providers.FirstOrDefault();
        RefreshProviderSelectionState();
    }

    private static ProviderViewModel BuildProviderViewModel(string providerId, List<UsageEventRecord> events, List<UsageEventRecord>? rawEvents = null) {
        var vm = new ProviderViewModel();
        var info = ProviderMetadata.Resolve(providerId);
        vm.ApplyProviderInfo(info);
        vm.ApplyUsageEvents(events);
        vm.ApplyConversationEvents(rawEvents ?? events);
        return vm;
    }

    private static ProviderViewModel BuildCombinedProviderViewModel(
        List<UsageEventRecord> events,
        List<UsageEventRecord> rawEvents,
        DateTimeOffset scannedAtUtc) {
        var allVm = BuildProviderViewModel("__all__", events, rawEvents);
        allVm.DisplayName = "All";
        allVm.ShortName = "All";
        allVm.IconKey = "IconIx";
        allVm.SortOrder = -1;
        allVm.AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(155, 233, 168)));
        allVm.InputColor = Color.FromRgb(155, 233, 168);
        allVm.OutputColor = Color.FromRgb(64, 196, 99);
        allVm.LastUpdated = scannedAtUtc;
        allVm.IsFavorite = false;
        return allVm;
    }

    private ProviderViewModel BuildGitHubProviderViewModel() {
        var gitHubProvider = new ProviderViewModel {
            ProviderId = "__github__",
            DisplayName = "GitHub",
            ShortName = "GitHub",
            IconKey = "IconGitHub",
            SortOrder = 999,
            IsFavorite = IsFavoriteProvider("__github__"),
            AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(64, 196, 99))),
            InputColor = Color.FromRgb(155, 233, 168),
            OutputColor = Color.FromRgb(64, 196, 99)
        };
        gitHubProvider.ApplyRefreshDelta(0L, 0);
        return gitHubProvider;
    }

    private static void ApplyUsageHealth(
        ProviderViewModel provider,
        UsageTelemetrySnapshotHealth? health,
        string? providerId) {
        if (provider is null) {
            return;
        }

        if (health is null) {
            provider.UsageHealthSummary = null;
            provider.UsageHealthDetail = null;
            provider.UsageHealthAccountsText = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(providerId)) {
            provider.UsageHealthSummary = BuildOverallUsageHealthSummary(health);
            provider.UsageHealthDetail = BuildOverallUsageHealthDetail(health);
            provider.UsageHealthAccountsText = BuildUsageHealthAccountsText(health.AccountLabels);
            return;
        }

        var providerHealth = health.ProviderHealth.FirstOrDefault(item =>
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (providerHealth is null) {
            provider.UsageHealthSummary = null;
            provider.UsageHealthDetail = null;
            provider.UsageHealthAccountsText = null;
            return;
        }

        provider.UsageHealthSummary = BuildProviderUsageHealthSummary(health, providerHealth);
        provider.UsageHealthDetail = BuildProviderUsageHealthDetail(providerHealth);
        provider.UsageHealthAccountsText = BuildUsageHealthAccountsText(providerHealth.AccountLabels);
    }

    private static string BuildOverallUsageHealthSummary(UsageTelemetrySnapshotHealth health) {
        var parts = new List<string> {
            health.IsCachedSnapshot ? "Cached snapshot" : "Live scan",
            health.ProviderCount.ToString(CultureInfo.InvariantCulture) + " providers",
            health.RootsCount.ToString(CultureInfo.InvariantCulture) + " roots",
            health.AccountCount.ToString(CultureInfo.InvariantCulture) + " usage accounts"
        };
        if (health.LatestEventUtc.HasValue) {
            parts.Add("latest " + health.LatestEventUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture));
        }

        return string.Join(" • ", parts);
    }

    private static string BuildOverallUsageHealthDetail(UsageTelemetrySnapshotHealth health) {
        var parts = new List<string> {
            health.EventsCount.ToString(CultureInfo.InvariantCulture) + " rollups"
        };
        if (health.ReusedArtifacts > 0) {
            parts.Add(health.ReusedArtifacts.ToString(CultureInfo.InvariantCulture) + " cached artifacts");
        }
        if (health.ParsedArtifacts > 0) {
            parts.Add(health.ParsedArtifacts.ToString(CultureInfo.InvariantCulture) + " parsed");
        }
        if (health.DuplicateRecordsCollapsed > 0) {
            parts.Add(health.DuplicateRecordsCollapsed.ToString(CultureInfo.InvariantCulture) + " deduped");
        }
        if (health.IsPartialScan) {
            parts.Add("partial scan");
        }
        if (health.IssueCount > 0) {
            parts.Add(health.IssueCount.ToString(CultureInfo.InvariantCulture) + " issues");
        }

        return string.Join(" • ", parts);
    }

    private static string BuildProviderUsageHealthSummary(
        UsageTelemetrySnapshotHealth overallHealth,
        UsageTelemetryProviderHealth providerHealth) {
        var parts = new List<string> {
            overallHealth.IsCachedSnapshot ? "Cached snapshot" : "Live scan",
            providerHealth.RootsCount.ToString(CultureInfo.InvariantCulture) + " roots",
            providerHealth.AccountCount.ToString(CultureInfo.InvariantCulture) + " usage accounts"
        };
        if (providerHealth.LatestEventUtc.HasValue) {
            parts.Add("latest " + providerHealth.LatestEventUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture));
        }

        return string.Join(" • ", parts);
    }

    private static string BuildProviderUsageHealthDetail(UsageTelemetryProviderHealth providerHealth) {
        var parts = new List<string> {
            providerHealth.EventsCount.ToString(CultureInfo.InvariantCulture) + " rollups"
        };
        if (providerHealth.ReusedArtifacts > 0) {
            parts.Add(providerHealth.ReusedArtifacts.ToString(CultureInfo.InvariantCulture) + " cached artifacts");
        }
        if (providerHealth.ParsedArtifacts > 0) {
            parts.Add(providerHealth.ParsedArtifacts.ToString(CultureInfo.InvariantCulture) + " parsed");
        }
        if (providerHealth.DuplicateRecordsCollapsed > 0) {
            parts.Add(providerHealth.DuplicateRecordsCollapsed.ToString(CultureInfo.InvariantCulture) + " deduped");
        }
        if (providerHealth.IsPartialScan) {
            parts.Add("partial scan");
        }

        return string.Join(" • ", parts);
    }

    private static string? BuildUsageHealthAccountsText(IReadOnlyList<string>? accountLabels) {
        if (accountLabels is not { Count: > 0 }) {
            return null;
        }

        const int maxVisible = 3;
        var visible = accountLabels
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Take(maxVisible)
            .ToArray();
        if (visible.Length == 0) {
            return null;
        }

        var text = "Usage accounts: " + string.Join(", ", visible);
        var hiddenCount = accountLabels.Count - visible.Length;
        if (hiddenCount > 0) {
            text += " +" + hiddenCount.ToString(CultureInfo.InvariantCulture) + " more";
        }

        return text;
    }

    private static List<ProviderRefreshData> BuildMergedProviderData(
        IReadOnlyList<ProviderRefreshData> providerData,
        IEnumerable<string> discoveredProviderIds) {
        var mergedProviderIds = discoveredProviderIds
            .Concat(providerData.Select(static provider => provider.ProviderId))
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static providerId => ProviderMetadata.Resolve(providerId).SortOrder)
            .ThenBy(static providerId => ProviderMetadata.Resolve(providerId).DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return mergedProviderIds
            .Select(providerId => providerData.FirstOrDefault(group => string.Equals(group.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                                  ?? new ProviderRefreshData(providerId, [], []))
            .ToList();
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush) {
        brush.Freeze();
        return brush;
    }

    private static IReadOnlyDictionary<string, ProviderComparisonHealthInfo> BuildProviderComparisonHealth(IEnumerable<ProviderViewModel> providers) {
        var health = new Dictionary<string, ProviderComparisonHealthInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers.Where(static provider => !string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal))) {
            var summary = BuildProviderHealthSummary(provider);
            if (summary is null) {
                continue;
            }

            health[provider.ProviderId] = summary;
        }

        return health;
    }

    private static ProviderComparisonHealthInfo? BuildProviderHealthSummary(ProviderViewModel provider) {
        if (provider.LimitWindows.Count > 0) {
            var hottestWindow = provider.LimitWindows
                .OrderByDescending(window => window.UsedPercent ?? -1d)
                .FirstOrDefault();
            if (hottestWindow is not null && hottestWindow.UsedPercent is double usedPercent) {
                var color = usedPercent >= 100d
                    ? Color.FromRgb(232, 107, 115)
                    : usedPercent >= 90d
                        ? Color.FromRgb(240, 192, 64)
                        : Color.FromRgb(144, 208, 160);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return new ProviderComparisonHealthInfo {
                    SummaryText = hottestWindow.Label + " " + usedPercent.ToString("0.#", CultureInfo.InvariantCulture) + "% • " + hottestWindow.ResetText,
                    SummaryBrush = brush
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(provider.LimitStatusMessage)) {
            var brush = new SolidColorBrush(Color.FromRgb(144, 144, 184));
            brush.Freeze();
            return new ProviderComparisonHealthInfo {
                SummaryText = provider.LimitStatusMessage!,
                SummaryBrush = brush
            };
        }

        if (!string.IsNullOrWhiteSpace(provider.LimitSummary)) {
            var brush = new SolidColorBrush(Color.FromRgb(144, 144, 184));
            brush.Freeze();
            return new ProviderComparisonHealthInfo {
                SummaryText = provider.LimitSummary!,
                SummaryBrush = brush
            };
        }

        return new ProviderComparisonHealthInfo {
            SummaryText = "No live limit data",
            SummaryBrush = Frozen(new SolidColorBrush(Color.FromRgb(96, 96, 136)))
        };
    }

    private static IReadOnlyDictionary<string, ProviderComparisonDeltaInfo> BuildProviderComparisonDelta(
        IEnumerable<ProviderRefreshData> currentProviders,
        IReadOnlyDictionary<string, ProviderRefreshSnapshot> previousSnapshots) {
        var delta = new Dictionary<string, ProviderComparisonDeltaInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in currentProviders) {
            var currentSnapshot = ProviderRefreshSnapshot.FromEvents(provider.Events);
            if (!previousSnapshots.TryGetValue(provider.ProviderId, out var previousSnapshot)) {
                delta[provider.ProviderId] = new ProviderComparisonDeltaInfo {
                    SummaryText = "First live snapshot",
                    SummaryBrush = Frozen(new SolidColorBrush(Color.FromRgb(96, 96, 136))),
                    TokenDelta = 0L,
                    EventDelta = 0
                };
                continue;
            }

            var tokenDelta = currentSnapshot.TotalTokens - previousSnapshot.TotalTokens;
            var eventDelta = currentSnapshot.EventCount - previousSnapshot.EventCount;
            var costDelta = currentSnapshot.CostUsd - previousSnapshot.CostUsd;

            var parts = new List<string>();
            if (tokenDelta != 0L) {
                parts.Add(FormatSignedCompact(tokenDelta, tokenDelta >= 0 ? " tokens" : " tokens"));
            }

            if (eventDelta != 0) {
                parts.Add(FormatSignedCompact(eventDelta, eventDelta >= 0 ? " rollups" : " rollups"));
            }

            if (costDelta != 0m) {
                parts.Add((costDelta >= 0m ? "+" : "-") + "$" + Math.Abs(costDelta).ToString("0.##", CultureInfo.InvariantCulture));
            }

            var brushColor = tokenDelta > 0L || eventDelta > 0 || costDelta > 0m
                ? Color.FromRgb(144, 208, 160)
                : tokenDelta < 0L || eventDelta < 0 || costDelta < 0m
                    ? Color.FromRgb(240, 192, 64)
                    : Color.FromRgb(96, 96, 136);
            var brush = new SolidColorBrush(brushColor);
            brush.Freeze();

            delta[provider.ProviderId] = new ProviderComparisonDeltaInfo {
                SummaryText = parts.Count > 0 ? string.Join(" • ", parts) + " since refresh" : "No change since refresh",
                SummaryBrush = brush,
                TokenDelta = tokenDelta,
                EventDelta = eventDelta
            };
        }

        return delta;
    }

    private void UpdateProviderRefreshHistory(IReadOnlyDictionary<string, ProviderRefreshSnapshot> currentSnapshots) {
        var activeProviderIds = new HashSet<string>(currentSnapshots.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var staleProviderId in _providerRefreshHistory.Keys.Where(providerId => !activeProviderIds.Contains(providerId)).ToList()) {
            _providerRefreshHistory.Remove(staleProviderId);
        }

        foreach (var (providerId, snapshot) in currentSnapshots) {
            if (!_providerRefreshHistory.TryGetValue(providerId, out var history)) {
                history = new Queue<ProviderRefreshSnapshot>();
                _providerRefreshHistory[providerId] = history;
            }

            history.Enqueue(snapshot);
            while (history.Count > RefreshHistoryDepth) {
                history.Dequeue();
            }
        }
    }

    private static IReadOnlyDictionary<string, ProviderComparisonHistoryInfo> BuildProviderComparisonHistory(
        IReadOnlyDictionary<string, Queue<ProviderRefreshSnapshot>> refreshHistory) {
        var historyByProvider = new Dictionary<string, ProviderComparisonHistoryInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (providerId, snapshotQueue) in refreshHistory) {
            var snapshots = snapshotQueue.ToArray();
            if (snapshots.Length <= 1) {
                historyByProvider[providerId] = new ProviderComparisonHistoryInfo {
                    SummaryText = "Trend building...",
                    SummaryBrush = Frozen(new SolidColorBrush(Color.FromRgb(96, 96, 136)))
                };
                continue;
            }

            var eventDeltas = new List<int>(snapshots.Length - 1);
            for (var i = 1; i < snapshots.Length; i++) {
                eventDeltas.Add(snapshots[i].EventCount - snapshots[i - 1].EventCount);
            }

            var recentEventDeltas = eventDeltas.TakeLast(3).ToList();
            var latestEventDelta = recentEventDeltas.LastOrDefault();
            var brushColor = latestEventDelta > 0
                ? Color.FromRgb(144, 208, 160)
                : latestEventDelta < 0
                    ? Color.FromRgb(240, 192, 64)
                    : Color.FromRgb(96, 96, 136);
            var brush = new SolidColorBrush(brushColor);
            brush.Freeze();

            historyByProvider[providerId] = new ProviderComparisonHistoryInfo {
                SummaryText = "Recent adds: " + string.Join(" • ", recentEventDeltas.Select(FormatShortSignedDelta)),
                SummaryBrush = brush
            };
        }

        return historyByProvider;
    }

    private static string FormatSignedCompact(long value, string suffix) {
        var absolute = Math.Abs(value);
        var prefix = value >= 0 ? "+" : "-";
        var compact = absolute switch {
            >= 1_000_000_000L => (absolute / 1_000_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "B",
            >= 1_000_000L => (absolute / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M",
            >= 1_000L => (absolute / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K",
            _ => absolute.ToString("N0", CultureInfo.CurrentCulture)
        };
        return prefix + compact + suffix;
    }

    private static string FormatSignedCompact(int value, string suffix) {
        return FormatSignedCompact((long)value, suffix);
    }

    private static string FormatShortSignedDelta(int value) {
        return value switch {
            > 0 => "+" + value.ToString("N0", CultureInfo.CurrentCulture),
            < 0 => "-" + Math.Abs(value).ToString("N0", CultureInfo.CurrentCulture),
            _ => "0"
        };
    }

    public void Dispose() {
        _refreshTimer.Stop();
        _loadingStatusTimer.Stop();
        _usageDirtyRefreshTimer.Stop();
        GitHub.PropertyChanged -= OnGitHubPropertyChanged;
        _usageChangeWatcher.Changed -= OnUsageChangeDetected;
        _usageChangeWatcher.Dispose();
        UnsubscribeProviders();
        var cts = Interlocked.Exchange(ref _gitHubRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
        cts = Interlocked.Exchange(ref _limitRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private sealed record ProviderRefreshData(string ProviderId, List<UsageEventRecord> Events, List<UsageEventRecord> RawEvents);

    private sealed record ProviderRefreshSnapshot(long TotalTokens, int EventCount, decimal CostUsd) {
        public static ProviderRefreshSnapshot FromEvents(IEnumerable<UsageEventRecord> events) {
            var materialized = events as IList<UsageEventRecord> ?? events.ToList();
            return new ProviderRefreshSnapshot(
                materialized.Sum(static e => e.TotalTokens ?? 0L),
                materialized.Count,
                materialized.Sum(static e => e.CostUsd ?? 0m));
        }
    }

    private sealed record RefreshComputationResult(
        List<UsageEventRecord> AllEvents,
        List<UsageEventRecord> RawEvents,
        List<ProviderRefreshData> ByProvider,
        DateTimeOffset ScannedAtUtc,
        string ScanInfo,
        IReadOnlyList<string> DiscoveredProviderIds,
        IReadOnlyList<SourceRootRecord> SourceRoots,
        UsageTelemetrySnapshotHealth? Health);

    private enum LimitNotificationLevel {
        Warning,
        Exhausted
    }
}

public sealed class TrayNotificationRequestedEventArgs : EventArgs {
    public TrayNotificationRequestedEventArgs(string title, string message, bool isCritical, string? providerId = null) {
        Title = title;
        Message = message;
        IsCritical = isCritical;
        ProviderId = providerId;
    }

    public string Title { get; }
    public string Message { get; }
    public bool IsCritical { get; }
    public string? ProviderId { get; }
}
