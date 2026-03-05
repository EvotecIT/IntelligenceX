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
    private static readonly IReadOnlyList<string> BuiltInPresetNamesView = Array.AsReadOnly(BuiltInPresetNames);
    private static readonly HashSet<string> BuiltInPresetNameSet = new(BuiltInPresetNames, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> GetBuiltInPresetNames() {
        return BuiltInPresetNamesView;
    }

    internal static bool TryGetCanonicalName(string? name, out string canonicalName) {
        canonicalName = string.Empty;

        var normalized = NormalizePresetName(name);
        if (normalized.Length == 0) {
            return false;
        }

        if (!string.Equals(normalized, PluginOnly, StringComparison.Ordinal)) {
            return false;
        }

        canonicalName = PluginOnly;
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

        storedProfilesUnavailable = !allowStoredProfiles;
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

        return (false, resolvedName, null, !allowStoredProfiles);
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
        return BuiltInPresetNameSet.Contains(name)
            || TryGetCanonicalName(name, out var canonicalName) && BuiltInPresetNameSet.Contains(canonicalName);
    }
}
