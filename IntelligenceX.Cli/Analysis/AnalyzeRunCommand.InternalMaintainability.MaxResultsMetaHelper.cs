using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static IReadOnlyList<AnalysisFindingItem> RunMaxResultsMetaHelperChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }

        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            findings.AddRange(EvaluateMaxResultsMetaHelperRule(rule, sourceFiles, excludedOutputPath, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateMaxResultsMetaHelperRule(AnalysisPolicyRule policyRule,
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
            if (!IsWriteToolCandidateSourceFile(sourceFile.RelativePath)) {
                continue;
            }

            findings.AddRange(EvaluateMaxResultsMetaHelperFile(sourceFile, severity, emittedRuleId, emittedTool, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateMaxResultsMetaHelperFile(SourceFileEntry sourceFile,
        string severity, string emittedRuleId, string emittedTool, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        string content;
        try {
            content = File.ReadAllText(sourceFile.FullPath);
        } catch (Exception ex) {
            warnings.Add($"Failed to read file for max-results metadata helper checks ({sourceFile.RelativePath}): {ex.Message}");
            return findings;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = syntaxTree.GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (!IsDirectMaxResultsMetaAddInvocation(invocation)) {
                continue;
            }

            var line = syntaxTree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
            findings.Add(new AnalysisFindingItem {
                Path = sourceFile.RelativePath,
                Line = line,
                Severity = severity,
                Message = "Use AddMaxResultsMeta(meta, value) instead of meta.Add(\"max_results\", value).",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{emittedRuleId}:{sourceFile.RelativePath}:{line}"
            });
        }

        return findings;
    }

    private static bool IsDirectMaxResultsMetaAddInvocation(InvocationExpressionSyntax invocation) {
        if (!string.Equals(TryGetInvocationMethodName(invocation), "Add", StringComparison.Ordinal)) {
            return false;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) {
            return false;
        }

        if (memberAccess.Expression is not IdentifierNameSyntax identifierName ||
            !string.Equals(identifierName.Identifier.ValueText, "meta", StringComparison.Ordinal)) {
            return false;
        }

        var arguments = invocation.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count < 2) {
            return false;
        }

        return TryGetStringLiteralValue(arguments.Value[0].Expression, out var value) &&
               string.Equals(value, "max_results", StringComparison.OrdinalIgnoreCase);
    }
}
