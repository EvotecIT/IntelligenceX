using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace IntelligenceX.Chat.App.Rendering;

internal static partial class TranscriptMarkdownNormalizer {
    private static string NormalizeWithOfficeImoInputNormalizer(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var bridge = OfficeImoInputNormalizationBridgeLazy.Value;
        if (bridge == null) {
            return text;
        }

        return bridge.Normalize(text);
    }

    private static OfficeImoInputNormalizationBridge? CreateOfficeImoInputNormalizationBridge() {
        try {
            var optionsType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizationOptions, OfficeIMO.Markdown", throwOnError: false);
            var normalizerType = Type.GetType("OfficeIMO.Markdown.MarkdownInputNormalizer, OfficeIMO.Markdown", throwOnError: false);
            if (optionsType == null || normalizerType == null) {
                return null;
            }

            var normalizeMethod = normalizerType.GetMethod(
                "Normalize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), optionsType],
                modifiers: null);
            if (normalizeMethod == null) {
                return null;
            }

            var enabledProperties = new List<PropertyInfo>(OfficeImoInputNormalizationPropertyNames.Length);
            foreach (var propertyName in OfficeImoInputNormalizationPropertyNames) {
                var property = optionsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is { CanWrite: true } && property.PropertyType == typeof(bool)) {
                    enabledProperties.Add(property);
                }
            }

            if (enabledProperties.Count == 0) {
                return null;
            }

            return new OfficeImoInputNormalizationBridge(optionsType, normalizeMethod, enabledProperties.ToArray());
        } catch {
            return null;
        }
    }

    private sealed class OfficeImoInputNormalizationBridge(Type optionsType, MethodInfo normalizeMethod, PropertyInfo[] enabledProperties) {
        public string Normalize(string text) {
            try {
                var options = Activator.CreateInstance(optionsType);
                if (options == null) {
                    return text;
                }

                for (var i = 0; i < enabledProperties.Length; i++) {
                    enabledProperties[i].SetValue(options, true);
                }

                var normalized = normalizeMethod.Invoke(null, [text, options]) as string;
                return string.IsNullOrEmpty(normalized) ? text : normalized;
            } catch {
                return text;
            }
        }
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
