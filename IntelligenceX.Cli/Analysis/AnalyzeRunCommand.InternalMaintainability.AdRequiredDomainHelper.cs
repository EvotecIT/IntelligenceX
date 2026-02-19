using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static readonly string[] RequiredAdDomainHelperNames = {
        "TryReadRequiredDomainQueryRequest",
        "TryReadPolicyAttributionToolRequest",
        "ExecuteDomainRowsViewTool",
        "ExecutePolicyAttributionTool"
    };

    private static IReadOnlyList<AnalysisFindingItem> RunAdRequiredDomainHelperChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }

        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            findings.AddRange(EvaluateAdRequiredDomainHelperRule(rule, sourceFiles, excludedOutputPath, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateAdRequiredDomainHelperRule(AnalysisPolicyRule policyRule,
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
            if (!IsAdRequiredDomainCandidateSourceFile(sourceFile.RelativePath)) {
                continue;
            }

            findings.AddRange(EvaluateAdRequiredDomainHelperFile(sourceFile, severity, emittedRuleId, emittedTool, warnings));
        }

        return findings;
    }

    private static bool IsAdRequiredDomainCandidateSourceFile(string? relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("IntelligenceX.Tools/IntelligenceX.Tools.ADPlayground/",
                   StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith("Tool.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateAdRequiredDomainHelperFile(SourceFileEntry sourceFile,
        string severity, string emittedRuleId, string emittedTool, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        string content;
        try {
            content = File.ReadAllText(sourceFile.FullPath);
        } catch (Exception ex) {
            warnings.Add(
                $"Failed to read file for AD required-domain helper checks ({sourceFile.RelativePath}): {ex.Message}");
            return findings;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = syntaxTree.GetRoot();
        var usesCanonicalHelper = ContainsRequiredAdDomainHelperInvocation(root);

        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()) {
            if (!IsToolDefinitionObjectCreation(creation)) {
                continue;
            }

            if (!TryGetParametersExpression(creation, out var parametersExpression)) {
                continue;
            }

            if (!TryGetRequiredDomainNameLine(parametersExpression, root, syntaxTree, out var line)) {
                continue;
            }

            if (usesCanonicalHelper) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = sourceFile.RelativePath,
                Line = line,
                Severity = severity,
                Message =
                    "AD tools with required domain_name should use ExecuteDomainRowsViewTool/ExecutePolicyAttributionTool or TryReadRequiredDomainQueryRequest/TryReadPolicyAttributionToolRequest.",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{emittedRuleId}:{sourceFile.RelativePath}:{line}"
            });
        }

        return findings;
    }

    private static bool TryGetRequiredDomainNameLine(ExpressionSyntax parametersExpression, SyntaxNode root, SyntaxTree syntaxTree,
        out int line) {
        line = 0;
        if (TryGetRequiredDomainNameLineFromExpression(parametersExpression, syntaxTree, out line)) {
            return true;
        }

        if (parametersExpression is IdentifierNameSyntax identifier &&
            TryGetInitializerExpression(root, identifier.Identifier.ValueText, out var initializerExpression)) {
            return TryGetRequiredDomainNameLineFromExpression(initializerExpression, syntaxTree, out line);
        }

        return false;
    }

    private static bool TryGetRequiredDomainNameLineFromExpression(ExpressionSyntax expression, SyntaxTree syntaxTree,
        out int line) {
        line = 0;
        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()) {
            var methodName = TryGetInvocationMethodName(invocation);
            if (!string.Equals(methodName, "Required", StringComparison.Ordinal)) {
                continue;
            }

            var firstArgument = invocation.ArgumentList?.Arguments.FirstOrDefault();
            if (firstArgument?.Expression is null) {
                continue;
            }

            if (!TryGetStringLiteralValue(firstArgument.Expression, out var value) ||
                !string.Equals(value, "domain_name", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            line = syntaxTree.GetLineSpan(firstArgument.Expression.Span).StartLinePosition.Line + 1;
            return true;
        }

        return false;
    }

    private static bool TryGetStringLiteralValue(ExpressionSyntax expression, out string value) {
        value = string.Empty;
        if (expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression)) {
            return false;
        }

        value = literal.Token.ValueText ?? string.Empty;
        return true;
    }

    private static bool ContainsRequiredAdDomainHelperInvocation(SyntaxNode root) {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            var methodName = TryGetInvocationMethodName(invocation);
            if (string.IsNullOrWhiteSpace(methodName)) {
                continue;
            }

            for (var i = 0; i < RequiredAdDomainHelperNames.Length; i++) {
                if (string.Equals(methodName, RequiredAdDomainHelperNames[i], StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }
}
