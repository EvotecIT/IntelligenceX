using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Shared;

/// <summary>
/// Centralizes user-facing domain-intent family presentation so diagnostics and chat use the same wording.
/// </summary>
public static class DomainIntentFamilyPresentationCatalog {
    private const string DomainIntentFamilyAd = "ad_domain";
    private const string DomainIntentFamilyPublic = "public_domain";

    /// <summary>
    /// Resolves a human-friendly presentation for a domain-intent family.
    /// </summary>
    public static DomainIntentFamilyPresentationInfo Resolve(
        string? family,
        IReadOnlyList<string>? representativePackIds = null,
        string? explicitDisplayName = null,
        string? explicitReplyExample = null,
        string? explicitChoiceDescription = null) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        return new DomainIntentFamilyPresentationInfo(
            ResolveDisplayName(normalizedFamily, representativePackIds, explicitDisplayName),
            ResolveReplyExample(normalizedFamily, representativePackIds, explicitReplyExample, explicitDisplayName),
            ResolveChoiceDescription(normalizedFamily, representativePackIds, explicitChoiceDescription));
    }

    /// <summary>
    /// Resolves a human-friendly family label.
    /// </summary>
    public static string ResolveDisplayName(
        string? family,
        IReadOnlyList<string>? representativePackIds = null,
        string? explicitDisplayName = null) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        var normalizedExplicitDisplayName = (explicitDisplayName ?? string.Empty).Trim();
        if (normalizedExplicitDisplayName.Length > 0) {
            return normalizedExplicitDisplayName;
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return "AD domain";
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return "Public domain";
        }

        if (representativePackIds is { Count: 1 }
            && ToolPackIdentityCatalog.TryGetDisplayName(representativePackIds[0], out var displayName)
            && !string.IsNullOrWhiteSpace(displayName)) {
            return displayName;
        }

        return HumanizeFamilyToken(normalizedFamily);
    }

    /// <summary>
    /// Resolves a short example of how a user might naturally select the family.
    /// </summary>
    public static string ResolveReplyExample(
        string? family,
        IReadOnlyList<string>? representativePackIds = null,
        string? explicitReplyExample = null,
        string? explicitDisplayName = null) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        var normalizedExplicitReplyExample = (explicitReplyExample ?? string.Empty).Trim();
        if (normalizedExplicitReplyExample.Length > 0) {
            return normalizedExplicitReplyExample;
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return "AD";
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return "public DNS";
        }

        var displayName = ResolveDisplayName(normalizedFamily, representativePackIds, explicitDisplayName);
        return string.IsNullOrWhiteSpace(displayName)
            ? "that scope"
            : displayName;
    }

    /// <summary>
    /// Resolves a user-facing clarification description for the family.
    /// </summary>
    public static string ResolveChoiceDescription(
        string? family,
        IReadOnlyList<string>? representativePackIds = null,
        string? explicitChoiceDescription = null) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        var normalizedExplicitChoiceDescription = (explicitChoiceDescription ?? string.Empty).Trim();
        if (normalizedExplicitChoiceDescription.Length > 0) {
            return normalizedExplicitChoiceDescription;
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return "AD domain (internal AD checks like replication, LDAP, DC health)";
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return "Public domain (external DNS/mail checks like MX/SPF/DMARC)";
        }

        var humanizedFamily = HumanizeFamilyToken(normalizedFamily);
        var packDisplayNames = (representativePackIds ?? Array.Empty<string>())
            .Select(static packId => ToolPackIdentityCatalog.TryGetDisplayName(packId, out var displayName) ? displayName : string.Empty)
            .Where(static displayName => !string.IsNullOrWhiteSpace(displayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packDisplayNames.Length > 0) {
            return $"{humanizedFamily} scope ({string.Join(", ", packDisplayNames)} tools)";
        }

        return $"{humanizedFamily} scope";
    }

    /// <summary>
    /// Converts a canonical family token into a title-cased label.
    /// </summary>
    public static string HumanizeFamilyToken(string? family) {
        var normalized = (family ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Custom";
        }

        var parts = normalized
            .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(part[0]) + part.Substring(1))
            .Where(static part => part.Length > 0)
            .ToArray();
        return parts.Length == 0
            ? normalized
            : string.Join(" ", parts);
    }
}

/// <summary>
/// User-facing presentation details for a domain-intent family.
/// </summary>
public readonly struct DomainIntentFamilyPresentationInfo {
    public DomainIntentFamilyPresentationInfo(string displayName, string replyExample, string choiceDescription) {
        DisplayName = displayName ?? string.Empty;
        ReplyExample = replyExample ?? string.Empty;
        ChoiceDescription = choiceDescription ?? string.Empty;
    }

    /// <summary>
    /// Friendly family label.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Natural-language reply example.
    /// </summary>
    public string ReplyExample { get; }

    /// <summary>
    /// Clarification description.
    /// </summary>
    public string ChoiceDescription { get; }
}
