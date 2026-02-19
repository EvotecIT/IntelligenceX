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
    private static readonly string[] RequiredWriteSchemaHelperNames = {
        "WithWriteGovernanceDefaults",
        "WithWriteGovernanceAndAuthenticationProbe"
    };

    private static readonly string[] ToolDefinitionParameterOrder = {
        "name",
        "description",
        "parameters",
        "displayName",
        "category",
        "tags",
        "writeGovernance",
        "aliases",
        "aliasOf",
        "authentication"
    };

    private static IReadOnlyList<AnalysisFindingItem> RunWriteToolSchemaChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }

        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            findings.AddRange(EvaluateWriteToolSchemaRule(rule, sourceFiles, excludedOutputPath, warnings));
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateWriteToolSchemaRule(AnalysisPolicyRule policyRule,
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

            findings.AddRange(EvaluateWriteToolSchemaFile(sourceFile, severity, emittedRuleId, emittedTool, warnings));
        }

        return findings;
    }

    private static bool IsWriteToolCandidateSourceFile(string? relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("IntelligenceX.Tools/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith("Tool.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateWriteToolSchemaFile(SourceFileEntry sourceFile, string severity,
        string emittedRuleId, string emittedTool, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        string content;
        try {
            content = File.ReadAllText(sourceFile.FullPath);
        } catch (Exception ex) {
            warnings.Add($"Failed to read file for write-tool schema checks ({sourceFile.RelativePath}): {ex.Message}");
            return findings;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = syntaxTree.GetRoot();
        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()) {
            if (!IsToolDefinitionObjectCreation(creation)) {
                continue;
            }

            if (!IsWriteGovernanceConfigured(creation)) {
                continue;
            }

            if (!TryGetParametersExpression(creation, out var parametersExpression)) {
                continue;
            }

            if (UsesRequiredWriteSchemaHelper(parametersExpression, root)) {
                continue;
            }

            var line = syntaxTree.GetLineSpan(parametersExpression.Span).StartLinePosition.Line + 1;
            findings.Add(new AnalysisFindingItem {
                Path = sourceFile.RelativePath,
                Line = line,
                Severity = severity,
                Message =
                    "Write-capable tool schema should use WithWriteGovernanceDefaults() or WithWriteGovernanceAndAuthenticationProbe().",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{emittedRuleId}:{sourceFile.RelativePath}:{line}"
            });
        }

        return findings;
    }

    private static bool IsToolDefinitionObjectCreation(BaseObjectCreationExpressionSyntax creation) {
        if (creation is ObjectCreationExpressionSyntax explicitCreation) {
            return IsToolDefinitionTypeName(explicitCreation.Type?.ToString());
        }

        if (creation is not ImplicitObjectCreationExpressionSyntax) {
            return false;
        }

        var declaration = creation.Ancestors()
            .OfType<VariableDeclarationSyntax>()
            .FirstOrDefault();
        if (declaration is null) {
            return false;
        }

        return IsToolDefinitionTypeName(declaration.Type?.ToString());
    }

    private static bool IsToolDefinitionTypeName(string? typeText) {
        if (string.IsNullOrWhiteSpace(typeText)) {
            return false;
        }

        return string.Equals(typeText, "ToolDefinition", StringComparison.Ordinal) ||
               typeText.EndsWith(".ToolDefinition", StringComparison.Ordinal);
    }

    private static bool IsWriteGovernanceConfigured(BaseObjectCreationExpressionSyntax creation) {
        if (!TryMapToolDefinitionArguments(creation, out var argumentMap)) {
            return false;
        }

        if (!argumentMap.TryGetValue("writeGovernance", out var writeGovernanceExpression) ||
            writeGovernanceExpression is null) {
            return false;
        }

        return !IsNullLiteral(writeGovernanceExpression);
    }

    private static bool IsNullLiteral(ExpressionSyntax expression) {
        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.NullLiteralExpression);
    }

    private static bool TryGetParametersExpression(BaseObjectCreationExpressionSyntax creation, out ExpressionSyntax expression) {
        expression = null!;
        if (!TryMapToolDefinitionArguments(creation, out var argumentMap)) {
            return false;
        }

        if (argumentMap.TryGetValue("parameters", out var parametersExpression) && parametersExpression is not null) {
            expression = parametersExpression;
            return true;
        }

        return false;
    }

    private static bool TryMapToolDefinitionArguments(
        BaseObjectCreationExpressionSyntax creation,
        out Dictionary<string, ExpressionSyntax> argumentMap) {
        argumentMap = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        var arguments = creation.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count == 0) {
            return false;
        }

        var nextPositionalIndex = 0;
        foreach (var argument in arguments.Value) {
            var parameterName = argument.NameColon?.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(parameterName)) {
                parameterName = GetNextPositionalParameterName(argumentMap, ref nextPositionalIndex);
            }

            if (string.IsNullOrWhiteSpace(parameterName)) {
                continue;
            }

            argumentMap[parameterName] = argument.Expression;
        }

        return argumentMap.Count > 0;
    }

    private static string? GetNextPositionalParameterName(
        Dictionary<string, ExpressionSyntax> argumentMap,
        ref int nextPositionalIndex) {
        while (nextPositionalIndex < ToolDefinitionParameterOrder.Length) {
            var candidate = ToolDefinitionParameterOrder[nextPositionalIndex];
            nextPositionalIndex++;
            if (!argumentMap.ContainsKey(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static bool UsesRequiredWriteSchemaHelper(ExpressionSyntax parametersExpression, SyntaxNode root) {
        if (ContainsRequiredHelperInvocation(parametersExpression)) {
            return true;
        }

        if (parametersExpression is IdentifierNameSyntax identifier &&
            TryGetInitializerExpression(root, identifier.Identifier.ValueText, out var initializerExpression)) {
            return ContainsRequiredHelperInvocation(initializerExpression);
        }

        return false;
    }

    private static bool TryGetInitializerExpression(SyntaxNode root, string variableName, out ExpressionSyntax expression) {
        expression = null!;
        if (string.IsNullOrWhiteSpace(variableName)) {
            return false;
        }

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>()) {
            if (!string.Equals(declarator.Identifier.ValueText, variableName, StringComparison.Ordinal)) {
                continue;
            }

            if (declarator.Initializer?.Value is not null) {
                expression = declarator.Initializer.Value;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRequiredHelperInvocation(ExpressionSyntax expression) {
        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()) {
            var methodName = TryGetInvocationMethodName(invocation);
            if (string.IsNullOrWhiteSpace(methodName)) {
                continue;
            }

            for (var i = 0; i < RequiredWriteSchemaHelperNames.Length; i++) {
                if (string.Equals(methodName, RequiredWriteSchemaHelperNames[i], StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TryGetInvocationMethodName(InvocationExpressionSyntax invocation) {
        return invocation.Expression switch {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
    }
}
