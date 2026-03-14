using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private readonly record struct DomainIntentSignalLexicon(
        IReadOnlyList<string> AdSignals,
        IReadOnlyList<string> PublicSignals);

    private static DomainIntentSignalLexicon ResolveDomainIntentSignalLexicon(IReadOnlyList<ToolDefinition>? availableDefinitions) {
        var adSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publicSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDomainSignalTokens(adSignals, ToolSelectionMetadata.GetDefaultDomainSignalTokens(DomainIntentFamilyAd));
        AddDomainSignalTokens(publicSignals, ToolSelectionMetadata.GetDefaultDomainSignalTokens(DomainIntentFamilyPublic));

        if (availableDefinitions is not null && availableDefinitions.Count > 0) {
            for (var i = 0; i < availableDefinitions.Count; i++) {
                var definition = availableDefinitions[i];
                if (definition is null) {
                    continue;
                }

                var family = ResolveDomainIntentFamily(definition);
                if (family.Length == 0) {
                    continue;
                }

                var signalSet = string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
                    ? adSignals
                    : string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)
                        ? publicSignals
                        : null;
                if (signalSet is null) {
                    continue;
                }

                var routingSignals = definition.Routing?.DomainSignalTokens;
                if (routingSignals is { Count: > 0 }) {
                    AddDomainSignalTokens(signalSet, routingSignals);
                    continue;
                }

                AddDomainSignalTokens(signalSet, ToolSelectionMetadata.GetDomainSignalTokens(definition));
            }
        }

        return new DomainIntentSignalLexicon(
            AdSignals: ToSortedDomainSignalArray(adSignals),
            PublicSignals: ToSortedDomainSignalArray(publicSignals));
    }

    private static void AddDomainSignalTokens(HashSet<string> destination, IReadOnlyList<string>? tokens) {
        if (destination is null || tokens is null || tokens.Count == 0) {
            return;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var normalized = NormalizeDomainSignalTokenValue(tokens[i] ?? string.Empty);
            if (normalized.Length < 2) {
                continue;
            }

            destination.Add(normalized);
        }
    }

    private static IReadOnlyList<string> ToSortedDomainSignalArray(HashSet<string> signals) {
        if (signals is null || signals.Count == 0) {
            return Array.Empty<string>();
        }

        var list = signals.ToArray();
        Array.Sort(list, StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static bool ContainsAnyDomainSignalToken(string text, IReadOnlyList<string> signals) {
        if (signals is null || signals.Count == 0) {
            return false;
        }

        for (var i = 0; i < signals.Count; i++) {
            var signal = (signals[i] ?? string.Empty).Trim();
            if (signal.Length == 0) {
                continue;
            }

            if (ContainsDomainSignalToken(text, signal)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDomainSignalToken(string text, string token) {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedToken = NormalizeDomainSignalTokenValue(token);
        var compactToken = NormalizeCompactToken(normalizedToken.AsSpan());
        if (normalizedText.Length == 0 || normalizedToken.Length == 0) {
            return false;
        }

        var index = 0;
        while (index < normalizedText.Length) {
            while (index < normalizedText.Length && !IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            if (index >= normalizedText.Length) {
                break;
            }

            var start = index;
            while (index < normalizedText.Length && IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            var length = index - start;
            if (length <= 0) {
                continue;
            }

            var candidate = NormalizeDomainSignalTokenValue(normalizedText.Substring(start, length));
            if (candidate.Length == 0) {
                continue;
            }

            if (string.Equals(candidate, normalizedToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (compactToken.Length > 0
                && string.Equals(NormalizeCompactToken(candidate.AsSpan()), compactToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (MatchesDomainSignalCandidateBySegments(candidate, normalizedToken, compactToken)) {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDomainSignalCandidateBySegments(string candidate, string normalizedToken, string compactToken) {
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return false;
        }

        var segments = normalizedCandidate.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++) {
            var segment = (segments[i] ?? string.Empty).Trim();
            if (segment.Length == 0) {
                continue;
            }

            if (string.Equals(segment, normalizedToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (compactToken.Length > 0
                && string.Equals(NormalizeCompactToken(segment.AsSpan()), compactToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (IsDomainSignalSuffixVariant(segment, normalizedToken)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainSignalSuffixVariant(string candidate, string token) {
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        var normalizedToken = (token ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0 || normalizedToken.Length < 5) {
            return false;
        }

        if (normalizedCandidate.Length <= normalizedToken.Length || normalizedCandidate.Length > normalizedToken.Length + 3) {
            return false;
        }

        if (!normalizedCandidate.StartsWith(normalizedToken, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        for (var i = normalizedToken.Length; i < normalizedCandidate.Length; i++) {
            if (!char.IsLetter(normalizedCandidate[i])) {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsDomainSignalAcronymToken(string text, string acronym) {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedAcronym = (acronym ?? string.Empty).Trim();
        if (normalizedText.Length == 0 || normalizedAcronym.Length == 0) {
            return false;
        }

        var index = 0;
        while (index < normalizedText.Length) {
            while (index < normalizedText.Length && !char.IsLetterOrDigit(normalizedText[index])) {
                index++;
            }

            if (index >= normalizedText.Length) {
                break;
            }

            var start = index;
            while (index < normalizedText.Length && char.IsLetterOrDigit(normalizedText[index])) {
                index++;
            }

            var length = index - start;
            if (length != normalizedAcronym.Length) {
                continue;
            }

            if (string.Compare(normalizedText, start, normalizedAcronym, 0, normalizedAcronym.Length, StringComparison.OrdinalIgnoreCase) != 0) {
                continue;
            }

            var allLettersUpper = true;
            for (var i = 0; i < length; i++) {
                var ch = normalizedText[start + i];
                if (!char.IsLetter(ch)) {
                    continue;
                }

                if (char.IsLower(ch)) {
                    allLettersUpper = false;
                    break;
                }
            }

            if (allLettersUpper) {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainSignalTokenCharacter(char ch) {
        return char.IsLetterOrDigit(ch) || ch is '_' or '-';
    }

    private static string NormalizeDomainSignalTokenValue(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                sb.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
                continue;
            }

            if (ch is '_' or '-') {
                if (!previousWasSeparator && sb.Length > 0) {
                    sb.Append('_');
                    previousWasSeparator = true;
                }
            }
        }

        return sb.ToString().Trim('_');
    }
}
