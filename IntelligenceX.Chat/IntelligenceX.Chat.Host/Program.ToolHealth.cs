using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static async Task RunToolHealthAsync(ToolRegistry registry, IReadOnlyList<IToolPack> packs, ReplOptions options, string? filterText,
        CancellationToken cancellationToken) {
        if (!TryParseToolHealthFilter(filterText, out var filter, out var filterError, out var showFilterHelp)) {
            if (!string.IsNullOrWhiteSpace(filterError)) {
                Console.WriteLine($"Invalid /toolhealth filter: {filterError}");
            }

            if (showFilterHelp || !string.IsNullOrWhiteSpace(filterError)) {
                WriteToolHealthHelp();
            }
            return;
        }

        var requireExplicitPackInfoRole = options.RequireExplicitRoutingMetadata;
        var packInfoDefinitions = ToolHealthDiagnostics.GetPackInfoDefinitions(registry, requireExplicitPackInfoRole);
        if (packInfoDefinitions.Length == 0) {
            Console.WriteLine("No pack-info role tools are registered in this session.");
            return;
        }

        var packMetadataById = BuildToolHealthPackMetadata(packs);
        var selectedProbeCount = 0;
        var okCount = 0;
        var failCount = 0;

        foreach (var definition in packInfoDefinitions) {
            var resolvedPackId = ResolveToolHealthPackId(definition);
            packMetadataById.TryGetValue(resolvedPackId, out var metadata);

            var effectivePackId = metadata.PackId.Length == 0 ? resolvedPackId : metadata.PackId;
            var effectiveSourceKind = metadata.SourceKind.Length == 0
                ? InferToolHealthSourceKind(sourceKind: null, effectivePackId)
                : metadata.SourceKind;

            if (!ShouldIncludeToolHealthProbe(effectivePackId, effectiveSourceKind, filter)) {
                continue;
            }

            selectedProbeCount++;
        }

        if (selectedProbeCount == 0) {
            Console.WriteLine("No pack probes matched the provided filters.");
            WriteAvailableToolHealthPacks(packMetadataById);
            return;
        }

        var selectedLabel = DescribeToolHealthFilter(filter);
        Console.WriteLine($"Running tool health checks for {selectedProbeCount}/{packInfoDefinitions.Length} pack probes{selectedLabel}...");

        foreach (var definition in packInfoDefinitions) {
            var resolvedPackId = ResolveToolHealthPackId(definition);
            packMetadataById.TryGetValue(resolvedPackId, out var metadata);

            var effectivePackId = metadata.PackId.Length == 0 ? resolvedPackId : metadata.PackId;
            var effectivePackName = metadata.PackName;
            var effectiveSourceKind = metadata.SourceKind.Length == 0
                ? InferToolHealthSourceKind(sourceKind: null, effectivePackId)
                : metadata.SourceKind;

            if (!ShouldIncludeToolHealthProbe(effectivePackId, effectiveSourceKind, filter)) {
                continue;
            }

            var probe = await ToolHealthDiagnostics.ProbeAsync(
                    registry,
                    definition.Name,
                    options.ToolTimeoutSeconds,
                    cancellationToken,
                    requireExplicitPackInfoRole)
                .ConfigureAwait(false);
            var probeScope = FormatProbeScope(effectivePackId, effectivePackName, effectiveSourceKind);
            if (probe.Ok) {
                okCount++;
                Console.WriteLine($"[OK]   {probe.ToolName}{probeScope}");
                continue;
            }

            failCount++;
            Console.WriteLine($"[FAIL] {probe.ToolName}{probeScope}: {probe.ErrorCode} ({probe.Error})");
        }

        Console.WriteLine($"Tool health summary: ok={okCount}, failed={failCount}");
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        var availability = descriptors
            .Select(static descriptor => new ToolPackAvailabilityInfo {
                Id = descriptor.Id ?? string.Empty,
                Name = descriptor.Name ?? string.Empty,
                Description = descriptor.Description,
                Tier = descriptor.Tier,
                IsDangerous = descriptor.IsDangerous,
                SourceKind = descriptor.SourceKind ?? string.Empty,
                Enabled = true
            })
            .ToArray();
        var parityEntries = ToolCapabilityParityInventoryBuilder.Build(registry.GetDefinitions(), availability);
        if (parityEntries.Length > 0) {
            Console.WriteLine("Capability parity summary: " + ToolCapabilityParityInventoryBuilder.FormatSummary(parityEntries));
            var details = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(parityEntries, maxItems: 8);
            foreach (var line in details) {
                Console.WriteLine("  [parity-detail] " + line);
            }
            var attention = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(parityEntries, maxItems: 6);
            foreach (var line in attention) {
                Console.WriteLine("  [parity] " + line);
            }
        }
    }

    private static bool TryParseToolHealthFilter(string? filterText, out ToolHealthFilter filter, out string? error, out bool showHelp) {
        error = null;
        showHelp = false;

        var sourceKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalizedInput = (filterText ?? string.Empty).Trim();
        if (normalizedInput.Length == 0) {
            filter = new ToolHealthFilter(null, null);
            return true;
        }

        var tokens = normalizedInput.Split(new[] { ' ', ',', ';', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens) {
            var value = token.Trim();
            if (value.Length == 0) {
                continue;
            }

            var canonical = CanonicalizeToken(value);
            if (canonical is "help" or "h" or "?") {
                showHelp = true;
                filter = new ToolHealthFilter(null, null);
                return false;
            }

            if (canonical is "all" or "*") {
                continue;
            }

            if (TryParseSourceKindToken(value, out var sourceKind)) {
                sourceKinds.Add(sourceKind);
                continue;
            }

            if (value.StartsWith("source:", StringComparison.OrdinalIgnoreCase)) {
                var sourceValue = value[7..].Trim();
                if (!TryParseSourceKindToken(sourceValue, out sourceKind)) {
                    error = $"unknown source kind '{sourceValue}'.";
                    filter = new ToolHealthFilter(null, null);
                    return false;
                }

                sourceKinds.Add(sourceKind);
                continue;
            }

            var packCandidate = value.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                ? value[5..].Trim()
                : value;
            var normalizedPackId = NormalizeToolHealthPackId(packCandidate);
            if (normalizedPackId.Length == 0) {
                error = $"invalid pack id in token '{value}'.";
                filter = new ToolHealthFilter(null, null);
                return false;
            }

            packIds.Add(normalizedPackId);
        }

        filter = new ToolHealthFilter(
            sourceKinds.Count == 0 ? null : sourceKinds,
            packIds.Count == 0 ? null : packIds);
        return true;
    }

    private static string DescribeToolHealthFilter(ToolHealthFilter filter) {
        var sourceKinds = filter.SourceKinds;
        var packIds = filter.PackIds;
        if ((sourceKinds is null || sourceKinds.Count == 0) && (packIds is null || packIds.Count == 0)) {
            return string.Empty;
        }

        var parts = new List<string>(2);
        if (sourceKinds is { Count: > 0 }) {
            var sourcePart = string.Join(",", sourceKinds.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
            parts.Add($"source={sourcePart}");
        }
        if (packIds is { Count: > 0 }) {
            var packPart = string.Join(",", packIds.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
            parts.Add($"packs={packPart}");
        }

        return $" ({string.Join("; ", parts)})";
    }

    private static bool ShouldIncludeToolHealthProbe(string packId, string sourceKind, ToolHealthFilter filter) {
        if (filter.SourceKinds is { Count: > 0 } sourceKinds && !sourceKinds.Contains(sourceKind)) {
            return false;
        }

        if (filter.PackIds is { Count: > 0 } packIds) {
            if (packId.Length == 0) {
                return false;
            }
            return packIds.Contains(packId);
        }

        return true;
    }

    private static Dictionary<string, ToolHealthPackMetadata> BuildToolHealthPackMetadata(IReadOnlyList<IToolPack> packs) {
        var result = new Dictionary<string, ToolHealthPackMetadata>(StringComparer.OrdinalIgnoreCase);
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        foreach (var descriptor in descriptors) {
            var normalizedPackId = NormalizeToolHealthPackId(descriptor.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            var sourceKind = InferToolHealthSourceKind(descriptor.SourceKind, normalizedPackId);
            var packName = ResolveToolHealthPackDisplayName(normalizedPackId, descriptor.Name);
            result[normalizedPackId] = new ToolHealthPackMetadata(normalizedPackId, packName, sourceKind);
        }

        return result;
    }

    private static void WriteAvailableToolHealthPacks(Dictionary<string, ToolHealthPackMetadata> packMetadataById) {
        if (packMetadataById.Count == 0) {
            Console.WriteLine("No pack metadata was discovered.");
            return;
        }

        Console.WriteLine("Available pack filters:");
        foreach (var metadata in packMetadataById.Values.OrderBy(static value => value.PackId, StringComparer.OrdinalIgnoreCase)) {
            var displayName = string.IsNullOrWhiteSpace(metadata.PackName) ? metadata.PackId : metadata.PackName.Trim();
            Console.WriteLine($"- pack:{metadata.PackId} ({displayName}, source={metadata.SourceKind})");
        }
    }

    private static void WriteToolHealthHelp() {
        Console.WriteLine("Usage: /toolhealth [filters]");
        Console.WriteLine("Filters can be combined with spaces or commas.");
        Console.WriteLine("Source filters: open, closed/private, builtin.");
        Console.WriteLine("Pack filters: pack:<id> where id is canonical runtime metadata.");
        Console.WriteLine("Examples:");
        Console.WriteLine("  /toolhealth");
        Console.WriteLine("  /toolhealth closed");
        Console.WriteLine("  /toolhealth open,pack:<id>");
        Console.WriteLine("  /toolhealth private pack:<id1> pack:<id2>");
    }

    private static string FormatProbeScope(string packId, string? packName, string sourceKind) {
        var normalizedPackId = (packId ?? string.Empty).Trim();
        var normalizedPackName = (packName ?? string.Empty).Trim();
        var normalizedSource = (sourceKind ?? string.Empty).Trim();
        if (normalizedPackId.Length == 0 && normalizedPackName.Length == 0 && normalizedSource.Length == 0) {
            return string.Empty;
        }

        var idAndName = normalizedPackId.Length == 0
            ? normalizedPackName
            : (normalizedPackName.Length == 0 ? normalizedPackId : $"{normalizedPackId}:{normalizedPackName}");

        if (idAndName.Length == 0) {
            return $" [source={normalizedSource}]";
        }

        return normalizedSource.Length == 0
            ? $" [{idAndName}]"
            : $" [{idAndName}, source={normalizedSource}]";
    }

    private static string ResolveToolHealthPackId(ToolDefinition definition) {
        if (ToolHealthDiagnostics.TryResolvePackId(definition, out var packId)) {
            return packId;
        }

        return string.Empty;
    }

    private static bool TryParseSourceKindToken(string token, out string sourceKind) {
        sourceKind = string.Empty;
        var canonical = CanonicalizeToken(token);
        switch (canonical) {
            case "open":
            case "opensource":
            case "public":
                sourceKind = "open_source";
                return true;
            case "closed":
            case "closedsource":
            case "private":
            case "nonopen":
            case "internal":
                sourceKind = "closed_source";
                return true;
            case "builtin":
            case "core":
                sourceKind = "builtin";
                return true;
            default:
                return false;
        }
    }

    private static string CanonicalizeToken(string token) {
        return (token ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
    }

    private static string InferToolHealthSourceKind(string? sourceKind, string descriptorId) {
        return ToolPackBootstrap.NormalizeSourceKind(sourceKind, descriptorId);
    }

    private static string ResolveToolHealthPackDisplayName(string? descriptorId, string? fallbackName) {
        var packId = NormalizeToolHealthPackId(descriptorId);
        return string.IsNullOrWhiteSpace(fallbackName)
            ? packId
            : fallbackName.Trim();
    }

    private static string NormalizeToolHealthPackId(string? descriptorId) {
        return ToolPackBootstrap.NormalizePackId(descriptorId);
    }

    private readonly record struct ToolHealthPackMetadata(string PackId, string PackName, string SourceKind);
    private readonly record struct ToolHealthFilter(HashSet<string>? SourceKinds, HashSet<string>? PackIds);

}
