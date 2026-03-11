using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using IntelligenceX.Chat.App;

namespace IntelligenceX.Chat.App.Rendering;

internal static partial class TranscriptMarkdownNormalizer {
    private static string NormalizeWithOfficeImoInputNormalizer(string text) {
        return OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(text);
    }

    private static string FlattenNestedStrongOutsideInlineCode(string body) {
        if (string.IsNullOrEmpty(body) || !body.Contains("**", StringComparison.Ordinal)) {
            return body;
        }

        var protectedBody = ProtectInlineCodeSpans(body, out var codeSpans, out var tokenPrefix);
        var flattened = FlattenNestedStrongSpans(protectedBody);
        return RestoreInlineCodeSpans(flattened, codeSpans, tokenPrefix);
    }

    private static string FlattenNestedStrongSpans(string input) {
        if (string.IsNullOrEmpty(input) || !input.Contains("**", StringComparison.Ordinal)) {
            return input;
        }

        string current = input;
        for (var i = 0; i < StrongFlattenMaxIterations; i++) {
            var next = NestedStrongSpanRegex.Replace(
                current,
                match => {
                    var inner = match.Groups["inner"].Value;
                    if (inner.Length == 0) {
                        return inner;
                    }

                    var prefix = string.Empty;
                    var suffix = string.Empty;
                    var start = match.Index;
                    var end = match.Index + match.Length;
                    if (start > 0) {
                        var before = current[start - 1];
                        if (!char.IsWhiteSpace(before) && IsWordLikeChar(before) && IsWordLikeChar(inner[0])) {
                            prefix = " ";
                        }
                    }

                    if (end < current.Length) {
                        var after = current[end];
                        if (!char.IsWhiteSpace(after) && IsWordLikeChar(inner[^1]) && IsWordLikeChar(after)) {
                            suffix = " ";
                        }
                    }

                    return prefix + inner + suffix;
                });
            if (next.Equals(current, StringComparison.Ordinal)) {
                return next;
            }

            current = next;
        }

        return current;
    }

    private static bool IsWordLikeChar(char value) {
        return char.IsLetterOrDigit(value);
    }

    private static string ProtectInlineCodeSpans(string input, out List<string> codeSpans, out string tokenPrefix) {
        var capturedCodeSpans = new List<string>();
        if (string.IsNullOrEmpty(input) || input.IndexOf('`', StringComparison.Ordinal) < 0) {
            codeSpans = capturedCodeSpans;
            tokenPrefix = string.Empty;
            return input;
        }

        var prefixId = unchecked((uint)Interlocked.Increment(ref InlineCodePlaceholderCounter))
            .ToString("x8", CultureInfo.InvariantCulture);
        var prefix = "\u001FIXCODE_" + prefixId + "_";

        var protectedInput = InlineCodeSpanRegex.Replace(input, match => {
            var index = capturedCodeSpans.Count;
            capturedCodeSpans.Add(match.Value);
            return prefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });
        protectedInput = UnmatchedInlineCodeTailRegex.Replace(protectedInput, match => {
            var index = capturedCodeSpans.Count;
            capturedCodeSpans.Add(match.Value);
            return prefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });

        tokenPrefix = prefix;
        codeSpans = capturedCodeSpans;
        return protectedInput;
    }

    private static string RestoreInlineCodeSpans(string input, IReadOnlyList<string> codeSpans, string tokenPrefix) {
        if (codeSpans.Count == 0 || string.IsNullOrEmpty(input) || string.IsNullOrEmpty(tokenPrefix)) {
            return input;
        }

        // Keep placeholder replacement strictly opt-in for the current call's token prefix.
        // This avoids mutating user text that may coincidentally match the placeholder shape.
        return InlineCodePlaceholderRegex.Replace(input, match => {
            if (!match.Value.StartsWith(tokenPrefix, StringComparison.Ordinal)) {
                return match.Value;
            }

            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) {
                return match.Value;
            }

            return index >= 0 && index < codeSpans.Count
                ? codeSpans[index]
                : match.Value;
        });
    }
}
