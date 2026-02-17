using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task HandleToolHealthAsync(StreamWriter writer, CheckToolHealthRequest request, CancellationToken cancellationToken) {
        var timeoutSeconds = request.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        if (timeoutSeconds < 0 || timeoutSeconds > 3600) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolTimeoutSeconds must be between 0 and 3600.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var packInfoDefinitions = _registry.GetDefinitions()
            .Where(static def => def.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceFilter = BuildSourceKindFilter(request.SourceKinds);
        var packIdFilter = BuildPackIdFilter(request.PackIds);

        var probes = new List<ToolHealthProbeDto>(packInfoDefinitions.Length);
        var okCount = 0;
        var failedCount = 0;
        foreach (var definition in packInfoDefinitions) {
            var metadata = ResolvePackMetadata(definition);
            if (!ShouldIncludeProbe(metadata.PackId, metadata.SourceKind, sourceFilter, packIdFilter)) {
                continue;
            }

            var probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (probe.Ok) {
                okCount++;
            } else {
                failedCount++;
            }

            probes.Add(MapProbeDto(probe, metadata.PackId, metadata.PackName, metadata.SourceKind));
        }

        await WriteAsync(writer, new ToolHealthMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Probes = probes.ToArray(),
            OkCount = okCount,
            FailedCount = failedCount
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ToolHealthProbeDto MapProbeDto(ToolHealthDiagnostics.ProbeResult probe, string? packId, string? packName, ToolPackSourceKind sourceKind) {
        var normalizedPackId = (packId ?? string.Empty).Trim();
        var normalizedPackName = (packName ?? string.Empty).Trim();
        return new ToolHealthProbeDto {
            ToolName = probe.ToolName,
            PackId = normalizedPackId.Length == 0 ? null : normalizedPackId,
            PackName = normalizedPackName.Length == 0 ? null : normalizedPackName,
            SourceKind = sourceKind,
            Ok = probe.Ok,
            ErrorCode = probe.Ok ? null : probe.ErrorCode,
            Error = probe.Ok ? null : probe.Error,
            DurationMs = probe.DurationMs
        };
    }

    private async Task PrimeStartupToolHealthWarningsAsync(CancellationToken cancellationToken) {
        var packInfoDefinitions = _registry.GetDefinitions()
            .Where(static def => def.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packInfoDefinitions.Length == 0) {
            return;
        }

        var warnings = new List<string>(_startupWarnings);
        var hasNewWarnings = false;

        foreach (var definition in packInfoDefinitions) {
            var metadata = ResolvePackMetadata(definition);
            var timeoutSeconds = ResolveStartupToolHealthTimeoutSeconds(_options.ToolTimeoutSeconds, metadata.SourceKind);
            var probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (!probe.Ok && IsToolTimeoutProbe(probe)) {
                var retryTimeoutSeconds = ResolveStartupToolHealthRetryTimeoutSeconds(timeoutSeconds, metadata.SourceKind);
                if (retryTimeoutSeconds > timeoutSeconds) {
                    probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, retryTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                }
            }
            if (probe.Ok) {
                continue;
            }

            var sourceLabel = ToSourceLabel(metadata.SourceKind);
            var packLabel = metadata.PackId.Length == 0 ? "unknown" : metadata.PackId;
            var warning = $"[tool health][{sourceLabel}][{packLabel}] {probe.ToolName} failed ({NormalizeHealthErrorCode(probe.ErrorCode)}): {NormalizeHealthError(probe.Error)}";
            if (warnings.Any(existing => string.Equals(existing, warning, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            warnings.Add(warning);
            hasNewWarnings = true;
            Console.Error.WriteLine($"[pack warning] {warning}");
        }

        if (hasNewWarnings) {
            _startupWarnings = NormalizeDistinctStrings(warnings, maxItems: 96);
        }
    }

    private static HashSet<ToolPackSourceKind>? BuildSourceKindFilter(IReadOnlyList<ToolPackSourceKind>? sourceKinds) {
        if (sourceKinds is null || sourceKinds.Count == 0) {
            return null;
        }

        var set = new HashSet<ToolPackSourceKind>();
        foreach (var sourceKind in sourceKinds) {
            set.Add(sourceKind);
        }

        return set.Count == 0 ? null : set;
    }

    private HashSet<string>? BuildPackIdFilter(IReadOnlyList<string>? packIds) {
        if (packIds is null || packIds.Count == 0) {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packIds) {
            var normalized = NormalizePackId(packId);
            if (normalized.Length == 0) {
                continue;
            }

            set.Add(normalized);
        }

        return set.Count == 0 ? null : set;
    }

    private static bool ShouldIncludeProbe(string packId, ToolPackSourceKind sourceKind, HashSet<ToolPackSourceKind>? sourceFilter,
        HashSet<string>? packIdFilter) {
        if (sourceFilter is not null && !sourceFilter.Contains(sourceKind)) {
            return false;
        }

        if (packIdFilter is not null) {
            if (packId.Length == 0) {
                return false;
            }

            return packIdFilter.Contains(packId);
        }

        return true;
    }

    private (string PackId, string? PackName, ToolPackSourceKind SourceKind) ResolvePackMetadata(ToolDefinition definition) {
        var packId = string.Empty;
        if (_toolPackIdsByToolName.TryGetValue(definition.Name, out var assignedPackId)) {
            packId = NormalizePackId(assignedPackId);
        }

        _packDisplayNamesById.TryGetValue(packId, out var packName);
        var sourceKind = ToolPackSourceKind.OpenSource;
        if (packId.Length > 0 && _packSourceKindsById.TryGetValue(packId, out var resolved)) {
            sourceKind = resolved;
        }
        return (packId, packName, sourceKind);
    }

    internal static int ResolveStartupToolHealthTimeoutSeconds(int configuredTimeoutSeconds, ToolPackSourceKind sourceKind) {
        if (configuredTimeoutSeconds <= 0) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 8 : 4;
        }

        return sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(configuredTimeoutSeconds, 4, 20)
            : Math.Clamp(configuredTimeoutSeconds, 2, 10);
    }

    internal static int ResolveStartupToolHealthRetryTimeoutSeconds(int initialTimeoutSeconds, ToolPackSourceKind sourceKind) {
        if (initialTimeoutSeconds <= 0) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 12 : 6;
        }

        var doubled = initialTimeoutSeconds * 2;
        return sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(doubled, 10, 30)
            : Math.Clamp(doubled, 4, 12);
    }

    private static bool IsToolTimeoutProbe(ToolHealthDiagnostics.ProbeResult probe) {
        return string.Equals(probe.ErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHealthErrorCode(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? "tool_failed" : normalized;
    }

    private static string NormalizeHealthError(string? value) {
        var normalized = ToolHealthDiagnostics.CompactOneLine(value);
        return normalized.Length == 0 ? "Probe failed." : normalized;
    }

    private static string ToSourceLabel(ToolPackSourceKind sourceKind) {
        return sourceKind switch {
            ToolPackSourceKind.Builtin => "builtin",
            ToolPackSourceKind.ClosedSource => "closed_source",
            ToolPackSourceKind.OpenSource => "open_source",
            _ => "unknown"
        };
    }
}
