using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable {
    private readonly UsageTelemetrySnapshotService _usageService;
    private readonly ProviderLimitSnapshotService _limitService;
    private readonly GitHubService _gitHubService;
    private readonly DispatcherTimer _refreshTimer;
    private ProviderViewModel? _selectedProvider;
    private bool _isLoading;
    private string _statusText = "Initializing...";
    private DateTimeOffset _lastRefreshed;
    private int _gitHubRefreshVersion;
    private CancellationTokenSource? _gitHubRefreshCts;

    public MainViewModel(UsageTelemetrySnapshotService usageService, ProviderLimitSnapshotService limitService, GitHubService gitHubService) {
        _usageService = usageService;
        _limitService = limitService;
        _gitHubService = gitHubService;
        GitHub = new GitHubViewModel();
        RefreshCommand = new RelayCommand(RefreshAsync);

        _refreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(120)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<ProviderViewModel> Providers { get; } = [];
    public GitHubViewModel GitHub { get; }

    public ProviderViewModel? SelectedProvider {
        get => _selectedProvider;
        set {
            if (SetProperty(ref _selectedProvider, value)) {
                OnPropertyChanged(nameof(HeaderTitle));
                OnPropertyChanged(nameof(IsGitHubTabSelected));
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(ShowUsageContent));
                OnPropertyChanged(nameof(ShowGitHubContent));
            }
        }
    }

    public bool IsGitHubTabSelected => SelectedProvider?.ProviderId == "__github__";

    public bool ShowUsageContent => !IsGitHubTabSelected && HasData;
    public bool ShowGitHubContent => IsGitHubTabSelected;

    public string HeaderTitle {
        get {
            if (IsGitHubTabSelected)
                return "GitHub";
            if (SelectedProvider == null || SelectedProvider.ProviderId == "__all__")
                return "Usage Monitor";
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

    public RelayCommand RefreshCommand { get; }

    public async Task InitializeAsync() {
        await RefreshAsync();
        _refreshTimer.Start();
    }

    public async Task RefreshAsync() {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Scanning providers...";

        try {
            // Do ALL heavy work on background thread: scan + aggregate
            var previousSelection = SelectedProvider?.ProviderId;
            var refreshData = await Task.Run(async () => {
                var snapshot = await _usageService.ScanAsync();
                var events = snapshot.Events;

                var today = DateTime.UtcNow.Date;
                var weekAgo = DateTime.UtcNow.AddDays(-7).Date;
                var monthAgo = DateTime.UtcNow.AddDays(-30).Date;

                var byProvider = events
                    .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .OrderBy(g => ProviderMetadata.Resolve(g.Key).SortOrder)
                    .ToList();
                var limitSnapshots = await _limitService.FetchAsync(byProvider.Select(group => group.Key)).ConfigureAwait(false);

                var info = $"{events.Count} events, {byProvider.Count} providers";
                if (snapshot.ScanDurationMs > 0) info += $" ({snapshot.ScanDurationMs / 1000.0:F1}s)";
                if (snapshot.Errors.Count > 0) info += $" [{snapshot.Errors.Count} errors]";

                return new RefreshComputationResult(
                    events.ToList(),
                    byProvider.Select(group => new ProviderRefreshData(group.Key, group.ToList())).ToList(),
                    limitSnapshots,
                    snapshot.ScannedAtUtc,
                    today,
                    weekAgo,
                    monthAgo,
                    info);
            });

            var newProviders = new List<ProviderViewModel>();
            if (refreshData.AllEvents.Count > 0) {
                var allVm = BuildProviderViewModel("__all__", refreshData.AllEvents, refreshData.Today, refreshData.WeekAgo, refreshData.MonthAgo);
                allVm.DisplayName = "All";
                allVm.ShortName = "All";
                allVm.IconKey = "IconIx";
                allVm.SortOrder = -1;
                allVm.AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(155, 233, 168)));
                allVm.InputColor = Color.FromRgb(155, 233, 168);
                allVm.OutputColor = Color.FromRgb(64, 196, 99);
                allVm.LastUpdated = refreshData.ScannedAtUtc;
                newProviders.Add(allVm);
            }

            foreach (var group in refreshData.ByProvider) {
                var vm = BuildProviderViewModel(group.ProviderId, group.Events, refreshData.Today, refreshData.WeekAgo, refreshData.MonthAgo);
                vm.LastUpdated = refreshData.ScannedAtUtc;
                if (refreshData.LimitSnapshots.TryGetValue(group.ProviderId, out var limitSnapshot)) {
                    vm.ApplyLimitSnapshot(limitSnapshot);
                }
                newProviders.Add(vm);
            }

            newProviders.Add(new ProviderViewModel {
                ProviderId = "__github__",
                DisplayName = "GitHub",
                ShortName = "GitHub",
                IconKey = "IconGitHub",
                SortOrder = 999,
                AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(64, 196, 99))),
                InputColor = Color.FromRgb(155, 233, 168),
                OutputColor = Color.FromRgb(64, 196, 99)
            });

            Providers.Clear();
            foreach (var p in newProviders) {
                p.RefreshIconGeometry();
                Providers.Add(p);
            }

            // Restore previous selection or pick first available
            ProviderViewModel? restored = null;
            if (previousSelection != null) {
                restored = Providers.FirstOrDefault(p => p.ProviderId == previousSelection);
            }
            SelectedProvider = restored ?? Providers.FirstOrDefault();
            OnPropertyChanged(nameof(HasData));
            LastRefreshed = DateTimeOffset.Now;
            StatusText = refreshData.ScanInfo;

            // Fetch GitHub data in the background (non-blocking)
            var ghLogin = GitHub.UsernameInput;
            _ = RefreshGitHubAsync(ghLogin);
        } catch (Exception ex) {
            StatusText = $"Error: {ex.Message}";
        } finally {
            IsLoading = false;
        }
    }

    private async Task RefreshGitHubAsync(string? ghLogin) {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
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
                    return;
                }

                GitHub.ClearData();
                if (effectiveLogin is not null) {
                    GitHub.UsernameInput = effectiveLogin;
                }

                if (!hasToken && !string.IsNullOrWhiteSpace(effectiveLogin)) {
                    GitHub.ErrorMessage = $"No public GitHub data was returned for '{effectiveLogin}'.";
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

    private bool IsCurrentGitHubRefresh(int version, CancellationToken cancellationToken) {
        return !cancellationToken.IsCancellationRequested && IsLatestGitHubRefresh(version);
    }

    private bool IsLatestGitHubRefresh(int version) {
        return version == Volatile.Read(ref _gitHubRefreshVersion);
    }

    private static ProviderViewModel BuildProviderViewModel(
        string providerId,
        List<IntelligenceX.Telemetry.Usage.UsageEventRecord> events,
        DateTime today,
        DateTime weekAgo,
        DateTime monthAgo) {
        var vm = new ProviderViewModel();
        var info = ProviderMetadata.Resolve(providerId);
        vm.ApplyProviderInfo(info);

        var todayEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date == today).ToList();
        vm.TodayTotalTokens = todayEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.TodayInputTokens = todayEvents.Sum(e => e.InputTokens ?? 0L);
        vm.TodayOutputTokens = todayEvents.Sum(e => e.OutputTokens ?? 0L);
        vm.TodayCachedTokens = todayEvents.Sum(e => e.CachedInputTokens ?? 0L);
        vm.TodayReasoningTokens = todayEvents.Sum(e => e.ReasoningTokens ?? 0L);
        vm.TodayCostUsd = todayEvents.Sum(e => e.CostUsd ?? 0m);
        vm.TodayEventCount = todayEvents.Count;

        var weekEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date >= weekAgo).ToList();
        vm.WeeklyTotalTokens = weekEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.WeeklyAvgPerDay = weekEvents.Count > 0 ? vm.WeeklyTotalTokens / 7 : 0;
        vm.WeeklyCostUsd = weekEvents.Sum(e => e.CostUsd ?? 0m);

        var monthEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date >= monthAgo).ToList();
        vm.MonthlyTotalTokens = monthEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.MonthlyAvgPerDay = monthEvents.Count > 0 ? vm.MonthlyTotalTokens / 30 : 0;
        vm.MonthlyCostUsd = monthEvents.Sum(e => e.CostUsd ?? 0m);

        // Daily activity bars (last 7 days)
        var barBrush = new SolidColorBrush(info.OutputColor);
        barBrush.Freeze();
        var todayBrush = new SolidColorBrush(info.InputColor);
        todayBrush.Freeze();
        var dailyTotals = new List<(DateTime Day, long Tokens)>();
        for (int i = 6; i >= 0; i--) {
            var day = today.AddDays(-i);
            var tokens = events.Where(e => e.TimestampUtc.UtcDateTime.Date == day).Sum(e => e.TotalTokens ?? 0L);
            dailyTotals.Add((day, tokens));
        }
        var maxDaily = dailyTotals.Max(d => d.Tokens);
        vm.DailyBars.Clear();
        foreach (var (day, tokens) in dailyTotals) {
            vm.DailyBars.Add(new DailyBarViewModel {
                DayUtc = day,
                DayLabel = day == today ? "Today" : day.ToString("ddd"),
                TotalTokens = tokens,
                BarHeight = maxDaily > 0 ? Math.Max(2, 48.0 * tokens / maxDaily) : 2,
                BarBrush = day == today ? todayBrush : barBrush
            });
        }

        // Model breakdown from all events (not just 30-day, since quick-scan merges by day)
        var modelGroups = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Model))
            .GroupBy(e => e.Model!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Model = g.Key, Total = g.Sum(e => e.TotalTokens ?? 0L) })
            .Where(g => g.Total > 0)
            .OrderByDescending(g => g.Total)
            .Take(8)
            .ToList();

        var maxModelTokens = modelGroups.Count > 0 ? modelGroups.Max(g => g.Total) : 1L;
        vm.ModelBreakdown.Clear();
        foreach (var mg in modelGroups) {
            vm.ModelBreakdown.Add(new ModelUsageViewModel {
                ModelName = mg.Model,
                TotalTokens = mg.Total,
                Proportion = (double)mg.Total / maxModelTokens,
                BarBrush = Frozen(new SolidColorBrush(info.OutputColor))
            });
        }

        return vm;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush) { brush.Freeze(); return brush; }

    private sealed record ProviderRefreshData(string ProviderId, List<IntelligenceX.Telemetry.Usage.UsageEventRecord> Events);

    private sealed record RefreshComputationResult(
        List<IntelligenceX.Telemetry.Usage.UsageEventRecord> AllEvents,
        List<ProviderRefreshData> ByProvider,
        IReadOnlyDictionary<string, ProviderLimitSnapshot> LimitSnapshots,
        DateTimeOffset ScannedAtUtc,
        DateTime Today,
        DateTime WeekAgo,
        DateTime MonthAgo,
        string ScanInfo);

    public void Dispose() {
        _refreshTimer.Stop();
        var cts = Interlocked.Exchange(ref _gitHubRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
