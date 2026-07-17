using System;
using System.IO;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards native ChartForgeX visual materialization for DOCX transcript export.
/// </summary>
public sealed class NativeTranscriptVisualExportMaterializerTests {
    /// <summary>
    /// Ensures a Mermaid fence becomes a temporary PNG Markdown image with an allowed source directory.
    /// </summary>
    [Fact]
    public void TryMaterialize_ReplacesMermaidFenceWithTemporaryImage() {
        const string markdown = """
            Before.

            ```mermaid {title="Directory flow"}
            flowchart LR
              A[User] --> B[Directory]
            ```

            After.
            """;

        string directory;
        using (var materialization = NativeTranscriptVisualExportMaterializer.TryMaterialize(markdown)) {
            var result = Assert.IsType<NativeTranscriptVisualExportMaterialization>(materialization);
            directory = Assert.Single(result.AllowedImageDirectories);
            Assert.DoesNotContain("```mermaid", result.Markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("![", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("Before.", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("After.", result.Markdown, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory, "*.png"));
        }

        Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// Ensures ordinary Markdown avoids temporary-directory work.
    /// </summary>
    [Fact]
    public void TryMaterialize_ReturnsNullWithoutVisualFences() {
        Assert.Null(NativeTranscriptVisualExportMaterializer.TryMaterialize("# Plain transcript"));
    }
}
