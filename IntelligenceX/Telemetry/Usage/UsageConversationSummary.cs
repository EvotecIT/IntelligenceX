using System.Globalization;

namespace IntelligenceX.Telemetry.Usage;

internal sealed record UsageConversationSummary(
    string ConversationKey,
    string ProviderId,
    string? ProviderAccountId,
    string? AccountLabel,
    string SessionId,
    string? ConversationTitle,
    string? WorkspacePath,
    string? WorkspaceName,
    string? RepositoryName,
    DateTimeOffset StartedUtc,
    DateTimeOffset LastSeenUtc,
    TimeSpan Duration,
    TimeSpan ActiveDuration,
    int TurnCount,
    int CompactCount,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long TotalTokens,
    decimal CostUsd,
    bool CostUsesEstimatedFallback,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Surfaces);

internal static class UsageConversationSummaryBuilder {
    public static IReadOnlyList<UsageConversationSummary> Build(IEnumerable<UsageEventRecord> events) {
        if (events is null) {
            return Array.Empty<UsageConversationSummary>();
        }

        return events
            .Select(static usageEvent => new {
                Event = usageEvent,
                Key = BuildConversationKey(usageEvent),
                Session = NormalizeOptional(usageEvent.ThreadId) ?? NormalizeOptional(usageEvent.SessionId)
            })
            .Where(static item => item.Key is not null && item.Session is not null)
            .GroupBy(static item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildSummary(group.Key, group.Select(static item => item.Event).ToList()))
            .OrderByDescending(static item => item.TotalTokens)
            .ThenByDescending(static item => item.LastSeenUtc)
            .ThenBy(static item => item.ConversationKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UsageConversationSummary BuildSummary(string key, IReadOnlyList<UsageEventRecord> records) {
        var first = records[0];
        var last = records[0];
        for (var i = 1; i < records.Count; i++) {
            var record = records[i];
            if (record.TimestampUtc < first.TimestampUtc) {
                first = record;
            }

            if (record.TimestampUtc > last.TimestampUtc) {
                last = record;
            }
        }

        var totalDurationMs = records.Sum(static item => item.DurationMs ?? 0L);
        var activeDuration = TimeSpan.FromMilliseconds(Math.Max(0L, totalDurationMs));
        var wallDuration = last.TimestampUtc - first.TimestampUtc;
        var displayDuration = wallDuration > TimeSpan.Zero
            ? wallDuration
            : activeDuration;
        var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(records);
        var accountLabel = records
            .Select(static item => NormalizeAccountLabel(item.AccountLabel, item.ProviderAccountId))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var providerAccountId = records
            .Select(static item => NormalizeOptional(item.ProviderAccountId))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var sessionId = NormalizeOptional(first.ThreadId) ?? NormalizeOptional(first.SessionId) ?? key;
        var conversationTitle = records
            .Select(static item => NormalizeOptional(item.ConversationTitle))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var workspacePath = records
            .Select(static item => NormalizeOptional(item.WorkspacePath))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var repositoryName = records
            .Select(static item => NormalizeOptional(item.RepositoryName))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var turnCount = records
            .Select(static item => NormalizeOptional(item.TurnId) ?? NormalizeOptional(item.ResponseId) ?? item.EventId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new UsageConversationSummary(
            key,
            first.ProviderId,
            providerAccountId,
            accountLabel,
            sessionId,
            conversationTitle,
            workspacePath,
            BuildWorkspaceName(workspacePath),
            repositoryName,
            first.TimestampUtc,
            last.TimestampUtc,
            displayDuration,
            activeDuration,
            turnCount,
            records.Sum(static item => item.CompactCount ?? 0),
            records.Sum(static item => item.InputTokens ?? 0L),
            records.Sum(static item => item.CachedInputTokens ?? 0L),
            records.Sum(static item => item.OutputTokens ?? 0L),
            records.Sum(static item => item.ReasoningTokens ?? 0L),
            records.Sum(static item => item.TotalTokens ?? 0L),
            displayCost.TotalCostUsd,
            displayCost.UsesEstimatedFallback,
            BuildTopLabels(records.Select(static item => NormalizeOptional(item.Model)), 3),
            BuildTopLabels(records.Select(static item => NormalizeSurfaceLabel(item.Surface)), 2));
    }

    private static string? BuildConversationKey(UsageEventRecord usageEvent) {
        var session = NormalizeOptional(usageEvent.ThreadId) ?? NormalizeOptional(usageEvent.SessionId);
        if (string.IsNullOrWhiteSpace(session)) {
            return null;
        }

        return string.Join("|",
            NormalizeOptional(usageEvent.ProviderId) ?? "unknown-provider",
            NormalizeOptional(usageEvent.ProviderAccountId)
            ?? NormalizeOptional(usageEvent.AccountLabel)
            ?? "unknown-account",
            session);
    }

    private static IReadOnlyList<string> BuildTopLabels(IEnumerable<string?> labels, int maxLabels) {
        return labels
            .Select(NormalizeOptional)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new {
                Label = group.First()!,
                Count = group.Count()
            })
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(1, maxLabels))
            .Select(static item => item.Label)
            .ToArray();
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeAccountLabel(string? accountLabel, string? accountId) {
        return NormalizeOptional(accountLabel) ?? NormalizeOptional(accountId);
    }

    private static string? NormalizeSurfaceLabel(string? surface) {
        var normalized = NormalizeOptional(surface);
        if (normalized is null) {
            return null;
        }

        return normalized.Replace('-', ' ').Replace('_', ' ');
    }

    private static string? BuildWorkspaceName(string? workspacePath) {
        var normalized = NormalizeOptional(workspacePath);
        if (normalized is null) {
            return null;
        }

        var trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\');
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return null;
        }

        var separatorIndex = trimmed.LastIndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' });
        var name = separatorIndex >= 0 && separatorIndex + 1 < trimmed.Length
            ? trimmed.Substring(separatorIndex + 1)
            : trimmed;
        return NormalizeOptional(name) ?? trimmed;
    }
}
