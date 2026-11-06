using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>A changed metadata snapshot delivered on a background worker. Marshal to the UI thread in native applications.</summary>
public sealed class AgentWorkSnapshotChangedEventArgs(AgentWorkSnapshot snapshot) : EventArgs {
    /// <summary>The current local snapshot, with the latest independently collected remote state.</summary>
    public AgentWorkSnapshot Snapshot { get; } = snapshot;
}

/// <summary>Watches recent local work with incremental parsing and an independent, slower GitHub refresh.</summary>
/// <remarks>Owns its watchers and workers. Caller-supplied cache and GitHub service remain caller-owned.
/// Use DisposeAsync to await shutdown. No provider clients, authentication or hooks are launched.</remarks>
public sealed partial class AgentWorkWatcher : IDisposable, IAsyncDisposable {
    private SourceRootRecord[] _roots;
    private readonly Func<IReadOnlyList<SourceRootRecord>>? _discoverRoots;
    private readonly Func<string, string, FileSystemWatcher> _createWatcher;
#if NET8_0_OR_GREATER
    private readonly TimeProvider _timeProvider = TimeProvider.System;
#endif
    private readonly AgentWorkOptions _options;
    private readonly IRawArtifactStore _cache;
    private readonly AgentLocalCollection _local;
    private readonly Dictionary<string, (string RootId, string Path)> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<CancellationToken, Task<IReadOnlyList<AgentSessionState>>>? _readMetadata;
    private readonly GitHubDashboardService? _github;
    private readonly string? _login;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentWorkDiagnostics _diagnostics;
    private IReadOnlyList<AgentSessionState> _metadata = Array.Empty<AgentSessionState>();
    private IReadOnlyList<AgentPullRequestState> _pullRequests = Array.Empty<AgentPullRequestState>();
    private DateTimeOffset? _gitHubCheckedAtUtc, _metadataCheckedAtUtc;
    private AgentWorkSnapshot _current = new();
    private AgentWorkSnapshot? _published;
    private Task? _startTask, _cleanup;
    private Task[] _workers = Array.Empty<Task>();
    private bool _disposed;
    private int _reconcileRequested;
    private int _rearmRequested;

    /// <summary>Creates a recent-work watcher. Null roots reuse telemetry discovery; an empty list watches nothing.</summary>
    /// <param name="roots">Local or recovered telemetry roots.</param>
    /// <param name="options">Defaults to a two-day window and an in-memory incremental cache.</param>
    /// <param name="readMetadataAsync">Optional Desktop metadata callback, refreshed independently from local logs.</param>
    /// <param name="github">Optional caller-owned GitHub service; network refreshes never block local collection.</param>
    /// <param name="gitHubLogin">Optional selected GitHub login.</param>
    public AgentWorkWatcher(IEnumerable<SourceRootRecord>? roots = null, AgentWorkOptions? options = null,
        Func<CancellationToken, Task<IReadOnlyList<AgentSessionState>>>? readMetadataAsync = null,
        GitHubDashboardService? github = null, string? gitHubLogin = null)
        : this(roots, options, readMetadataAsync, github, gitHubLogin, LocalAgentWorkStateService.DiscoverRoots,
            static (directory, filter) => new FileSystemWatcher(directory, filter)) { }

    internal AgentWorkWatcher(IEnumerable<SourceRootRecord>? roots, AgentWorkOptions? options,
        Func<CancellationToken, Task<IReadOnlyList<AgentSessionState>>>? readMetadataAsync,
        GitHubDashboardService? github, string? gitHubLogin, Func<IReadOnlyList<SourceRootRecord>> discoverRoots,
        Func<string, string, FileSystemWatcher> createWatcher) {
        _options = options ?? new AgentWorkOptions();
        _options.Validate();
        _discoverRoots = roots is null ? discoverRoots : null;
        _roots = roots?.ToArray() ?? Array.Empty<SourceRootRecord>();
        _createWatcher = createWatcher;
        _diagnostics = new AgentWorkDiagnostics(Signal);
        _cache = _options.Cache ?? new InMemoryRawArtifactStore();
        _local = new AgentLocalCollection(CopyReadOptions(), incremental: true);
        _readMetadata = readMetadataAsync;
        _github = github;
        _login = gitHubLogin;
    }

#if NET8_0_OR_GREATER
    internal AgentWorkWatcher(IEnumerable<SourceRootRecord>? roots, AgentWorkOptions? options,
        Func<CancellationToken, Task<IReadOnlyList<AgentSessionState>>>? readMetadataAsync,
        GitHubDashboardService? github, string? gitHubLogin, Func<IReadOnlyList<SourceRootRecord>> discoverRoots,
        Func<string, string, FileSystemWatcher> createWatcher, TimeProvider timeProvider)
        : this(roots, options, readMetadataAsync, github, gitHubLogin, discoverRoots, createWatcher) { _timeProvider = timeProvider; }
#endif

    /// <summary>Last immutable snapshot. Before StartAsync completes it is empty.</summary>
    public AgentWorkSnapshot CurrentSnapshot => Volatile.Read(ref _current);
    /// <summary>Raised when sessions, attention, remote state or diagnostics change; capture times and scan counters alone do not raise it.</summary>
    public event EventHandler<AgentWorkSnapshotChangedEventArgs>? SnapshotChanged;

    /// <summary>Starts observation and awaits the first local snapshot, without awaiting optional network or Desktop metadata.</summary>
    /// <param name="cancellationToken">Cancels startup; disposal stops the subsequent watcher lifetime.</param>
    public Task StartAsync(CancellationToken cancellationToken = default) {
        lock (_gate) {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentWorkWatcher));
            if (_startTask is not null) throw new InvalidOperationException("The watcher is already started.");
            _startTask = Task.Run(async () => {
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
                try {
                    await CollectAsync(startup.Token, reconcile: true, rearmWatchers: true).ConfigureAwait(false);
                    _stop.Token.ThrowIfCancellationRequested();
                    _workers = new[] { Task.Run(RunLocalAsync), Task.Run(RunReconcileAsync), Task.Run(RunMetadataAsync), Task.Run(RunGitHubAsync) };
                } catch {
                    _stop.Cancel();
                    throw;
                }
            });
            return _startTask;
        }
    }

    private async Task RunLocalAsync() {
        try {
            while (true) {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                // Coalesce a burst of writes without waiting for a quiet writer indefinitely.
                await DelayAsync(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
                while (_signal.Wait(0)) { }
                var request = Interlocked.Exchange(ref _reconcileRequested, 0);
                await CollectAsync(_stop.Token, request != 0, forceMembership: request > 1,
                    rearmWatchers: Interlocked.Exchange(ref _rearmRequested, 0) != 0).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RunReconcileAsync() {
        try {
            while (true) {
                await DelayAsync(_options.ReconcileInterval, _stop.Token).ConfigureAwait(false);
                Interlocked.CompareExchange(ref _reconcileRequested, 1, 0);
                Interlocked.Exchange(ref _rearmRequested, 1);
                Signal();
            }
        } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
#if NET8_0_OR_GREATER
        Task.Delay(delay, _timeProvider, cancellationToken);
#else
        Task.Delay(delay, cancellationToken);
#endif

    private async Task CollectAsync(CancellationToken cancellationToken, bool reconcile, bool forceMembership = false, bool rearmWatchers = false) {
        if (reconcile) RefreshDiscoveredRoots();
        EnsureWatchers(rearmWatchers);
        IReadOnlyList<AgentSessionState> metadata;
        IReadOnlyList<AgentPullRequestState> pullRequests;
        IReadOnlyList<AgentSourceDiagnostic> diagnostics;
        DateTimeOffset? gitHubChecked;
        DateTimeOffset? metadataChecked;
        (string RootId, string Path)[] changed;
        lock (_gate) {
            metadata = _metadata;
            pullRequests = _pullRequests;
            diagnostics = _diagnostics.Snapshot;
            gitHubChecked = _gitHubCheckedAtUtc;
            metadataChecked = _metadataCheckedAtUtc;
            changed = _changedFiles.Values.ToArray();
            _changedFiles.Clear();
        }
        var local = await Task.Run(() => {
            _local.ResetStatistics();
            if (reconcile) _local.Reconcile(_roots, null, cancellationToken, forceMembership);
            else {
                var discover = false;
                foreach (var file in changed) discover |= !_local.Update(file.RootId, file.Path, cancellationToken);
                if (discover) _local.Reconcile(_roots, null, cancellationToken);
            }
            return _local.Snapshot(metadata, pullRequests: pullRequests, additionalDiagnostics: diagnostics);
        }, cancellationToken).ConfigureAwait(false);
        var snapshot = local with { GitHubCheckedAtUtc = gitHubChecked, MetadataCheckedAtUtc = metadataChecked };
        Volatile.Write(ref _current, snapshot);
        if (SameContent(snapshot, _published) || _stop.IsCancellationRequested) return;
        _published = snapshot;
        var handlers = SnapshotChanged;
        if (handlers is null) return;
        foreach (EventHandler<AgentWorkSnapshotChangedEventArgs> handler in handlers.GetInvocationList()) {
            try { handler(this, new AgentWorkSnapshotChangedEventArgs(snapshot)); }
            catch (Exception) { SetDiagnostic("watcher", "subscriber-failed"); }
        }
    }

    // Compare typed metadata instead of serializing the entire history to JSON on every tick.
    private static bool SameContent(AgentWorkSnapshot current, AgentWorkSnapshot? previous) {
        if (previous is null || current.Sessions.Count != previous.Sessions.Count || !current.PullRequests.SequenceEqual(previous.PullRequests)
            || !current.NeedsAttention.SequenceEqual(previous.NeedsAttention) || !current.Diagnostics.SequenceEqual(previous.Diagnostics)) return false;
        for (var i = 0; i < current.Sessions.Count; i++) {
            var left = current.Sessions[i];
            var right = previous.Sessions[i];
            if (!left.PullRequests.SequenceEqual(right.PullRequests) || !left.PendingInputs.SequenceEqual(right.PendingInputs)
                || left with { PullRequests = right.PullRequests, PendingInputs = right.PendingInputs } != right) return false;
        }
        return true;
    }

    private AgentWorkOptions CopyReadOptions() => new() { ActiveWithin = _options.ActiveWithin, ActiveTurnWindow = _options.ActiveTurnWindow,
        StaleAfter = _options.StaleAfter, Cache = _cache };

    private void Signal() {
        if (_stop.IsCancellationRequested) return;
        // Most append callbacks arrive while a burst is already queued. Avoid throwing for
        // that normal case; concurrent producers can still race, so retain the guarded release.
        try { if (_signal.CurrentCount == 0) _signal.Release(); }
        catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    private void SetDiagnostic(string provider, string? code, string? sourceRootId = null) => _diagnostics.Set(provider, code, sourceRootId);

    /// <summary>Requests shutdown without blocking the caller. Use DisposeAsync when workers must be fully stopped.</summary>
    public void Dispose() { _ = BeginShutdown(); }

    /// <summary>Cancels outstanding work, releases filesystem handles and awaits all owned workers.</summary>
    public ValueTask DisposeAsync() => new(BeginShutdown());

    private Task BeginShutdown() {
        lock (_gate) {
            if (_cleanup is not null) return _cleanup;
            _disposed = true;
            _stop.Cancel();
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _cleanup = CompleteShutdownAsync();
            return _cleanup;
        }
    }

    private async Task CompleteShutdownAsync() {
        try {
            if (_startTask is not null) await _startTask.ConfigureAwait(false);
            await Task.WhenAll(_workers).ConfigureAwait(false);
        } catch (OperationCanceledException) { }
        finally { _signal.Dispose(); _stop.Dispose(); }
    }
}
