using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static IReadOnlyList<AnalysisFindingItem> RunCanonicalBoundedIntHelperChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }

        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            findings.AddRange(EvaluateCanonicalBoundedIntHelperRule(rule, sourceFiles, excludedOutputPath, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateCanonicalBoundedIntHelperRule(AnalysisPolicyRule policyRule,
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
            if (!IsCanonicalBoundedIntHelperCandidateSourceFile(sourceFile.RelativePath)) {
                continue;
            }

            findings.AddRange(EvaluateCanonicalBoundedIntHelperFile(sourceFile, severity, emittedRuleId, emittedTool,
                warnings));
        }

        return findings;
    }

    private static bool IsCanonicalBoundedIntHelperCandidateSourceFile(string? relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("IntelligenceX.Tools/", StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (normalized.StartsWith("IntelligenceX.Tools/IntelligenceX.Tools.Tests/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (normalized.Equals("IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolArgs.cs",
                StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateCanonicalBoundedIntHelperFile(SourceFileEntry sourceFile,
        string severity, string emittedRuleId, string emittedTool, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        string content;
        try {
            content = File.ReadAllText(sourceFile.FullPath);
        } catch (Exception ex) {
            warnings.Add(
                $"Failed to read file for canonical bounded-int helper checks ({sourceFile.RelativePath}): {ex.Message}");
            return findings;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = syntaxTree.GetRoot();
        var seenLines = new HashSet<int>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (!IsLegacyOptionBoundedHelperInvocation(invocation)) {
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
                Message =
                    "Use ToolArgs.GetOptionBoundedInt32(..., nonPositiveBehavior: ToolArgs.NonPositiveInt32Behavior.UseDefault, defaultValue: ...) instead of ToolArgs.GetPositiveOptionBoundedInt32OrDefault(...).",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{emittedRuleId}:{sourceFile.RelativePath}:{line}"
            });
        }

        return findings;
    }

    private static bool IsLegacyOptionBoundedHelperInvocation(InvocationExpressionSyntax invocation) {
        if (!string.Equals(TryGetInvocationMethodName(invocation), "GetPositiveOptionBoundedInt32OrDefault",
                StringComparison.Ordinal)) {
            return false;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression.ToString().EndsWith("ToolArgs", StringComparison.Ordinal);
    }
}
