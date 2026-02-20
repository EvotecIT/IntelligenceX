using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Normalizes external entity handoff payloads (for example EventLog correlation output) into AD-ready identities.
/// </summary>
public sealed class AdHandoffPrepareTool : ActiveDirectoryToolBase, ITool {
    private const string EventLogEntityHandoffContract = "eventlog_entity_handoff";
    private const string DefaultScopeDiscoveryFallback = "current_domain";
    private const int DefaultMaxIdentities = 50;
    private const int MaxIdentitiesCap = 500;
    private const int MaxScopeSeedDomainControllers = 25;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_handoff_prepare",
        "Normalize entity_handoff candidates (for example EventLog meta.entity_handoff) into AD-ready identities and lookup arguments.",
        ToolSchema.Object(
                ("entity_handoff", ToolSchema.Object(
                        ("contract", ToolSchema.String("Optional handoff contract identifier. Expected: eventlog_entity_handoff.")),
                        ("version", ToolSchema.Integer("Optional handoff contract version.")),
                        ("identity_candidates", ToolSchema.Array(
                            ToolSchema.Object(
                                    ("value", ToolSchema.String("Candidate identity value.")),
                                    ("count", ToolSchema.Integer("Observed candidate count.")),
                                    ("source_fields", ToolSchema.Array(ToolSchema.String(), "Optional source fields where the candidate appeared.")))
                                .Required("value")
                                .NoAdditionalProperties(),
                            "Identity candidates from upstream handoff payload.")),
                        ("computer_candidates", ToolSchema.Array(
                            ToolSchema.Object(
                                    ("value", ToolSchema.String("Candidate computer/host identity value.")),
                                    ("count", ToolSchema.Integer("Observed candidate count.")),
                                    ("source_fields", ToolSchema.Array(ToolSchema.String(), "Optional source fields where the candidate appeared.")))
                                .Required("value")
                                .NoAdditionalProperties(),
                            "Computer candidates from upstream handoff payload.")))
                    .Required("identity_candidates")
                    .NoAdditionalProperties()),
                ("include_computers", ToolSchema.Boolean("When true (default), computer candidates are merged into identities.")),
                ("max_identities", ToolSchema.Integer("Maximum identities returned for AD lookup arguments (capped). Default 50.")),
                ("min_candidate_count", ToolSchema.Integer("Minimum candidate count required for inclusion. Default 1.")))
            .Required("entity_handoff")
            .NoAdditionalProperties());

    private sealed record CandidateInput(
        string Value,
        int Count,
        IReadOnlyList<string> SourceFields);

    private sealed class CandidateAccumulator {
        public CandidateAccumulator(string value) {
            Value = value;
        }

        public string Value { get; }

        public int Count { get; set; }

        public bool FromIdentityCandidates { get; set; }

        public bool FromComputerCandidates { get; set; }

        public HashSet<string> SourceFields { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record NormalizedCandidate(
        string Value,
        int Count,
        bool FromIdentityCandidates,
        bool FromComputerCandidates,
        bool HasNonComputerEvidence,
        IReadOnlyList<string> SourceFields);

    private sealed record AdObjectResolveTarget(
        IReadOnlyList<string> Identities);

    private sealed record AdSearchTarget(
        string Identity);

    private sealed record AdScopeDiscoveryTarget(
        string DiscoveryFallback,
        string? DomainName,
        IReadOnlyList<string> IncludeDomainControllers);

    private sealed record TargetArguments(
        AdObjectResolveTarget AdObjectResolve,
        AdSearchTarget? AdSearch,
        AdScopeDiscoveryTarget AdScopeDiscovery);

    private sealed record PreparedHandoffResult(
        string SourceContract,
        int SourceVersion,
        bool IncludeComputers,
        int MinCandidateCount,
        int CandidatesTotal,
        int CandidatesSelected,
        bool Truncated,
        string? PrimaryIdentity,
        IReadOnlyList<string> Identities,
        IReadOnlyList<NormalizedCandidate> Candidates,
        TargetArguments TargetArguments);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdHandoffPrepareTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdHandoffPrepareTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var entityHandoff = arguments?.GetObject("entity_handoff");
        if (entityHandoff is null) {
            return Task.FromResult(Error("invalid_argument", "entity_handoff is required."));
        }

        var sourceContract = ToolArgs.GetOptionalTrimmed(entityHandoff, "contract") ?? EventLogEntityHandoffContract;
        if (!string.Equals(sourceContract, EventLogEntityHandoffContract, StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult(Error(
                "invalid_argument",
                $"entity_handoff.contract ('{sourceContract}') is not supported. Expected: {EventLogEntityHandoffContract}."));
        }

        var sourceVersionRaw = entityHandoff.GetInt64("version");
        var sourceVersion = sourceVersionRaw.HasValue && sourceVersionRaw.Value > 0
            ? (int)Math.Min(sourceVersionRaw.Value, int.MaxValue)
            : 1;

        if (!TryReadCandidates(
                entityHandoff.GetArray("identity_candidates"),
                "entity_handoff.identity_candidates",
                required: true,
                out var identityCandidates,
                out var identityError)) {
            return Task.FromResult(Error("invalid_argument", identityError ?? "entity_handoff.identity_candidates is invalid."));
        }

        if (!TryReadCandidates(
                entityHandoff.GetArray("computer_candidates"),
                "entity_handoff.computer_candidates",
                required: false,
                out var computerCandidates,
                out var computerError)) {
            return Task.FromResult(Error("invalid_argument", computerError ?? "entity_handoff.computer_candidates is invalid."));
        }

        var includeComputers = ToolArgs.GetBoolean(arguments, "include_computers", defaultValue: true);
        var maxIdentities = ToolArgs.GetCappedInt32(arguments, "max_identities", DefaultMaxIdentities, 1, MaxIdentitiesCap);
        var minCandidateCount = ToolArgs.GetCappedInt32(arguments, "min_candidate_count", 1, 1, int.MaxValue);
        var computerCandidateValues = computerCandidates
            .Select(static candidate => candidate.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var merged = MergeCandidates(identityCandidates, computerCandidates, includeComputers);
        var selected = merged
            .Where(candidate => includeComputers || !computerCandidateValues.Contains(candidate.Value))
            .Where(candidate => candidate.Count >= minCandidateCount)
            .OrderByDescending(candidate => !computerCandidateValues.Contains(candidate.Value))
            .ThenByDescending(static candidate => candidate.HasNonComputerEvidence)
            .ThenByDescending(static candidate => candidate.Count)
            .ThenBy(static candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var truncated = selected.Length > maxIdentities;
        if (truncated) {
            selected = selected
                .Take(maxIdentities)
                .ToArray();
        }

        var identities = selected
            .Select(static candidate => candidate.Value)
            .ToArray();
        var primaryIdentity = identities.Length > 0 ? identities[0] : null;
        var scopeDiscoveryTarget = BuildScopeDiscoveryTarget(computerCandidates, minCandidateCount);

        var result = new PreparedHandoffResult(
            SourceContract: EventLogEntityHandoffContract,
            SourceVersion: sourceVersion,
            IncludeComputers: includeComputers,
            MinCandidateCount: minCandidateCount,
            CandidatesTotal: merged.Count,
            CandidatesSelected: selected.Length,
            Truncated: truncated,
            PrimaryIdentity: primaryIdentity,
            Identities: identities,
            Candidates: selected,
            TargetArguments: new TargetArguments(
                AdObjectResolve: new AdObjectResolveTarget(Identities: identities),
                AdSearch: primaryIdentity is null ? null : new AdSearchTarget(Identity: primaryIdentity),
                AdScopeDiscovery: scopeDiscoveryTarget));

        var meta = new JsonObject()
            .Add("max_identities", maxIdentities)
            .Add("include_computers", includeComputers)
            .Add("min_candidate_count", minCandidateCount)
            .Add("candidates_total", merged.Count)
            .Add("candidates_selected", selected.Length)
            .Add("truncated", truncated)
            .Add("scope_seed_domain_controllers", scopeDiscoveryTarget.IncludeDomainControllers.Count);

        var summary = ToolMarkdown.SummaryFacts(
            title: "AD Handoff Prepared",
            facts: new[] {
                ("Identities", identities.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Include computers", includeComputers ? "true" : "false"),
                ("Min candidate count", minCandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Scope seed DCs", scopeDiscoveryTarget.IncludeDomainControllers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Truncated", truncated ? "true" : "false")
            });

        return Task.FromResult(ToolResponse.OkModel(model: result, meta: meta, summaryMarkdown: summary));
    }

    private static IReadOnlyList<NormalizedCandidate> MergeCandidates(
        IReadOnlyList<CandidateInput> identityCandidates,
        IReadOnlyList<CandidateInput> computerCandidates,
        bool includeComputers) {
        var map = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);

        AddCandidateSet(map, identityCandidates, fromIdentityCandidates: true, fromComputerCandidates: false);
        if (includeComputers) {
            AddCandidateSet(map, computerCandidates, fromIdentityCandidates: false, fromComputerCandidates: true);
        }

        return map.Values
            .Select(static entry => new NormalizedCandidate(
                Value: entry.Value,
                Count: entry.Count,
                FromIdentityCandidates: entry.FromIdentityCandidates,
                FromComputerCandidates: entry.FromComputerCandidates,
                HasNonComputerEvidence: HasNonComputerEvidence(entry.SourceFields),
                SourceFields: entry.SourceFields
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static void AddCandidateSet(
        Dictionary<string, CandidateAccumulator> map,
        IReadOnlyList<CandidateInput> candidates,
        bool fromIdentityCandidates,
        bool fromComputerCandidates) {
        for (var i = 0; i < candidates.Count; i++) {
            var candidate = candidates[i];
            if (!map.TryGetValue(candidate.Value, out var accumulator)) {
                accumulator = new CandidateAccumulator(candidate.Value);
                map[candidate.Value] = accumulator;
            }

            accumulator.Count += Math.Max(candidate.Count, 1);
            accumulator.FromIdentityCandidates |= fromIdentityCandidates;
            accumulator.FromComputerCandidates |= fromComputerCandidates;
            for (var j = 0; j < candidate.SourceFields.Count; j++) {
                var sourceField = candidate.SourceFields[j];
                if (!string.IsNullOrWhiteSpace(sourceField)) {
                    accumulator.SourceFields.Add(sourceField.Trim());
                }
            }
        }
    }

    private static bool TryReadCandidates(
        JsonArray? source,
        string argumentPath,
        bool required,
        out IReadOnlyList<CandidateInput> candidates,
        out string? error) {
        error = null;
        var list = new List<CandidateInput>();
        candidates = list;

        if (source is null) {
            if (required) {
                error = $"{argumentPath} is required.";
                return false;
            }

            return true;
        }

        for (var i = 0; i < source.Count; i++) {
            var item = source[i].AsObject();
            if (item is null) {
                error = $"{argumentPath}[{i}] must be an object.";
                return false;
            }

            var value = NormalizeIdentity(item.GetString("value"));
            if (value is null) {
                error = $"{argumentPath}[{i}].value is required.";
                return false;
            }

            var countRaw = item.GetInt64("count");
            var count = countRaw.HasValue && countRaw.Value > 0
                ? (int)Math.Min(countRaw.Value, int.MaxValue)
                : 1;

            var sourceFields = ToolArgs.ReadDistinctStringArray(item.GetArray("source_fields"))
                .Where(static field => !string.IsNullOrWhiteSpace(field))
                .Select(static field => field.Trim())
                .ToArray();

            list.Add(new CandidateInput(
                Value: value,
                Count: count,
                SourceFields: sourceFields));
        }

        return true;
    }

    private static string? NormalizeIdentity(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 1 && normalized[0] == '-'
            ? null
            : normalized;
    }

    private static bool HasNonComputerEvidence(IReadOnlyCollection<string> sourceFields) {
        if (sourceFields.Count == 0) {
            return true;
        }

        foreach (var sourceField in sourceFields) {
            if (!string.Equals(sourceField, "computer", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static AdScopeDiscoveryTarget BuildScopeDiscoveryTarget(
        IReadOnlyList<CandidateInput> computerCandidates,
        int minCandidateCount) {
        var includeDomainControllers = computerCandidates
            .Where(candidate => candidate.Count >= minCandidateCount)
            .Select(static candidate => NormalizeHostOrName(candidate.Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxScopeSeedDomainControllers)
            .ToArray();

        var domainName = includeDomainControllers
            .Select(static value => TryExtractDnsDomainName(value))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        return new AdScopeDiscoveryTarget(
            DiscoveryFallback: DefaultScopeDiscoveryFallback,
            DomainName: domainName,
            IncludeDomainControllers: includeDomainControllers);
    }

    private static string? TryExtractDnsDomainName(string? host) {
        var normalized = NormalizeHostOrName(host);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var firstDot = normalized.IndexOf('.');
        if (firstDot <= 0 || firstDot >= normalized.Length - 1) {
            return null;
        }

        var domain = normalized[(firstDot + 1)..];
        return string.IsNullOrWhiteSpace(domain)
            ? null
            : domain;
    }

    private static string NormalizeHostOrName(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return value.Trim().TrimEnd('.');
    }
}
