using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

/// <summary>
/// Top-level ViewModel for the tray popup. Manages provider tabs, auto-refresh, and data flow.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable {
    private readonly UsageDataService _usageService;
    private readonly DispatcherTimer _refreshTimer;
    private ProviderViewModel? _selectedProvider;
    private bool _isLoading;
    private string _statusText = "Initializing...";
    private DateTimeOffset _lastRefreshed;

    public MainViewModel(UsageDataService usageService) {
        _usageService = usageService;
        RefreshCommand = new RelayCommand(RefreshAsync);

        _refreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(60)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<ProviderViewModel> Providers { get; } = [];

    public ProviderViewModel? SelectedProvider {
        get => _selectedProvider;
        set => SetProperty(ref _selectedProvider, value);
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

    public RelayCommand RefreshCommand { get; }

    public async Task InitializeAsync() {
        await RefreshAsync();
        _refreshTimer.Start();
    }

    public async Task RefreshAsync() {
        if (IsLoading) {
            return;
        }

        IsLoading = true;
        StatusText = "Scanning...";

        try {
            var snapshot = await Task.Run(() => _usageService.ScanAsync());
            var events = snapshot.Events;

            var today = DateTime.UtcNow.Date;
            var weekAgo = DateTime.UtcNow.AddDays(-7).Date;
            var monthAgo = DateTime.UtcNow.AddDays(-30).Date;

            // Group events by provider
            var byProvider = events
                .GroupBy(e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .OrderBy(g => ProviderMetadata.Resolve(g.Key).SortOrder)
                .ToList();

            // Build provider ViewModels
            var newProviders = new List<ProviderViewModel>();

            // Add "All" provider first
            var allVm = BuildProviderViewModel("__all__", events.ToList(), today, weekAgo, monthAgo);
            allVm.DisplayName = "All";
            allVm.SortOrder = -1;
            allVm.AccentBrush = new SolidColorBrush(Color.FromRgb(155, 233, 168));
            allVm.LastUpdated = snapshot.ScannedAtUtc;
            newProviders.Add(allVm);

            foreach (var group in byProvider) {
                var vm = BuildProviderViewModel(group.Key, group.ToList(), today, weekAgo, monthAgo);
                vm.LastUpdated = snapshot.ScannedAtUtc;
                newProviders.Add(vm);
            }

            // Update the collection on the UI thread
            Providers.Clear();
            foreach (var p in newProviders) {
                Providers.Add(p);
            }

            SelectedProvider = Providers.FirstOrDefault();
            LastRefreshed = DateTimeOffset.Now;
            StatusText = $"{events.Count} events from {byProvider.Count} providers";
        } catch (Exception ex) {
            StatusText = $"Scan failed: {ex.Message}";
        } finally {
            IsLoading = false;
        }
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

        // Today
        var todayEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date == today).ToList();
        vm.TodayTotalTokens = todayEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.TodayInputTokens = todayEvents.Sum(e => e.InputTokens ?? 0L);
        vm.TodayOutputTokens = todayEvents.Sum(e => e.OutputTokens ?? 0L);
        vm.TodayCachedTokens = todayEvents.Sum(e => e.CachedInputTokens ?? 0L);
        vm.TodayReasoningTokens = todayEvents.Sum(e => e.ReasoningTokens ?? 0L);
        vm.TodayCostUsd = todayEvents.Sum(e => e.CostUsd ?? 0m);
        vm.TodayEventCount = todayEvents.Count;

        // 7-day rolling
        var weekEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date >= weekAgo).ToList();
        vm.WeeklyTotalTokens = weekEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.WeeklyAvgPerDay = weekEvents.Count > 0 ? vm.WeeklyTotalTokens / 7 : 0;
        vm.WeeklyCostUsd = weekEvents.Sum(e => e.CostUsd ?? 0m);

        // 30-day rolling
        var monthEvents = events.Where(e => e.TimestampUtc.UtcDateTime.Date >= monthAgo).ToList();
        vm.MonthlyTotalTokens = monthEvents.Sum(e => e.TotalTokens ?? 0L);
        vm.MonthlyAvgPerDay = monthEvents.Count > 0 ? vm.MonthlyTotalTokens / 30 : 0;
        vm.MonthlyCostUsd = monthEvents.Sum(e => e.CostUsd ?? 0m);

        // Model breakdown (from 30-day events)
        var modelGroups = monthEvents
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
                BarBrush = new SolidColorBrush(info.OutputColor)
            });
        }

        return vm;
    }

    public void Dispose() {
        _refreshTimer.Stop();
    }
}
