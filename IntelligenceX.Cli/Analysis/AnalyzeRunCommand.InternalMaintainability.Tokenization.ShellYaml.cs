using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static readonly Regex ShellTokenRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|\\$\\{?[A-Za-z_][A-Za-z0-9_]*\\}?|[A-Za-z_][A-Za-z0-9_:-]*|\\d+(?:\\.\\d+)?|==|!=|<=|>=|\\+\\+|--|&&|\\|\\||[-+*/%=!<>|&.:?]+|[()\\[\\]{};,]",
        RegexOptions.Compiled);
    private static readonly Regex YamlTokenRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:''|[^'])*'|[A-Za-z_][A-Za-z0-9_-]*|\\d+(?:\\.\\d+)?|[:{}\\[\\],.-]+",
        RegexOptions.Compiled);
    private static readonly HashSet<string> ShellKeywordTokens = new(StringComparer.OrdinalIgnoreCase) {
        "if",
        "then",
        "else",
        "elif",
        "fi",
        "for",
        "while",
        "until",
        "do",
        "done",
        "case",
        "esac",
        "function",
        "in"
    };
    private static readonly HashSet<string> YamlKeywordTokens = new(StringComparer.OrdinalIgnoreCase) {
        "true",
        "false",
        "null",
        "yes",
        "no",
        "on",
        "off"
    };

    private static List<SignificantLine> BuildSignificantLinesFromShellTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var rawLine = lines[index] ?? string.Empty;
            if (index == 0 && rawLine.StartsWith("#!", StringComparison.Ordinal)) {
                continue;
            }

            var stripped = StripInlineShellHashComment(rawLine);
            var normalizedLine = BuildNormalizedTokenLine(stripped, ShellTokenRegex, NormalizeShellToken);
            if (string.IsNullOrWhiteSpace(normalizedLine)) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, normalizedLine));
        }
        return result;
    }

    private static string NormalizeShellToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsShellStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (trimmed.StartsWith("$", StringComparison.Ordinal)) {
            return "__ID__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_') {
            if (ShellKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsShellStructureOnlyToken(string token) {
        return token is "{" or "}" or ";" or "(" or ")" or "[" or "]" or "," or ".";
    }

    private static List<SignificantLine> BuildSignificantLinesFromYamlTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var stripped = StripInlineYamlHashComment(lines[index] ?? string.Empty);
            var normalizedLine = BuildNormalizedTokenLine(stripped, YamlTokenRegex, NormalizeYamlToken);
            if (string.IsNullOrWhiteSpace(normalizedLine)) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, normalizedLine));
        }
        return result;
    }

    private static string BuildNormalizedTokenLine(string line, Regex tokenRegex, Func<string, string> normalizeToken) {
        if (string.IsNullOrWhiteSpace(line) || tokenRegex is null || normalizeToken is null) {
            return string.Empty;
        }

        StringBuilder? builder = null;
        foreach (Match match in tokenRegex.Matches(line)) {
            if (!match.Success) {
                continue;
            }

            var normalized = normalizeToken(match.Value);
            if (string.IsNullOrWhiteSpace(normalized)) {
                continue;
            }

            builder ??= new StringBuilder(line.Length);
            if (builder.Length > 0) {
                builder.Append(' ');
            }
            builder.Append(normalized);
        }

        return builder?.ToString() ?? string.Empty;
    }

    private static string NormalizeYamlToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsYamlStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_') {
            if (YamlKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsYamlStructureOnlyToken(string token) {
        return token is ":" or "{" or "}" or "[" or "]" or "," or "." or "-";
    }

    // Heuristic shell scanner: tracks common expansion contexts where '#' is syntax (not comment).
    // It is not a full shell parser and intentionally keeps behavior conservative for duplication tokenization.
    // Known limitations (acceptable for duplication-only tokenization):
    // - does not parse heredoc bodies or ANSI-C strings ($'...')
    // - may not perfectly model deeply mixed nested shell expansion constructs
    // - treats comments by lexical boundaries, not full shell grammar execution rules
    private static string StripInlineShellHashComment(string input) {
        if (string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var parameterExpansionDepth = 0;
        var commandSubstitutionDepth = 0;
        var arithmeticExpansionDepth = 0;
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            var next = i + 1 < input.Length ? input[i + 1] : '\0';
            var nextNext = i + 2 < input.Length ? input[i + 2] : '\0';

            if (inSingleQuote) {
                if (ch == '\'') {
                    inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote) {
                if (ch == '"' && !IsEscapedByBackslash(input, i)) {
                    inDoubleQuote = false;
                }
                continue;
            }

            if (ch == '\'') {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"') {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$') {
                if (next == '{') {
                    parameterExpansionDepth++;
                    i++;
                    continue;
                }

                if (next == '(' && nextNext == '(') {
                    arithmeticExpansionDepth++;
                    i += 2;
                    continue;
                }

                if (next == '(') {
                    commandSubstitutionDepth++;
                    i++;
                    continue;
                }
            }

            if (ch == '}' && parameterExpansionDepth > 0) {
                parameterExpansionDepth--;
                continue;
            }

            if (ch == ')' && arithmeticExpansionDepth > 0 && next == ')') {
                arithmeticExpansionDepth--;
                i++;
                continue;
            }

            if (ch == ')' && commandSubstitutionDepth > 0) {
                commandSubstitutionDepth--;
                continue;
            }

            if (ch == '#' &&
                parameterExpansionDepth == 0 &&
                commandSubstitutionDepth == 0 &&
                arithmeticExpansionDepth == 0 &&
                IsShellCommentStart(input, i)) {
                return input.Substring(0, i);
            }
        }

        return input;
    }

    private static string StripInlineYamlHashComment(string input) {
        if (string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            if (ch == '\'' && !inDoubleQuote) {
                if (inSingleQuote && i + 1 < input.Length && input[i + 1] == '\'') {
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                continue;
            }
            if (ch == '"' && !inSingleQuote) {
                if (!IsEscapedByBackslash(input, i)) {
                    inDoubleQuote = !inDoubleQuote;
                }
                continue;
            }
            if (ch == '#' && !inSingleQuote && !inDoubleQuote) {
                return input.Substring(0, i);
            }
        }

        return input;
    }

    private static bool IsEscapedByBackslash(string input, int index) {
        if (string.IsNullOrEmpty(input) || index <= 0) {
            return false;
        }

        var slashCount = 0;
        for (var i = index - 1; i >= 0 && input[i] == '\\'; i--) {
            slashCount++;
        }
        return (slashCount & 1) == 1;
    }

    private static bool IsShellCommentStart(string input, int index) {
        if (string.IsNullOrEmpty(input) || index < 0 || index >= input.Length) {
            return false;
        }

        if (IsEscapedByBackslash(input, index)) {
            return false;
        }

        if (index == 0) {
            return true;
        }

        var previous = input[index - 1];
        if (char.IsWhiteSpace(previous)) {
            return true;
        }

        return previous is ';' or '(' or ')' or '{' or '}' or '|' or '&';
    }
}
