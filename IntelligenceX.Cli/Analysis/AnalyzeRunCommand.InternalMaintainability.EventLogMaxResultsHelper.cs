using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static IReadOnlyList<AnalysisFindingItem> RunEventLogMaxResultsHelperChecks(
        IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles,
        string? excludedOutputPath,
        List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }

        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            findings.AddRange(EvaluateEventLogMaxResultsHelperRule(rule, sourceFiles, excludedOutputPath, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateEventLogMaxResultsHelperRule(AnalysisPolicyRule policyRule,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var severity = NormalizeSeverity(policyRule.Severity);
        if (string.IsNullOrWhiteSpace(severity)) {
            Console.WriteLine($"Internal maintainability rule {policyRule.Rule.Id} is disabled by policy severity.");
            return findings;
        }

        ValidateInternalMaintainabilityTags(policyRule.Rule, WriteToolSchemaSupportedTagPrefixes, warnings);
        var filteredFiles = FilterSourceFilesForRule(policyRule.Rule, sourceFiles, excludedOutputPath, warnings);
        if (filteredFiles.Count == 0) {
            return findings;
        }

        var emittedRuleId = string.IsNullOrWhiteSpace(policyRule.Rule.ToolRuleId)
            ? policyRule.Rule.Id
            : policyRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(policyRule.Rule.Tool)
            ? InternalToolName
            : policyRule.Rule.Tool;

        foreach (var sourceFile in filteredFiles) {
            if (!IsEventLogMaxResultsHelperCandidateSourceFile(sourceFile.RelativePath)) {
                continue;
            }

            findings.AddRange(EvaluateEventLogMaxResultsHelperFile(sourceFile, severity, emittedRuleId, emittedTool,
                warnings));
        }

        return findings;
    }

    private static bool IsEventLogMaxResultsHelperCandidateSourceFile(string? relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("IntelligenceX.Tools/IntelligenceX.Tools.EventLog/",
                StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith("Tool.cs", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (normalized.StartsWith("IntelligenceX.Tools/IntelligenceX.Tools.EventLog.Tests/",
                StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateEventLogMaxResultsHelperFile(SourceFileEntry sourceFile,
        string severity, string emittedRuleId, string emittedTool, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        string content;
        try {
            content = File.ReadAllText(sourceFile.FullPath);
        } catch (Exception ex) {
            warnings.Add(
                $"Failed to read file for EventLog max-results helper checks ({sourceFile.RelativePath}): {ex.Message}");
            return findings;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = syntaxTree.GetRoot();
        var seenLines = new HashSet<int>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (!TryGetLegacyEventLogMaxResultsHelperMessage(invocation, out var message)) {
                continue;
            }

            var line = syntaxTree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
            if (!seenLines.Add(line)) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = sourceFile.RelativePath,
                Line = line,
                Severity = severity,
                Message = message,
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{emittedRuleId}:{sourceFile.RelativePath}:{line}"
            });
        }

        return findings;
    }

    private static bool TryGetLegacyEventLogMaxResultsHelperMessage(InvocationExpressionSyntax invocation,
        out string message) {
        message =
            "In EventLog tools, use ResolveOptionBoundedMaxResults(...) for option-bounded max_results and ResolveCappedMaxResults(...) for explicit default/cap behavior.";
        var methodName = TryGetInvocationMethodName(invocation);
        if (string.Equals(methodName, "ResolveMaxResults", StringComparison.Ordinal)) {
            return true;
        }

        if (!string.Equals(methodName, "ResolveBoundedOptionLimit", StringComparison.Ordinal)) {
            return false;
        }

        return IsMaxResultsBoundedOptionInvocation(invocation);
    }

    private static bool IsMaxResultsBoundedOptionInvocation(InvocationExpressionSyntax invocation) {
        var arguments = invocation.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count == 0) {
            return false;
        }

        foreach (var argument in arguments.Value) {
            var parameterName = argument.NameColon?.Name.Identifier.ValueText;
            if (string.Equals(parameterName, "argumentName", StringComparison.Ordinal) &&
                TryGetStringLiteralValue(argument.Expression, out var value) &&
                string.Equals(value, "max_results", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        if (arguments.Value.Count < 2) {
            return false;
        }

        return TryGetStringLiteralValue(arguments.Value[1].Expression, out var positionalValue) &&
               string.Equals(positionalValue, "max_results", StringComparison.OrdinalIgnoreCase);
    }
}
