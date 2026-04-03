using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.GitHub;

#pragma warning disable CS1591

/// <summary>
/// Combines star-sync, shared stargazers, fork-network overlap, and local-pulse alignment into related repo clusters.
/// </summary>
public static class GitHubRepositoryClusterSummaryBuilder {
    private const double MinimumStarCorrelation = 0.45d;
    private const double MinimumLocalCorrelation = 0.25d;
    private const int MinimumSignals = 2;

    public static GitHubRepositoryClusterSummaryData Build(
        GitHubObservabilitySummaryData? gitHubSummary,
        GitHubLocalActivityCorrelationSummaryData? localActivitySummary = null) {
        if (gitHubSummary is null || gitHubSummary.Repositories.Count < 2) {
            return GitHubRepositoryClusterSummaryData.Empty;
        }

        var maxSharedStargazers = Math.Max(1, gitHubSummary.StargazerAudienceOverlaps
            .Select(static overlap => overlap.SharedStargazerCount)
            .DefaultIfEmpty(0)
            .Max());
        var maxSharedForkOwners = Math.Max(1, gitHubSummary.ForkNetworkOverlaps
            .Select(static overlap => overlap.SharedForkOwnerCount)
            .DefaultIfEmpty(0)
            .Max());
        var positiveLocalByRepository = (localActivitySummary?.RepositoryCorrelations ?? Array.Empty<GitHubLocalActivityRepositoryCorrelationData>())
            .Where(static correlation => correlation.Correlation >= MinimumLocalCorrelation)
            .ToDictionary(
                static correlation => correlation.RepositoryNameWithOwner,
                static correlation => correlation,
                StringComparer.OrdinalIgnoreCase);

        var pairBuilders = new Dictionary<string, ClusterPairBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var correlation in gitHubSummary.StarCorrelations.Where(static correlation => correlation.Correlation >= MinimumStarCorrelation)) {
            var builder = GetOrAddBuilder(pairBuilders, correlation.RepositoryANameWithOwner, correlation.RepositoryBNameWithOwner);
            builder.StarCorrelation = correlation;
        }

        foreach (var overlap in gitHubSummary.StargazerAudienceOverlaps.Where(static overlap => overlap.SharedStargazerCount > 0)) {
            var builder = GetOrAddBuilder(pairBuilders, overlap.RepositoryANameWithOwner, overlap.RepositoryBNameWithOwner);
            builder.StargazerOverlap = overlap;
        }

        foreach (var overlap in gitHubSummary.ForkNetworkOverlaps.Where(static overlap => overlap.SharedForkOwnerCount > 0)) {
            var builder = GetOrAddBuilder(pairBuilders, overlap.RepositoryANameWithOwner, overlap.RepositoryBNameWithOwner);
            builder.ForkOverlap = overlap;
        }

        foreach (var builder in pairBuilders.Values) {
            if (positiveLocalByRepository.TryGetValue(builder.RepositoryANameWithOwner, out var localA)) {
                builder.LocalA = localA;
            }
            if (positiveLocalByRepository.TryGetValue(builder.RepositoryBNameWithOwner, out var localB)) {
                builder.LocalB = localB;
            }
        }

        var clusters = pairBuilders.Values
            .Select(builder => builder.Build(maxSharedStargazers, maxSharedForkOwners))
            .Where(static cluster => cluster is not null)
            .Select(static cluster => cluster!)
            .Where(static cluster => cluster.SupportingSignalCount >= MinimumSignals)
            .OrderByDescending(static cluster => cluster.CompositeScore)
            .ThenByDescending(static cluster => cluster.SupportingSignalCount)
            .ThenByDescending(static cluster => cluster.SharedStargazerCount)
            .ThenByDescending(static cluster => cluster.SharedForkOwnerCount)
            .ThenBy(static cluster => cluster.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static cluster => cluster.RepositoryBNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        return new GitHubRepositoryClusterSummaryData(
            watchedRepositoryCount: gitHubSummary.Repositories.Count,
            locallyAlignedRepositoryCount: positiveLocalByRepository.Count,
            clusters: clusters);
    }

    private static ClusterPairBuilder GetOrAddBuilder(
        IDictionary<string, ClusterPairBuilder> builders,
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner) {
        var ordered = OrderPair(repositoryANameWithOwner, repositoryBNameWithOwner);
        var key = ordered.RepositoryANameWithOwner + "|" + ordered.RepositoryBNameWithOwner;
        if (!builders.TryGetValue(key, out var builder)) {
            builder = new ClusterPairBuilder(ordered.RepositoryANameWithOwner, ordered.RepositoryBNameWithOwner);
            builders[key] = builder;
        }

        return builder;
    }

    private static (string RepositoryANameWithOwner, string RepositoryBNameWithOwner) OrderPair(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner) {
        return string.Compare(repositoryANameWithOwner, repositoryBNameWithOwner, StringComparison.OrdinalIgnoreCase) <= 0
            ? (repositoryANameWithOwner, repositoryBNameWithOwner)
            : (repositoryBNameWithOwner, repositoryANameWithOwner);
    }

    private sealed class ClusterPairBuilder {
        public ClusterPairBuilder(string repositoryANameWithOwner, string repositoryBNameWithOwner) {
            RepositoryANameWithOwner = repositoryANameWithOwner;
            RepositoryBNameWithOwner = repositoryBNameWithOwner;
        }

        public string RepositoryANameWithOwner { get; }
        public string RepositoryBNameWithOwner { get; }
        public GitHubObservedStarCorrelationData? StarCorrelation { get; set; }
        public GitHubObservedStargazerAudienceOverlapData? StargazerOverlap { get; set; }
        public GitHubObservedForkNetworkOverlapData? ForkOverlap { get; set; }
        public GitHubLocalActivityRepositoryCorrelationData? LocalA { get; set; }
        public GitHubLocalActivityRepositoryCorrelationData? LocalB { get; set; }

        public GitHubRepositoryClusterData? Build(int maxSharedStargazers, int maxSharedForkOwners) {
            var starCorrelation = StarCorrelation?.Correlation ?? 0d;
            var stargazerOverlapRatio = StargazerOverlap?.OverlapRatio ?? 0d;
            var sharedStargazerCount = StargazerOverlap?.SharedStargazerCount ?? 0;
            var normalizedStargazerCount = sharedStargazerCount <= 0
                ? 0d
                : Math.Min(1d, sharedStargazerCount / (double)Math.Max(1, maxSharedStargazers));
            var stargazerScore = (stargazerOverlapRatio * 0.6d) + (normalizedStargazerCount * 0.4d);
            var forkOverlapRatio = ForkOverlap?.OverlapRatio ?? 0d;
            var sharedForkOwnerCount = ForkOverlap?.SharedForkOwnerCount ?? 0;
            var normalizedForkOwnerCount = sharedForkOwnerCount <= 0
                ? 0d
                : Math.Min(1d, sharedForkOwnerCount / (double)Math.Max(1, maxSharedForkOwners));
            var forkScore = (forkOverlapRatio * 0.6d) + (normalizedForkOwnerCount * 0.4d);

            var locallyAlignedRepositoryCount = 0;
            double localAlignmentAverage = 0d;
            if (LocalA is not null) {
                locallyAlignedRepositoryCount++;
                localAlignmentAverage += Math.Max(0d, LocalA.Correlation);
            }
            if (LocalB is not null) {
                locallyAlignedRepositoryCount++;
                localAlignmentAverage += Math.Max(0d, LocalB.Correlation);
            }
            if (locallyAlignedRepositoryCount > 0) {
                localAlignmentAverage /= locallyAlignedRepositoryCount;
            }

            var supportingSignalCount = 0;
            if (starCorrelation >= MinimumStarCorrelation) {
                supportingSignalCount++;
            }
            if (sharedStargazerCount > 0) {
                supportingSignalCount++;
            }
            if (sharedForkOwnerCount > 0) {
                supportingSignalCount++;
            }
            if (locallyAlignedRepositoryCount == 2 && localAlignmentAverage >= MinimumLocalCorrelation) {
                supportingSignalCount++;
            }

            if (supportingSignalCount < MinimumSignals) {
                return null;
            }

            var compositeScore = (Math.Max(0d, starCorrelation) * 0.32d)
                                 + (stargazerScore * 0.28d)
                                 + (forkScore * 0.25d)
                                 + (localAlignmentAverage * (locallyAlignedRepositoryCount == 2 ? 0.15d : 0.06d));
            return new GitHubRepositoryClusterData(
                repositoryANameWithOwner: RepositoryANameWithOwner,
                repositoryBNameWithOwner: RepositoryBNameWithOwner,
                compositeScore: Math.Min(1d, compositeScore),
                supportingSignalCount: supportingSignalCount,
                starCorrelation: starCorrelation,
                repositoryARecentStarChange: StarCorrelation?.RepositoryARecentStarChange ?? 0,
                repositoryBRecentStarChange: StarCorrelation?.RepositoryBRecentStarChange ?? 0,
                sharedStargazerCount: sharedStargazerCount,
                stargazerOverlapRatio: stargazerOverlapRatio,
                sharedForkOwnerCount: sharedForkOwnerCount,
                forkOverlapRatio: forkOverlapRatio,
                locallyAlignedRepositoryCount: locallyAlignedRepositoryCount,
                localAlignmentAverageCorrelation: localAlignmentAverage,
                sampleSharedStargazers: StargazerOverlap?.SampleSharedStargazers ?? Array.Empty<string>(),
                sampleSharedForkOwners: ForkOverlap?.SampleSharedForkOwners ?? Array.Empty<string>());
        }
    }
}

public sealed class GitHubRepositoryClusterSummaryData {
    public static GitHubRepositoryClusterSummaryData Empty { get; } = new(
        watchedRepositoryCount: 0,
        locallyAlignedRepositoryCount: 0,
        clusters: Array.Empty<GitHubRepositoryClusterData>());

    public GitHubRepositoryClusterSummaryData(
        int watchedRepositoryCount,
        int locallyAlignedRepositoryCount,
        IReadOnlyList<GitHubRepositoryClusterData> clusters) {
        WatchedRepositoryCount = Math.Max(0, watchedRepositoryCount);
        LocallyAlignedRepositoryCount = Math.Max(0, locallyAlignedRepositoryCount);
        Clusters = clusters ?? Array.Empty<GitHubRepositoryClusterData>();
    }

    public int WatchedRepositoryCount { get; }
    public int LocallyAlignedRepositoryCount { get; }
    public IReadOnlyList<GitHubRepositoryClusterData> Clusters { get; }
    public bool HasData => WatchedRepositoryCount > 0;
    public bool HasSignals => Clusters.Count > 0;
    public GitHubRepositoryClusterData? StrongestCluster => Clusters
        .OrderByDescending(static cluster => cluster.CompositeScore)
        .ThenByDescending(static cluster => cluster.SupportingSignalCount)
        .ThenByDescending(static cluster => cluster.SharedStargazerCount)
        .ThenByDescending(static cluster => cluster.SharedForkOwnerCount)
        .FirstOrDefault();
}

public sealed class GitHubRepositoryClusterData {
    public GitHubRepositoryClusterData(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner,
        double compositeScore,
        int supportingSignalCount,
        double starCorrelation,
        int repositoryARecentStarChange,
        int repositoryBRecentStarChange,
        int sharedStargazerCount,
        double stargazerOverlapRatio,
        int sharedForkOwnerCount,
        double forkOverlapRatio,
        int locallyAlignedRepositoryCount,
        double localAlignmentAverageCorrelation,
        IReadOnlyList<string>? sampleSharedStargazers,
        IReadOnlyList<string>? sampleSharedForkOwners) {
        RepositoryANameWithOwner = string.IsNullOrWhiteSpace(repositoryANameWithOwner)
            ? throw new ArgumentNullException(nameof(repositoryANameWithOwner))
            : repositoryANameWithOwner.Trim();
        RepositoryBNameWithOwner = string.IsNullOrWhiteSpace(repositoryBNameWithOwner)
            ? throw new ArgumentNullException(nameof(repositoryBNameWithOwner))
            : repositoryBNameWithOwner.Trim();
        CompositeScore = Math.Max(0d, Math.Min(1d, compositeScore));
        SupportingSignalCount = Math.Max(0, supportingSignalCount);
        StarCorrelation = starCorrelation;
        RepositoryARecentStarChange = repositoryARecentStarChange;
        RepositoryBRecentStarChange = repositoryBRecentStarChange;
        SharedStargazerCount = Math.Max(0, sharedStargazerCount);
        StargazerOverlapRatio = Math.Max(0d, Math.Min(1d, stargazerOverlapRatio));
        SharedForkOwnerCount = Math.Max(0, sharedForkOwnerCount);
        ForkOverlapRatio = Math.Max(0d, Math.Min(1d, forkOverlapRatio));
        LocallyAlignedRepositoryCount = Math.Max(0, locallyAlignedRepositoryCount);
        LocalAlignmentAverageCorrelation = Math.Max(0d, Math.Min(1d, localAlignmentAverageCorrelation));
        SampleSharedStargazers = sampleSharedStargazers ?? Array.Empty<string>();
        SampleSharedForkOwners = sampleSharedForkOwners ?? Array.Empty<string>();
    }

    public string RepositoryANameWithOwner { get; }
    public string RepositoryBNameWithOwner { get; }
    public double CompositeScore { get; }
    public int SupportingSignalCount { get; }
    public double StarCorrelation { get; }
    public int RepositoryARecentStarChange { get; }
    public int RepositoryBRecentStarChange { get; }
    public int SharedStargazerCount { get; }
    public double StargazerOverlapRatio { get; }
    public int SharedForkOwnerCount { get; }
    public double ForkOverlapRatio { get; }
    public int LocallyAlignedRepositoryCount { get; }
    public double LocalAlignmentAverageCorrelation { get; }
    public IReadOnlyList<string> SampleSharedStargazers { get; }
    public IReadOnlyList<string> SampleSharedForkOwners { get; }
}
