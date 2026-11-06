using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Tray.ViewModels;

/// <summary>Read-only presentation of the central Ledger projection.</summary>
public sealed class LedgerViewModel : ViewModelBase, IDisposable {
    private readonly CancellationTokenSource _life = new();
    private string _configuration;
    private string _status = "Reading shared work…";
    private string _coverage = "";
    private bool _busy;
    private bool _closed;
    private LedgerRow? _selected;

    public LedgerViewModel(string configuration) {
        _configuration = configuration;
        RefreshCommand = new RelayCommand(RefreshAsync, () => !_busy);
    }

    public ObservableCollection<LedgerRow> Items { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Coverage { get => _coverage; private set => SetProperty(ref _coverage, value); }
    public LedgerRow? Selected {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(Details)); }
    }
    public string Details => Selected is { } row
        ? row.Label + Environment.NewLine + row.Verdict + " · " + row.Item.Provider + " · " + row.Item.MachineId +
            Environment.NewLine + row.Item.ReasonCode + " · since " + row.Since +
            Environment.NewLine + "Next actor: " + (row.Item.NextActor ?? "unknown") +
            Environment.NewLine + (row.Item.Availability is { } availability
                ? "Availability: " + availability.State + " · archived " + (availability.Archived?.ToString().ToLowerInvariant() ?? "unknown") +
                    " · archive observed " + (availability.ArchiveObservedAtUtc?.ToLocalTime().ToString("g") ?? "unknown") +
                    " · usage limited " + availability.UsageLimited.ToString().ToLowerInvariant() +
                    " · application " + availability.ApplicationState
                : "Availability: unknown") +
            Environment.NewLine + "Missing facts: " + string.Join(", ", row.Item.MissingFacts) +
            Environment.NewLine + "Coverage: " + string.Join(", ", row.Item.Coverage.Concat(row.Item.Availability?.Coverage ?? [])) +
            Environment.NewLine + string.Join(Environment.NewLine, row.Item.Evidence.Select(evidence =>
                evidence.Source + " · " + evidence.ObservedAtUtc.ToLocalTime().ToString("g") + " · " + evidence.EventId))
        : "Select an item to inspect its reason and evidence.";

    public async Task ConfigureAsync(string path) {
        _configuration = path;
        await RefreshAsync();
    }

    public async Task RefreshAsync() {
        if (_busy || _closed) return;
        var cancellation = _life.Token;
        var configuration = _configuration;
        _busy = true;
        RefreshCommand.RaiseCanExecuteChanged();
        try {
            using var client = new LedgerReadClient(LedgerClientSettings.Load(configuration));
            var view = await client.WorkAsync(cancellation);
            if (_closed) return;
            if (configuration != _configuration) { _busy = false; await RefreshAsync(); return; }
            var selectedId = Selected?.Item.Id;
            Items.Clear();
            foreach (var item in view.Items) Items.Add(new(item));
            Selected = Items.FirstOrDefault(item => item.Item.Id == selectedId);
            Coverage = string.Join(" · ", view.Machines.Select(machine => machine.Id + " " + machine.State));
            Status = "Observed " + view.AsOfUtc.ToLocalTime().ToString("g") +
                (view.Truncated ? " · list truncated" : "") + " · " + Items.Count + " current items";
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            HttpRequestException or System.Text.Json.JsonException or ArgumentException or TaskCanceledException) {
            Status = Items.Count > 0 ? "Ledger unavailable · showing the previous observation" : "Choose a valid Ledger read settings file to connect.";
        } finally {
            _busy = false;
            RefreshCommand.RaiseCanExecuteChanged();
        }
        if (!_closed && configuration != _configuration) await RefreshAsync();
    }

    public void Dispose() {
        if (_closed) return;
        _closed = true;
        _life.Cancel();
        _life.Dispose();
    }
}

/// <summary>Local display formatting of a service-owned work row.</summary>
public sealed record LedgerRow(LedgerWorkRow Source) {
    public string Label => Source.Label;
    public LedgerItemView Item => Source.Item;
    public string Verdict => Item.Verdict switch {
        "working" => "Working", "waiting" => "Waiting", "needs-you" => "Needs you",
        "stuck" => "Stuck", "done" => "Done", _ => "Unknown"
    };
    public string Since => Item.SinceUtc.ToLocalTime().ToString("g");
}
