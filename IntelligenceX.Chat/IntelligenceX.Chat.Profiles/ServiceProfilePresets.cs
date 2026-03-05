using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Profiles;

/// <summary>
/// Built-in non-persisted service profile presets that can be activated without a stored SQLite row.
/// </summary>
internal static class ServiceProfilePresets {
    internal const string PluginOnly = "plugin-only";

    private static readonly string[] BuiltInPresetNames = new[] {
        PluginOnly
    };
    private static readonly string[] PluginOnlyAliases = new[] {
        PluginOnly,
        "plugin_only",
        "plugin only"
    };
    private static readonly IReadOnlyList<string> BuiltInPresetNamesView = Array.AsReadOnly(BuiltInPresetNames);
    private static readonly HashSet<string> BuiltInPresetNameSet = new(BuiltInPresetNames, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> BuiltInPresetAliasMap = BuildBuiltInPresetAliasMap();

    internal static IReadOnlyList<string> GetBuiltInPresetNames() {
        return BuiltInPresetNamesView;
    }

    internal static bool TryGetCanonicalName(string? name, out string canonicalName) {
        canonicalName = string.Empty;

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return false;
        }

        if (BuiltInPresetAliasMap.TryGetValue(trimmed, out var directCanonicalName)) {
            canonicalName = directCanonicalName;
            return true;
        }

        var normalized = NormalizePresetName(trimmed);
        if (normalized.Length == 0
            || !BuiltInPresetAliasMap.TryGetValue(normalized, out var normalizedCanonicalName)) {
            return false;
        }

        canonicalName = normalizedCanonicalName;
        return true;
    }

    internal static bool TryResolve(string? name, out string canonicalName, out ServiceProfile profile) {
        profile = null!;
        if (!TryGetCanonicalName(name, out canonicalName)) {
            return false;
        }

        profile = new ServiceProfile {
            EnableBuiltInPackLoading = false,
            EnableDefaultPluginPaths = true,
            PowerShellAllowWrite = false,
            RequireExplicitRoutingMetadata = true
        };
        return true;
    }

    internal static IReadOnlyList<string> GetStoredProfileLookupCandidates(string? requestedName) {
        var trimmed = (requestedName ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return Array.Empty<string>();
        }

        if (!TryGetCanonicalName(trimmed, out var canonicalName)
            || string.Equals(trimmed, canonicalName, StringComparison.Ordinal)) {
            return new[] { trimmed };
        }

        return new[] { trimmed, canonicalName };
    }

    internal static bool TryResolveStoredOrBuiltInProfile(
        string? requestedName,
        bool allowStoredProfiles,
        Func<string, ServiceProfile?> tryGetStoredProfile,
        out string resolvedName,
        out ServiceProfile? profile,
        out bool storedProfilesUnavailable) {
        resolvedName = (requestedName ?? string.Empty).Trim();
        profile = null;
        storedProfilesUnavailable = false;

        if (resolvedName.Length == 0) {
            return false;
        }

        if (allowStoredProfiles) {
            foreach (var candidateName in GetStoredProfileLookupCandidates(resolvedName)) {
                var storedProfile = tryGetStoredProfile(candidateName);
                if (storedProfile == null) {
                    continue;
                }

                resolvedName = candidateName;
                profile = storedProfile;
                return true;
            }
        }

        if (TryResolve(resolvedName, out var presetName, out var presetProfile)) {
            resolvedName = presetName;
            profile = presetProfile;
            return true;
        }

        storedProfilesUnavailable = !allowStoredProfiles && !LooksLikeBuiltInPresetReference(resolvedName);
        return false;
    }

    internal static async ValueTask<(bool Success, string ResolvedName, ServiceProfile? Profile, bool StoredProfilesUnavailable)> TryResolveStoredOrBuiltInProfileAsync(
        string? requestedName,
        bool allowStoredProfiles,
        Func<string, CancellationToken, Task<ServiceProfile?>> tryGetStoredProfileAsync,
        CancellationToken cancellationToken) {
        var resolvedName = (requestedName ?? string.Empty).Trim();
        if (resolvedName.Length == 0) {
            return (false, string.Empty, null, false);
        }

        if (allowStoredProfiles) {
            foreach (var candidateName in GetStoredProfileLookupCandidates(resolvedName)) {
                var storedProfile = await tryGetStoredProfileAsync(candidateName, cancellationToken).ConfigureAwait(false);
                if (storedProfile == null) {
                    continue;
                }

                return (true, candidateName, storedProfile, false);
            }
        }

        if (TryResolve(resolvedName, out var presetName, out var presetProfile)) {
            return (true, presetName, presetProfile, false);
        }

        return (false, resolvedName, null, !allowStoredProfiles && !LooksLikeBuiltInPresetReference(resolvedName));
    }

    internal static string[] MergeBuiltInPresetNames(IEnumerable<string>? storedNames) {
        var builtIns = BuiltInPresetNames
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);
        var stored = (storedNames ?? Array.Empty<string>())
            .Select(static name => (name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .Where(static name => !IsBuiltInPresetNameOrAlias(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);

        return builtIns.Concat(stored).ToArray();
    }

    private static string NormalizePresetName(string? name) {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        var chars = new char[trimmed.Length];
        var length = 0;
        var previousWasDash = false;
        for (var i = 0; i < trimmed.Length; i++) {
            var ch = char.ToLowerInvariant(trimmed[i]);
            if (ch is '_' or ' ' or '\t') {
                ch = '-';
            }

            if (ch == '-') {
                if (previousWasDash) {
                    continue;
                }

                previousWasDash = true;
            } else {
                previousWasDash = false;
            }

            chars[length++] = ch;
        }

        return length == 0
            ? string.Empty
            : new string(chars, 0, length).Trim('-');
    }

    private static bool IsBuiltInPresetNameOrAlias(string name) {
        return TryGetCanonicalName(name, out _);
    }

    private static bool LooksLikeBuiltInPresetReference(string name) {
        if (IsBuiltInPresetNameOrAlias(name)) {
            return true;
        }

        var compactName = CompactPresetReference(name);
        if (compactName.Length == 0) {
            return false;
        }

        foreach (var builtInPresetName in BuiltInPresetNames) {
            var compactBuiltIn = CompactPresetReference(builtInPresetName);
            if (string.Equals(compactName, compactBuiltIn, StringComparison.Ordinal)
                || AreWithinSingleEdit(compactName, compactBuiltIn)) {
                return true;
            }
        }

        return false;
    }

    private static string CompactPresetReference(string? name) {
        var normalized = NormalizePresetName(name);
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var chars = new char[normalized.Length];
        var length = 0;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch == '-') {
                continue;
            }

            chars[length++] = ch;
        }

        return length == 0 ? string.Empty : new string(chars, 0, length);
    }

    private static bool AreWithinSingleEdit(string left, string right) {
        if (string.Equals(left, right, StringComparison.Ordinal)) {
            return true;
        }

        var lengthDelta = Math.Abs(left.Length - right.Length);
        if (lengthDelta > 1) {
            return false;
        }

        var leftIndex = 0;
        var rightIndex = 0;
        var edits = 0;
        while (leftIndex < left.Length && rightIndex < right.Length) {
            if (left[leftIndex] == right[rightIndex]) {
                leftIndex++;
                rightIndex++;
                continue;
            }

            edits++;
            if (edits > 1) {
                return false;
            }

            if (left.Length == right.Length) {
                leftIndex++;
                rightIndex++;
            } else if (left.Length > right.Length) {
                leftIndex++;
            } else {
                rightIndex++;
            }
        }

        if (leftIndex < left.Length || rightIndex < right.Length) {
            edits++;
        }

        return edits <= 1;
    }

    private static Dictionary<string, string> BuildBuiltInPresetAliasMap() {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddBuiltInAliases(aliases, PluginOnly, PluginOnlyAliases);
        return aliases;
    }

    private static void AddBuiltInAliases(Dictionary<string, string> aliases, string canonicalName, IEnumerable<string> names) {
        foreach (var name in names) {
            AddBuiltInAlias(aliases, name, canonicalName);
            AddBuiltInAlias(aliases, NormalizePresetName(name), canonicalName);
        }
    }

    private static void AddBuiltInAlias(Dictionary<string, string> aliases, string? name, string canonicalName) {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return;
        }

        aliases[trimmed] = canonicalName;
    }
}
