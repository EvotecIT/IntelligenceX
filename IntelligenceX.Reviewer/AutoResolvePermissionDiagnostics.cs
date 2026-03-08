using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed class AutoResolvePermissionDiagnostics {
    public static AutoResolvePermissionDiagnostics Empty { get; } =
        new(0, Array.Empty<string>());

    public AutoResolvePermissionDiagnostics(int deniedThreadCount, IReadOnlyList<string> deniedCredentialLabels) {
        DeniedThreadCount = Math.Max(0, deniedThreadCount);
        DeniedCredentialLabels = deniedCredentialLabels ?? Array.Empty<string>();
    }

    public int DeniedThreadCount { get; }
    public IReadOnlyList<string> DeniedCredentialLabels { get; }
    public bool HasFailures => DeniedThreadCount > 0;

    public AutoResolvePermissionDiagnostics Merge(AutoResolvePermissionDiagnostics? other) {
        if (other is null || !other.HasFailures) {
            return this;
        }
        if (!HasFailures) {
            return other;
        }

        var labels = new List<string>(DeniedCredentialLabels.Count + other.DeniedCredentialLabels.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDistinctLabels(labels, seen, DeniedCredentialLabels);
        AddDistinctLabels(labels, seen, other.DeniedCredentialLabels);
        return new AutoResolvePermissionDiagnostics(DeniedThreadCount + other.DeniedThreadCount, labels);
    }

    public static AutoResolvePermissionDiagnostics From(int deniedThreadCount, IEnumerable<string>? deniedCredentialLabels) {
        if (deniedThreadCount <= 0) {
            return Empty;
        }

        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDistinctLabels(labels, seen, deniedCredentialLabels);
        return new AutoResolvePermissionDiagnostics(deniedThreadCount, labels);
    }

    private static void AddDistinctLabels(List<string> destination, HashSet<string> seen, IEnumerable<string>? labels) {
        if (labels is null) {
            return;
        }

        foreach (var label in labels) {
            if (string.IsNullOrWhiteSpace(label) || !seen.Add(label)) {
                continue;
            }
            destination.Add(label);
        }
    }
}
