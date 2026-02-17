using System;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for shell asset composition.
/// </summary>
public sealed class UiShellAssetsTests {
    private static string UiDirectory => Path.Combine(AppContext.BaseDirectory, "Ui");

    /// <summary>
    /// Ensures the tool-source helpers are present in generated shell script.
    /// Missing helpers break tools rendering at runtime.
    /// </summary>
    [Fact]
    public void Load_IncludesPackSourceHelpers_ForToolsRendering() {
        var html = UiShellAssets.Load();

        Assert.Contains("function packSourceKind(", html, StringComparison.Ordinal);
        Assert.Contains("function packSourceLabel(", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures split JavaScript files are explicitly tracked by manifest,
    /// so adding/renaming files cannot silently change runtime composition.
    /// </summary>
    [Fact]
    public void JavaScriptManifest_MatchesSplitFilesInOutput() {
        var manifest = UiShellAssets.JavaScriptManifest.ToArray();
        var actual = Directory.EnumerateFiles(UiDirectory, "Shell.*.js", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(manifest.Length, actual.Length);
        foreach (var file in manifest) {
            Assert.Contains(file, actual, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Ensures split CSS files are explicitly tracked by manifest.
    /// </summary>
    [Fact]
    public void CssManifest_MatchesSplitFilesInOutput() {
        var manifest = UiShellAssets.StyleManifest.ToArray();
        var actual = Directory.EnumerateFiles(UiDirectory, "Shell.*.css", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(manifest.Length, actual.Length);
        foreach (var file in manifest) {
            Assert.Contains(file, actual, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Ensures composed HTML is not the fallback diagnostics page in normal test output.
    /// </summary>
    [Fact]
    public void Load_DoesNotReturnFallbackDiagnosticsPage() {
        var html = UiShellAssets.Load();
        Assert.DoesNotContain("UI shell assets are invalid", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures shell JavaScript chunks are emitted in manifest order.
    /// </summary>
    [Fact]
    public void Load_EmitsJavaScriptChunksInManifestOrder() {
        var html = UiShellAssets.Load();
        var previousIndex = -1;

        foreach (var file in UiShellAssets.JavaScriptManifest) {
            var marker = $"/* IXCHAT_PART:{file} */";
            var index = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing marker for {file}");
            Assert.True(index > previousIndex, $"Marker order invalid for {file}");
            previousIndex = index;
        }
    }

    /// <summary>
    /// Ensures autonomy review-loop controls propagate through the UI set_autonomy payload.
    /// </summary>
    [Fact]
    public void Load_IncludesAutonomyReviewLoopFieldsInSetAutonomyPayload() {
        var html = UiShellAssets.Load();

        Assert.Contains("post(\"set_autonomy\", {", html, StringComparison.Ordinal);
        Assert.Contains("planExecuteReviewLoop: (byId(\"optAutonomyPlanReview\").value || \"default\").trim()", html, StringComparison.Ordinal);
        Assert.Contains("maxReviewPasses: (byId(\"optAutonomyMaxReviewPasses\").value || \"\").trim()", html, StringComparison.Ordinal);
        Assert.Contains("modelHeartbeatSeconds: (byId(\"optAutonomyModelHeartbeat\").value || \"\").trim()", html, StringComparison.Ordinal);
    }
}
