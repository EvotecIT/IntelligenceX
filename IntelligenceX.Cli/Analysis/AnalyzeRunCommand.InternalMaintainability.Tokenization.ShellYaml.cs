using System;
using System.Collections.Generic;
using System.Globalization;
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
            var normalizedTokens = new List<string>();
            foreach (Match match in ShellTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizeShellToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
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
            var normalizedTokens = new List<string>();
            foreach (Match match in YamlTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizeYamlToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }
        return result;
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
                arithmeticExpansionDepth == 0) {
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
}
