using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxHostGeneratedToolCallIdChars = 64;
    private const string HostGeneratedToolCallIdFallbackPrefix = "host";

    private static string BuildHostGeneratedToolCallId(string prefix, string? scope = null) {
        var normalizedPrefix = NormalizeHostGeneratedToolCallIdSegment(prefix);
        if (normalizedPrefix.Length == 0) {
            normalizedPrefix = HostGeneratedToolCallIdFallbackPrefix;
        }

        var normalizedScope = NormalizeHostGeneratedToolCallIdSegment(scope);
        var baseId = normalizedScope.Length == 0
            ? normalizedPrefix
            : normalizedPrefix + "_" + normalizedScope;
        if (baseId.Length == 0) {
            baseId = HostGeneratedToolCallIdFallbackPrefix;
        }

        var maxSuffixLength = Math.Max(8, MaxHostGeneratedToolCallIdChars - baseId.Length - 1);
        if (baseId.Length + 1 + maxSuffixLength > MaxHostGeneratedToolCallIdChars) {
            var maxBaseLength = Math.Max(1, MaxHostGeneratedToolCallIdChars - 1 - 8);
            if (baseId.Length > maxBaseLength) {
                baseId = baseId[..maxBaseLength].Trim('_');
            }
        }
        if (baseId.Length == 0) {
            baseId = HostGeneratedToolCallIdFallbackPrefix;
        }

        var availableSuffixLength = Math.Max(8, MaxHostGeneratedToolCallIdChars - baseId.Length - 1);
        var guid = Guid.NewGuid().ToString("N");
        if (guid.Length > availableSuffixLength) {
            guid = guid[..availableSuffixLength];
        }

        return baseId + "_" + guid;
    }

    private static string NormalizeHostGeneratedToolCallIdSegment(string? value) {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasUnderscore = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasUnderscore = false;
                continue;
            }

            if (lastWasUnderscore) {
                continue;
            }

            builder.Append('_');
            lastWasUnderscore = true;
        }

        return builder.ToString().Trim('_');
    }
}
