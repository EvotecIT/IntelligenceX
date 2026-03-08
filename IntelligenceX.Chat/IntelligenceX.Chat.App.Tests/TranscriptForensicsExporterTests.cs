using System;
using System.Collections.Generic;
using System.IO;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Ensures transcript forensic export captures raw, normalized, and rendered views without mutating source text.
/// </summary>
public sealed class TranscriptForensicsExporterTests {
    /// <summary>
    /// Verifies the forensic bundle keeps live and persisted snapshots distinct while recording normalized markdown output.
    /// </summary>
    [Fact]
    public void Build_CapturesLiveAndPersistedSnapshotsSeparately() {
        var options = ChatMarkdownOptions.Create();
        var now = new DateTime(2026, 3, 8, 18, 6, 28, DateTimeKind.Local);
        var liveMessages = new List<(string Role, string Text, DateTime Time, string? Model)> {
            ("Assistant", """
            ### Forest Replication Status
            - Overall health ****healthy****
            """, now, "gpt-5.3-codex")
        };
        var persistedMessages = new List<ChatMessageState> {
            new() {
                Role = "Assistant",
                Text = """
                ### Forest Replication Status
                - Overall health ****healthy****
                """,
                TimeUtc = now.ToUniversalTime(),
                Model = "gpt-5.3-codex"
            }
        };

        var bundle = TranscriptForensicsExporter.Build(
            "default",
            @"C:\Users\me\AppData\Local\IntelligenceX.Chat\app-state.db",
            "HH:mm:ss",
            options,
            "conv-1",
            "Forest",
            "thread-1",
            liveMessages,
            persistedMessages);

        Assert.Equal("conv-1", bundle.ConversationId);
        Assert.Equal("Forest", bundle.ConversationTitle);
        Assert.NotNull(bundle.Live);
        Assert.NotNull(bundle.Persisted);
        Assert.Single(bundle.Live.Messages);
        Assert.Single(bundle.Persisted!.Messages);
        Assert.Contains("****healthy****", bundle.Live.Messages[0].RawText, StringComparison.Ordinal);
        Assert.Contains("**healthy**", bundle.Live.Messages[0].NormalizedText, StringComparison.Ordinal);
        Assert.True(bundle.Live.Messages[0].WasNormalized);
        Assert.Contains("Forest Replication Status", bundle.Live.RenderedTranscriptHtml, StringComparison.Ordinal);
        Assert.Contains("OfficeIMO.MarkdownRenderer", bundle.Renderer.MarkdownRendererAssembly, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies forensic export writes an indented JSON document with the expected raw and rendered transcript fields.
    /// </summary>
    [Fact]
    public void Export_WritesIndentedJsonBundle() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 3, 8, 18, 8, 40, DateTimeKind.Local);
        var bundle = TranscriptForensicsExporter.Build(
            "default",
            @"C:\db\app-state.db",
            "HH:mm:ss",
            options,
            "conv-2",
            "Diagnostics",
            null,
            new List<(string Role, string Text, DateTime Time, string? Model)> {
                ("Assistant", "Hello world", now, "gpt-5.3-codex")
            },
            persistedMessages: null);

        var root = CreateTempDirectory();
        try {
            var outputPath = Path.Combine(root, "forensics.json");
            TranscriptForensicsExporter.Export(outputPath, bundle);

            Assert.True(File.Exists(outputPath));
            var json = File.ReadAllText(outputPath);
            Assert.Contains("\"conversationId\": \"conv-2\"", json, StringComparison.Ordinal);
            Assert.Contains("\"rawText\": \"Hello world\"", json, StringComparison.Ordinal);
            Assert.Contains("\"renderedTranscriptHtml\":", json, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
