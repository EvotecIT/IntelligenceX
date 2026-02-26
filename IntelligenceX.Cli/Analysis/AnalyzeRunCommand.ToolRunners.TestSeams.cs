using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    internal static IReadOnlyList<string> BuildJavaScriptRunnerArgsForTests(
        string sarifPath,
        IReadOnlyDictionary<string, string> severityByToolRuleId) {
        var selectors = new List<ExternalToolRuleSelector>();
        foreach (var pair in severityByToolRuleId ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) {
                continue;
            }
            selectors.Add(new ExternalToolRuleSelector(pair.Key.Trim(), pair.Value.Trim()));
        }
        return BuildJavaScriptRunnerArgs(sarifPath, selectors);
    }

    internal static IReadOnlyList<string> BuildPythonRunnerArgsForTests(IReadOnlyList<string> selectedRuleIds) {
        return BuildPythonRunnerArgs(string.Empty, selectedRuleIds, includeOutputFile: false);
    }

    internal static IReadOnlyList<string> BuildPythonRunnerArgsWithOutputForTests(string sarifPath,
        IReadOnlyList<string> selectedRuleIds) {
        return BuildPythonRunnerArgs(sarifPath, selectedRuleIds, includeOutputFile: true);
    }

    internal static IReadOnlyDictionary<string, string> BuildJavaScriptRuleSelectorsForTests(
        IReadOnlyList<AnalysisPolicyRule> rules) {
        var selectors = BuildJavaScriptRuleSelectors(rules);
        return selectors.ToDictionary(
            static selector => selector.ToolRuleId,
            static selector => selector.Severity,
            StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> BuildPythonSelectedRuleIdsForTests(IReadOnlyList<AnalysisPolicyRule> rules) {
        return BuildPythonSelectedRuleIds(rules);
    }

    internal static bool IsUnsupportedRuffOutputFileOptionForTests(int exitCode, string stdOut, string stdErr) {
        return IsUnsupportedRuffOutputFileOption(new CommandResult(exitCode, stdOut, stdErr));
    }

    internal static string BuildExternalRunnerFailureMessageForTests(
        string languageLabel,
        string command,
        string optionName,
        int exitCode,
        string stdOut,
        string stdErr) {
        return BuildExternalRunnerFailureMessage(languageLabel, command, optionName,
            new CommandResult(exitCode, stdOut, stdErr));
    }

    internal static bool WorkspaceContainsAnySourceFileForTests(string workspace, params string[] extensions) {
        return WorkspaceContainsAnySourceFile(workspace, extensions);
    }

    internal static (bool Found, int SkippedEnumerations) WorkspaceContainsAnySourceFileWithDiagnosticsForTests(
        string workspace,
        params string[] extensions) {
        var found = WorkspaceContainsAnySourceFile(workspace, out var skippedEnumerations, extensions);
        return (found, skippedEnumerations);
    }

    internal static (IReadOnlyList<string> Extensions, int SkippedEnumerations, bool ScanLimitReached, int MaxScannedFiles)
        DiscoverWorkspaceSourceInventoryForTests(
        string workspace) {
        var inventory = DiscoverWorkspaceSourceInventory(workspace);
        if (inventory is null) {
            return (Array.Empty<string>(), 0, false, 0);
        }

        var extensions = inventory.Extensions
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (extensions, inventory.SkippedEnumerations, inventory.ScanLimitReached, inventory.MaxScannedFiles);
    }

    internal static (bool Found, int SkippedEnumerations, bool UsedDirectFallback, IReadOnlyList<string> Warnings)
        TryDetectSourceFilesWithSharedInventoryForTests(string workspace, string languageLabel, params string[] extensions) {
        var inventory = DiscoverWorkspaceSourceInventory(workspace);
        var warnings = new List<string>();
        var found = TryDetectSourceFiles(
            workspace,
            inventory,
            languageLabel,
            warnings,
            out var skippedEnumerations,
            out var usedDirectFallback,
            extensions);
        return (found, skippedEnumerations, usedDirectFallback, warnings);
    }

    internal static IReadOnlyList<string> BuildPowerShellRunnerArgsForTests(
        string tempScript,
        string workspace,
        string findingsPath,
        string settingsPath,
        bool strict) {
        return BuildPowerShellRunnerArgs(tempScript, workspace, findingsPath, settingsPath, strict);
    }

    internal static bool IsExpectedProcessExecutionExceptionForTests(Exception ex) {
        return IsExpectedProcessExecutionException(ex);
    }

    internal static bool IsPathExcludedByConfiguredPathsForTests(string relativePath, params string[] excludedPaths) {
        var normalizedExcludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in excludedPaths ?? Array.Empty<string>()) {
            var normalized = NormalizeExcludedPathTagValue(path);
            if (!string.IsNullOrWhiteSpace(normalized)) {
                normalizedExcludedPaths.Add(normalized);
            }
        }
        return IsPathExcludedByConfiguredPaths(relativePath, normalizedExcludedPaths);
    }
}
