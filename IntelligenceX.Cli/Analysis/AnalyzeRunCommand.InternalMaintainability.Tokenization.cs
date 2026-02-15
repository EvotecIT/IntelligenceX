using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static List<SignificantLine> ReadSignificantLines(SourceFileEntry sourceFile, List<string> warnings) {
        try {
            var content = File.ReadAllText(sourceFile.FullPath);
            var extension = Path.GetExtension(sourceFile.FullPath);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromRoslynTokens(content);
            }
            if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromPowerShellTokens(content);
            }
            if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromJavaScriptTokens(content);
            }
            if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromPythonTokens(content);
            }
            return BuildSignificantLinesFallback(content);
        } catch (Exception ex) {
            warnings.Add($"Failed to read file for duplication check ({sourceFile.RelativePath}): {ex.Message}");
            return new List<SignificantLine>();
        }
    }

    private static List<SignificantLine> BuildSignificantLinesFromRoslynTokens(string content) {
        var lines = new Dictionary<int, List<string>>();
        var tree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = tree.GetRoot();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false)) {
            if (ShouldSkipDuplicationToken(token)) {
                continue;
            }
            var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (line <= 0) {
                continue;
            }
            if (!lines.TryGetValue(line, out var tokens)) {
                tokens = new List<string>();
                lines[line] = tokens;
            }
            tokens.Add(NormalizeDuplicationToken(token));
        }

        var significant = new List<SignificantLine>(lines.Count);
        foreach (var entry in lines.OrderBy(item => item.Key)) {
            if (entry.Value.Count == 0) {
                continue;
            }
            significant.Add(new SignificantLine(entry.Key, string.Join(" ", entry.Value)));
        }
        return significant;
    }

    private static bool ShouldSkipDuplicationToken(SyntaxToken token) {
        if (token.IsKind(SyntaxKind.OpenBraceToken) ||
            token.IsKind(SyntaxKind.CloseBraceToken) ||
            token.IsKind(SyntaxKind.SemicolonToken)) {
            return true;
        }
        var parent = token.Parent;
        if (parent is null) {
            return true;
        }
        if (parent is UsingDirectiveSyntax || parent is BaseNamespaceDeclarationSyntax) {
            return true;
        }
        return false;
    }

    private static string NormalizeDuplicationToken(SyntaxToken token) {
        if (token.IsKind(SyntaxKind.IdentifierToken)) {
            return "__ID__";
        }
        if (token.IsKind(SyntaxKind.NumericLiteralToken) ||
            token.IsKind(SyntaxKind.StringLiteralToken) ||
            token.IsKind(SyntaxKind.CharacterLiteralToken) ||
            token.IsKind(SyntaxKind.Utf8StringLiteralToken)) {
            return "__LIT__";
        }
        if (token.IsKind(SyntaxKind.InterpolatedStringTextToken)) {
            return "__LIT__";
        }
        var kindName = token.Kind().ToString();
        if (kindName.EndsWith("Token", StringComparison.Ordinal)) {
            return kindName.Substring(0, kindName.Length - "Token".Length);
        }
        return kindName;
    }

    private static List<SignificantLine> BuildSignificantLinesFromPowerShellTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            if (!TryStripPowerShellComment(lines[index], out var stripped)) {
                stripped = lines[index] ?? string.Empty;
            }

            var normalizedTokens = new List<string>();
            foreach (Match match in PowerShellTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizePowerShellToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }
            if (IsPowerShellUsingLine(normalizedTokens)) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }
        return result;
    }

    private static bool TryStripPowerShellComment(string input, out string stripped) {
        stripped = input ?? string.Empty;
        if (stripped.Length == 0) {
            return false;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < stripped.Length; i++) {
            var ch = stripped[i];
            if (ch == '\'' && !inDoubleQuote) {
                if (inSingleQuote && i + 1 < stripped.Length && stripped[i + 1] == '\'') {
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                continue;
            }
            if (ch == '"' && !inSingleQuote) {
                var escaped = i > 0 && stripped[i - 1] == '`';
                if (!escaped) {
                    inDoubleQuote = !inDoubleQuote;
                }
                continue;
            }
            if (ch == '#' && !inSingleQuote && !inDoubleQuote) {
                stripped = stripped.Substring(0, i);
                return true;
            }
        }

        return false;
    }

    private static string NormalizePowerShellToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsPowerShellStructureOnlyToken(trimmed)) {
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
            if (PowerShellKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsPowerShellStructureOnlyToken(string token) {
        return token is "{" or "}" or ";" or "(" or ")" or "[" or "]" or ",";
    }

    private static bool IsPowerShellUsingLine(IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }
        if (!tokens[0].Equals("using", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        for (var i = 1; i < tokens.Count; i++) {
            var token = tokens[i];
            if (string.IsNullOrWhiteSpace(token)) {
                continue;
            }
            if (token.Equals("__ID__", StringComparison.Ordinal) ||
                token.Equals("__LIT__", StringComparison.Ordinal)) {
                continue;
            }
            // Keep conservative: treat "using *" lines as import-only noise for duplication calculations.
            if (token.Equals(".", StringComparison.Ordinal) ||
                token.Equals("/", StringComparison.Ordinal) ||
                token.Equals("\\", StringComparison.Ordinal) ||
                token.Equals(":", StringComparison.Ordinal)) {
                continue;
            }
            return false;
        }

        return true;
    }

    private static List<SignificantLine> BuildSignificantLinesFromJavaScriptTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inBlockComment = false;
        for (var index = 0; index < lines.Length; index++) {
            var stripped = StripJavaScriptComments(lines[index] ?? string.Empty, ref inBlockComment);
            var normalizedTokens = new List<string>();
            foreach (Match match in JavaScriptTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizeJavaScriptToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }
            if (IsJavaScriptImportOrExportLine(normalizedTokens)) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }

        return result;
    }

    private static bool IsJavaScriptImportOrExportLine(IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }
        var hasImport = false;
        var hasExport = false;
        foreach (var token in tokens) {
            if (string.Equals(token, "import", StringComparison.OrdinalIgnoreCase)) {
                hasImport = true;
            } else if (string.Equals(token, "export", StringComparison.OrdinalIgnoreCase)) {
                hasExport = true;
            }
        }
        if (!hasImport && !hasExport) {
            return false;
        }

        foreach (var token in tokens) {
            if (string.IsNullOrWhiteSpace(token)) {
                continue;
            }
            if (token.Equals("__ID__", StringComparison.Ordinal) ||
                token.Equals("__LIT__", StringComparison.Ordinal)) {
                continue;
            }
            // Keep this conservative: only strip import/export-only lines to avoid suppressing real code.
            if (token.Equals("import", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("export", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("from", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("as", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("*", StringComparison.Ordinal)) {
                continue;
            }
            return false;
        }
        return true;
    }

    private static string StripJavaScriptComments(string input, ref bool inBlockComment) {
        if (string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inTemplateString = false;
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            var next = i + 1 < input.Length ? input[i + 1] : '\0';

            if (inBlockComment) {
                if (ch == '*' && next == '/') {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inTemplateString) {
                if (ch == '/' && next == '/') {
                    break;
                }
                if (ch == '/' && next == '*') {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (ch == '\'' && !inDoubleQuote && !inTemplateString) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inSingleQuote = !inSingleQuote;
                }
                sb.Append(ch);
                continue;
            }

            if (ch == '"' && !inSingleQuote && !inTemplateString) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inDoubleQuote = !inDoubleQuote;
                }
                sb.Append(ch);
                continue;
            }

            if (ch == '`' && !inSingleQuote && !inDoubleQuote) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inTemplateString = !inTemplateString;
                }
                sb.Append(ch);
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string NormalizeJavaScriptToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsJavaScriptStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("`", StringComparison.Ordinal) && trimmed.EndsWith("`", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_' || trimmed[0] == '$') {
            if (JavaScriptKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsJavaScriptStructureOnlyToken(string token) {
        return token is "{" or "}" or ";" or "(" or ")" or "[" or "]" or "," or ".";
    }

    private static List<SignificantLine> BuildSignificantLinesFromPythonTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inTripleSingleQuote = false;
        var inTripleDoubleQuote = false;
        for (var index = 0; index < lines.Length; index++) {
            var stripped = StripPythonComments(lines[index] ?? string.Empty, ref inTripleSingleQuote, ref inTripleDoubleQuote);

            var normalizedTokens = new List<string>();
            foreach (Match match in PythonTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizePythonToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }
            if (IsPythonImportLine(normalizedTokens)) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }
        return result;
    }

    private static bool IsPythonImportLine(IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }
        var hasImportLikeKeyword = false;
        foreach (var token in tokens) {
            if (string.Equals(token, "import", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "from", StringComparison.OrdinalIgnoreCase)) {
                hasImportLikeKeyword = true;
                break;
            }
        }
        if (!hasImportLikeKeyword) {
            return false;
        }

        foreach (var token in tokens) {
            if (string.IsNullOrWhiteSpace(token)) {
                continue;
            }
            if (token.Equals("__ID__", StringComparison.Ordinal)) {
                continue;
            }
            if (token.Equals("import", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("from", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("as", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            return false;
        }
        return true;
    }

    private static string StripPythonComments(string input, ref bool inTripleSingleQuote, ref bool inTripleDoubleQuote) {
        if (string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        var stripped = new StringBuilder(input.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];

            if (inTripleSingleQuote) {
                stripped.Append(ch);
                if (ch == '\'' && i + 2 < input.Length && input[i + 1] == '\'' && input[i + 2] == '\'') {
                    stripped.Append(input[i + 1]);
                    stripped.Append(input[i + 2]);
                    i += 2;
                    inTripleSingleQuote = false;
                }
                continue;
            }
            if (inTripleDoubleQuote) {
                stripped.Append(ch);
                if (ch == '"' && i + 2 < input.Length && input[i + 1] == '"' && input[i + 2] == '"') {
                    stripped.Append(input[i + 1]);
                    stripped.Append(input[i + 2]);
                    i += 2;
                    inTripleDoubleQuote = false;
                }
                continue;
            }

            if (inSingleQuote) {
                stripped.Append(ch);
                if (ch == '\'' && !IsEscapedPythonCharacter(input, i)) {
                    inSingleQuote = false;
                }
                continue;
            }
            if (inDoubleQuote) {
                stripped.Append(ch);
                if (ch == '"' && !IsEscapedPythonCharacter(input, i)) {
                    inDoubleQuote = false;
                }
                continue;
            }

            if (ch == '#') {
                break;
            }

            if (ch == '\'' && i + 2 < input.Length && input[i + 1] == '\'' && input[i + 2] == '\'') {
                stripped.Append(ch);
                stripped.Append(input[i + 1]);
                stripped.Append(input[i + 2]);
                i += 2;
                inTripleSingleQuote = true;
                continue;
            }
            if (ch == '"' && i + 2 < input.Length && input[i + 1] == '"' && input[i + 2] == '"') {
                stripped.Append(ch);
                stripped.Append(input[i + 1]);
                stripped.Append(input[i + 2]);
                i += 2;
                inTripleDoubleQuote = true;
                continue;
            }

            if (ch == '\'') {
                inSingleQuote = true;
                stripped.Append(ch);
                continue;
            }
            if (ch == '"') {
                inDoubleQuote = true;
                stripped.Append(ch);
                continue;
            }

            stripped.Append(ch);
        }

        return stripped.ToString();
    }

    private static bool IsEscapedPythonCharacter(string input, int index) {
        if (index <= 0 || string.IsNullOrEmpty(input)) {
            return false;
        }
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && input[i] == '\\'; i--) {
            slashCount++;
        }
        return (slashCount & 1) == 1;
    }

    private static string NormalizePythonToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsPythonStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_') {
            if (PythonKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsPythonStructureOnlyToken(string token) {
        return token is "(" or ")" or "[" or "]" or "{" or "}" or "," or "." or ":";
    }

    private static List<SignificantLine> BuildSignificantLinesFallback(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var normalized = lines[index]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("//", StringComparison.Ordinal) ||
                normalized.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            result.Add(new SignificantLine(index + 1, normalized));
        }
        return result;
    }

    private static string BuildWindowSignature(IReadOnlyList<SignificantLine> lines, int startIndex, int windowSize) {
        var builder = new StringBuilder(windowSize * 24);
        for (var i = 0; i < windowSize; i++) {
            if (i > 0) {
                builder.Append('\n');
            }
            builder.Append(lines[startIndex + i].Value);
        }
        return builder.ToString();
    }

    private static bool HasDistinctWindowOccurrences(IReadOnlyList<WindowOccurrence> occurrences) {
        if (occurrences is null || occurrences.Count < 2) {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences) {
            seen.Add($"{occurrence.FileIndex}:{occurrence.StartIndex}");
            if (seen.Count > 1) {
                return true;
            }
        }
        return false;
    }

    private static double ComputePercent(int part, int whole) {
        if (whole <= 0) {
            return 0;
        }
        return Math.Round((part * 100.0) / whole, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatPercent(double value) {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
