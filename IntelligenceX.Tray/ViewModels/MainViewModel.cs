using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable {
    private const int DefaultRefreshIntervalSeconds = 120;
    private const int RefreshHistoryDepth = 4;
    private const double LimitWarningThresholdPercent = 90d;
    private const double LimitExhaustedThresholdPercent = 100d;

    private readonly UsageTelemetrySnapshotService _usageService;
    private readonly ProviderLimitSnapshotService _limitService;
    private readonly GitHubService _gitHubService;
    private readonly TrayPreferencesStore _preferencesStore;
    private readonly TrayPreferences _preferences;
    private readonly DispatcherTimer _refreshTimer;
    private readonly HashSet<string> _activeLimitNotificationKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _favoriteProviderIds;
    private Dictionary<string, ProviderRefreshSnapshot> _previousProviderSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<ProviderRefreshSnapshot>> _providerRefreshHistory = new(StringComparer.OrdinalIgnoreCase);
    private ProviderViewModel? _selectedProvider;
    private bool _isLoading;
    private string _statusText = "Initializing...";
    private DateTimeOffset _lastRefreshed;
    private int _gitHubRefreshVersion;
    private CancellationTokenSource? _gitHubRefreshCts;
    private int _autoRefreshIntervalSeconds;
    private bool _notificationsEnabled;
    private bool _hasCompletedInitialRefresh;

    public MainViewModel(
        UsageTelemetrySnapshotService usageService,
        ProviderLimitSnapshotService limitService,
        GitHubService gitHubService,
        TrayPreferencesStore preferencesStore) {
        _usageService = usageService;
        _limitService = limitService;
        _gitHubService = gitHubService;
        _preferencesStore = preferencesStore;
        _preferences = _preferencesStore.Load();
        _autoRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(_preferences.AutoRefreshIntervalSeconds);
        _notificationsEnabled = _preferences.NotificationsEnabled;
        _favoriteProviderIds = new HashSet<string>(
            (_preferences.FavoriteProviderIds ?? [])
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId)),
            StringComparer.OrdinalIgnoreCase);

        GitHub = new GitHubViewModel {
            UsernameInput = _preferences.GitHubUsername ?? string.Empty
        };
        GitHub.PropertyChanged += OnGitHubPropertyChanged;

        RefreshCommand = new RelayCommand(RefreshAsync);
        RefreshGitHubCommand = new RelayCommand(RefreshGitHubCurrentAsync);
        OpenOpenAiCacheCommand = new RelayCommand(OpenOpenAiCacheAsync);
        ToggleSelectedProviderFavoriteCommand = new RelayCommand(ToggleSelectedProviderFavoriteAsync, () => CanToggleFavoriteSelectedProvider);

        _refreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _autoRefreshIntervalSeconds <= 0 ? DefaultRefreshIntervalSeconds : _autoRefreshIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public event EventHandler<TrayNotificationRequestedEventArgs>? NotificationRequested;

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
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusText {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

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

    public int AutoRefreshIntervalSeconds {
        get => _autoRefreshIntervalSeconds;
        private set {
            if (SetProperty(ref _autoRefreshIntervalSeconds, value)) {
                OnPropertyChanged(nameof(RefreshModeLabel));
            }
        }
    }

    public bool NotificationsEnabled {
        get => _notificationsEnabled;
        private set => SetProperty(ref _notificationsEnabled, value);
    }

    public string RefreshModeLabel => AutoRefreshIntervalSeconds <= 0
        ? "Manual refresh"
        : "Auto " + FormatRefreshInterval(AutoRefreshIntervalSeconds);

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshGitHubCommand { get; }
    public RelayCommand OpenOpenAiCacheCommand { get; }
    public RelayCommand ToggleSelectedProviderFavoriteCommand { get; }

    public async Task InitializeAsync() {
        await RefreshAsync();
        ConfigureRefreshTimer();
    }

    public async Task RefreshAsync() {
        if (IsLoading) {
            return;
        }

        IsLoading = true;
        StatusText = "Scanning providers...";

        try {
            var preferredSelection = SelectedProvider?.ProviderId ?? _preferences.SelectedProviderId;
            var refreshData = await Task.Run(async () => {
                var snapshot = await _usageService.ScanAsync();
                var events = snapshot.Events;

                var byProvider = events
                    .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .OrderBy(g => ProviderMetadata.Resolve(g.Key).SortOrder)
                    .ToList();
                var limitSnapshots = await _limitService.FetchAsync(byProvider.Select(group => group.Key)).ConfigureAwait(false);

                var info = $"{events.Count} events, {byProvider.Count} providers";
                if (snapshot.ScanDurationMs > 0) {
                    info += $" ({snapshot.ScanDurationMs / 1000.0:F1}s)";
                }

                if (snapshot.Errors.Count > 0) {
                    info += $" [{snapshot.Errors.Count} errors]";
                }

                return new RefreshComputationResult(
                    events.ToList(),
                    byProvider.Select(group => new ProviderRefreshData(group.Key, group.ToList())).ToList(),
                    limitSnapshots,
                    snapshot.ScannedAtUtc,
                    info);
            });
            var providerDelta = BuildProviderComparisonDelta(refreshData.ByProvider, _previousProviderSnapshots);
            var currentProviderSnapshots = refreshData.ByProvider.ToDictionary(
                static group => group.ProviderId,
                static group => ProviderRefreshSnapshot.FromEvents(group.Events),
                StringComparer.OrdinalIgnoreCase);
            UpdateProviderRefreshHistory(currentProviderSnapshots);
            var providerHistory = BuildProviderComparisonHistory(_providerRefreshHistory);

            var newProviders = new List<ProviderViewModel>();
            if (refreshData.AllEvents.Count > 0) {
                var allVm = BuildProviderViewModel("__all__", refreshData.AllEvents);
                allVm.DisplayName = "All";
                allVm.ShortName = "All";
                allVm.IconKey = "IconIx";
                allVm.SortOrder = -1;
                allVm.AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(155, 233, 168)));
                allVm.InputColor = Color.FromRgb(155, 233, 168);
                allVm.OutputColor = Color.FromRgb(64, 196, 99);
                allVm.LastUpdated = refreshData.ScannedAtUtc;
                allVm.IsFavorite = false;
                allVm.ApplyRefreshDelta(
                    providerDelta.Values.Where(static delta => delta.TokenDelta > 0L).Sum(static delta => delta.TokenDelta),
                    providerDelta.Values.Where(static delta => delta.EventDelta > 0).Sum(static delta => delta.EventDelta));
                allVm.ApplyExplorerPreferences(GetProviderExplorerPreferences(allVm.ProviderId));
                newProviders.Add(allVm);
            }

            foreach (var group in refreshData.ByProvider) {
                var vm = BuildProviderViewModel(group.ProviderId, group.Events);
                vm.LastUpdated = refreshData.ScannedAtUtc;
                vm.IsFavorite = IsFavoriteProvider(group.ProviderId);
                if (providerDelta.TryGetValue(group.ProviderId, out var deltaInfo)) {
                    vm.ApplyRefreshDelta(deltaInfo.TokenDelta, deltaInfo.EventDelta);
                } else {
                    vm.ApplyRefreshDelta(0L, 0);
                }

                if (refreshData.LimitSnapshots.TryGetValue(group.ProviderId, out var limitSnapshot)) {
                    vm.ApplyLimitSnapshot(limitSnapshot);
                }

                vm.ApplyExplorerPreferences(GetProviderExplorerPreferences(vm.ProviderId));
                newProviders.Add(vm);
            }

            if (newProviders.FirstOrDefault(provider => string.Equals(provider.ProviderId, "__all__", StringComparison.Ordinal)) is { } allProvider) {
                allProvider.SetProviderComparisonHealth(BuildProviderComparisonHealth(newProviders));
                allProvider.SetProviderComparisonDelta(providerDelta);
                allProvider.SetProviderComparisonHistory(providerHistory);
                allProvider.SetProviderComparisonFavorites(_favoriteProviderIds);
            }

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
            newProviders.Add(gitHubProvider);

            var orderedProviders = OrderProviders(newProviders);
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
            LastRefreshed = refreshData.ScannedAtUtc.ToLocalTime();
            StatusText = refreshData.ScanInfo;

            EvaluateLimitNotifications(refreshData.LimitSnapshots);
            _previousProviderSnapshots = new Dictionary<string, ProviderRefreshSnapshot>(currentProviderSnapshots, StringComparer.OrdinalIgnoreCase);

            var ghLogin = GitHub.UsernameInput;
            _ = RefreshGitHubAsync(ghLogin);
            _hasCompletedInitialRefresh = true;
        } catch (Exception ex) {
            StatusText = $"Error: {ex.Message}";
        } finally {
            IsLoading = false;
        }
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
        var hasToken = !string.IsNullOrWhiteSpace(
            IntelligenceX.Telemetry.GitHub.GitHubDashboardService.ResolveTokenFromEnvironment());
        var effectiveLogin = string.IsNullOrWhiteSpace(ghLogin) ? null : ghLogin.Trim();

        await dispatcher.InvokeAsync(() => {
            if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                return;
            }

            if (!hasToken) {
                GitHub.ClearData();
                if (effectiveLogin is not null) {
                    GitHub.UsernameInput = effectiveLogin;
                }
            }

            GitHub.HasToken = hasToken;
            GitHub.IsLoading = hasToken || !string.IsNullOrWhiteSpace(effectiveLogin);
            GitHub.ErrorMessage = string.Empty;
        });

        if (!hasToken && string.IsNullOrWhiteSpace(effectiveLogin)) {
            await dispatcher.InvokeAsync(() => {
                if (!IsLatestGitHubRefresh(currentVersion)) {
                    return;
                }

                GitHub.IsLoading = false;
            });
            Interlocked.CompareExchange(ref _gitHubRefreshCts, null, refreshCts);
            return;
        }

        try {
            var ghData = await _gitHubService.FetchAsync(effectiveLogin, cancellationToken).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                if (ghData is not null) {
                    GitHub.Apply(ghData);
                    StatusText = "GitHub loaded for " + ghData.Login + ".";
                    return;
                }

                GitHub.ClearData();
                if (effectiveLogin is not null) {
                    GitHub.UsernameInput = effectiveLogin;
                }

                if (!hasToken && !string.IsNullOrWhiteSpace(effectiveLogin)) {
                    GitHub.ErrorMessage = $"No public GitHub data was returned for '{effectiveLogin}'.";
                    StatusText = GitHub.ErrorMessage;
                }
            });
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // A newer refresh superseded this one.
        } catch (Exception ghEx) {
            await dispatcher.InvokeAsync(() => {
                if (!IsCurrentGitHubRefresh(currentVersion, cancellationToken)) {
                    return;
                }

                GitHub.ClearData();
                if (effectiveLogin is not null) {
                    GitHub.UsernameInput = effectiveLogin;
                }

                GitHub.ErrorMessage = ghEx.Message;
                StatusText = "GitHub load failed: " + ghEx.Message;
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

    private void OnGitHubPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (!string.Equals(e.PropertyName, nameof(GitHubViewModel.UsernameInput), StringComparison.Ordinal)) {
            return;
        }

        _preferences.GitHubUsername = GitHub.UsernameInput?.Trim() ?? string.Empty;
        SavePreferences();
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

    private void ConfigureRefreshTimer() {
        if (AutoRefreshIntervalSeconds <= 0) {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromSeconds(AutoRefreshIntervalSeconds);
        _refreshTimer.Start();
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

    private void RefreshProviderSelectionState() {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(IsGitHubTabSelected));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ShowUsageContent));
        OnPropertyChanged(nameof(ShowGitHubContent));
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

    private static ProviderViewModel BuildProviderViewModel(string providerId, List<UsageEventRecord> events) {
        var vm = new ProviderViewModel();
        var info = ProviderMetadata.Resolve(providerId);
        vm.ApplyProviderInfo(info);
        vm.ApplyUsageEvents(events);
        return vm;
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
                parts.Add(FormatSignedCompact(eventDelta, eventDelta >= 0 ? " events" : " events"));
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
        GitHub.PropertyChanged -= OnGitHubPropertyChanged;
        UnsubscribeProviders();
        var cts = Interlocked.Exchange(ref _gitHubRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private sealed record ProviderRefreshData(string ProviderId, List<UsageEventRecord> Events);

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
        List<ProviderRefreshData> ByProvider,
        IReadOnlyDictionary<string, ProviderLimitSnapshot> LimitSnapshots,
        DateTimeOffset ScannedAtUtc,
        string ScanInfo);

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
