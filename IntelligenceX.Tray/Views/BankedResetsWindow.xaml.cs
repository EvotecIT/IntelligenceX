using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tray.Views;

/// <summary>Account-scoped inventory editor. All timing advice and persistence are shared-core operations.</summary>
public partial class BankedResetsWindow : Window {
    private readonly string _providerId;
    private readonly ProviderLimitAccountSnapshot _account;
    private readonly BankedResetInventoryStore _store;
    private readonly System.Windows.Threading.DispatcherTimer _evidenceTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private IReadOnlyList<BankedResetCredit>? _credits;
    private bool _isClosed;

    public BankedResetsWindow(string providerId, ProviderLimitAccountSnapshot account, BankedResetInventoryStore? store = null) {
        InitializeComponent();
        _providerId = providerId;
        _account = account;
        _store = store ?? new BankedResetInventoryStore();
        AccountText.Text = account.AccountLabel ?? account.AccountId;
        Loaded += async (_, _) => { await RunAsync(null); if (!_isClosed) _evidenceTimer.Start(); };
        _evidenceTimer.Tick += (_, _) => { if (IsVisible && ContentPanel.IsEnabled) RenderEvidence(); };
        Closed += (_, _) => { _isClosed = true; _evidenceTimer.Stop(); };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(null);

    private async void Add_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(_account.AccountId)) return;
        if (!Enum.TryParse<ResetCreditScope>((ScopePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var scope)) return;
        var date = ExpiryPicker.SelectedDate;
        await RunAsync(() => {
            DateTimeOffset? expiry = date.HasValue ? BankedResetExpiry.ResolveLocalDateEnd(date.Value, TimeZoneInfo.Local) : null;
            var credit = new BankedResetCredit(Guid.NewGuid().ToString("N"), _providerId, _account.AccountId,
                scope, ResetCreditEvidence.Manual, DateTimeOffset.UtcNow, expiry);
            _store.Save(credit);
        });
    }

    private async void RecordUsed_Click(object sender, RoutedEventArgs e) {
        if (CreditList.SelectedItem is not CreditRow { Credit: { } credit }) {
            StatusText.Text = "Select an inventory entry first.";
            return;
        }
        if (credit.RecordedUsedAtUtc.HasValue) {
            StatusText.Text = "This entry is already recorded as used.";
            return;
        }
        if (MessageBox.Show(this, "Record this credit as already used? This updates only the local inventory; it does not redeem anything.",
                "IX Tray · Record reset use", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var updated = new BankedResetCredit(credit.Id, credit.ProviderId, credit.AccountId, credit.Scope,
            credit.Evidence, credit.ObservedAtUtc, credit.ExpiresAtUtc, DateTimeOffset.UtcNow);
        await RunAsync(() => _store.Save(updated));
    }

    private async void Remove_Click(object sender, RoutedEventArgs e) {
        if (CreditList.SelectedItem is not CreditRow { Credit: { } credit }) {
            StatusText.Text = "Select an inventory entry first.";
            return;
        }
        if (MessageBox.Show(this, "Remove this local inventory entry? Provider credits are unaffected.",
                "IX Tray · Remove entry", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(() => _store.Remove(credit.ProviderId, credit.AccountId, credit.Id));
    }

    private async Task RunAsync(Action? update) {
        ContentPanel.IsEnabled = false;
        StatusText.Text = "Reading inventory…";
        try {
            _credits = await Task.Run(() => {
                update?.Invoke();
                return _store.Load();
            });
            RenderEvidence();
            StatusText.Text = "Inventory loaded. Account limits are the reading from when this window opened.";
        } catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException
                                        or System.Text.Json.JsonException or ArgumentException or NotSupportedException) {
            // Avoid displaying local paths or raw serialized data from an invalid inventory.
            StatusText.Text = "Inventory could not be read or saved. It may be busy or invalid. Retry; existing data is not replaced with an empty inventory.";
            AdviceText.Text = "Reset inventory unavailable. Do not infer a zero balance.";
            _credits = null;
            CreditList.ItemsSource = null;
        } finally {
            ContentPanel.IsEnabled = true;
        }
    }

    private void RenderEvidence() {
        if (_credits is null) return;
        var now = DateTimeOffset.UtcNow;
        var selectedId = (CreditList.SelectedItem as CreditRow)?.Credit.Id;
        var rows = _credits
            .Where(c => string.Equals(c.ProviderId, _providerId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(c.AccountId, _account.AccountId, StringComparison.Ordinal))
            .OrderBy(c => c.RecordedUsedAtUtc.HasValue)
            .ThenBy(c => c.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
            .Select(c => new CreditRow(c, now)).ToArray();
        CreditList.ItemsSource = rows;
        CreditList.SelectedItem = rows.FirstOrDefault(row => row.Credit.Id == selectedId);
        AdviceText.Text = BankedResetPlanner.Build(_providerId, _account, _credits, now, TimeSpan.FromMinutes(10)).Summary;
    }

    private sealed class CreditRow {
        public CreditRow(BankedResetCredit credit, DateTimeOffset now) {
            Credit = credit;
            var scope = credit.Scope switch {
                ResetCreditScope.Full => "Full reset",
                ResetCreditScope.Weekly => "Weekly reset",
                ResetCreditScope.ShortWindow => "Short-window reset",
                _ => "Reset · scope unknown"
            };
            var state = credit.RecordedUsedAtUtc.HasValue ? "Recorded used"
                : credit.ExpiresAtUtc <= now ? "Expired" : "Recorded available";
            Title = scope + " · " + state;
            var source = credit.Evidence == ResetCreditEvidence.Manual ? "Manual" : "Provider-reported";
            var expiry = credit.ExpiresAtUtc.HasValue
                ? "expires " + credit.ExpiresAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "expiry unknown";
            Detail = source + " · " + expiry + " · observed "
                     + credit.ObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }
        public BankedResetCredit Credit { get; }
        public string Title { get; }
        public string Detail { get; }
    }
}
