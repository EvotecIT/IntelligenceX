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
        Assert.Contains("<!-- ix:model: gpt-5.3-codex -->", bundle.Live.RawTranscriptMarkdown, StringComparison.Ordinal);
        Assert.Contains("****healthy****", bundle.Live.RawTranscriptMarkdown, StringComparison.Ordinal);
        Assert.Contains("**healthy**", bundle.Live.NormalizedTranscriptMarkdown, StringComparison.Ordinal);
        Assert.Contains("Forest Replication Status", bundle.Live.RenderedTranscriptHtml, StringComparison.Ordinal);
        Assert.Contains("OfficeIMO.MarkdownRenderer", bundle.Renderer.MarkdownRendererAssembly, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies persisted timestamps loaded without a kind are treated as UTC before projection.
    /// </summary>
    [Fact]
    public void Build_TreatsPersistedUnspecifiedTimestampAsUtc() {
        var options = ChatMarkdownOptions.Create();
        var persistedTimestamp = new DateTime(2026, 3, 8, 18, 6, 28, DateTimeKind.Unspecified);
        var bundle = TranscriptForensicsExporter.Build(
            "default",
            @"C:\Users\me\AppData\Local\IntelligenceX.Chat\app-state.db",
            "HH:mm:ss",
            options,
            "conv-utc",
            "Forest",
            "thread-1",
            new List<(string Role, string Text, DateTime Time, string? Model)>(),
            new List<ChatMessageState> {
                new() {
                    Role = "Assistant",
                    Text = "One",
                    TimeUtc = persistedTimestamp,
                    Model = "gpt-5.3-codex"
                }
            });

        Assert.NotNull(bundle.Persisted);
        Assert.Single(bundle.Persisted!.Messages);
        Assert.Equal(DateTimeKind.Utc, bundle.Persisted.Messages[0].TimeUtc.Kind);
        Assert.Equal(DateTime.SpecifyKind(persistedTimestamp, DateTimeKind.Utc).Ticks, bundle.Persisted.Messages[0].TimeUtc.Ticks);
    }

    /// <summary>
    /// Verifies messages that normalize to blank are excluded consistently from all forensic transcript artifacts.
    /// </summary>
    [Fact]
    public void Build_SkipsMessagesThatNormalizeToBlankAcrossArtifacts() {
        var options = ChatMarkdownOptions.Create();
        var now = new DateTime(2026, 3, 8, 18, 10, 0, DateTimeKind.Local);
        var bundle = TranscriptForensicsExporter.Build(
            "default",
            @"C:\db\app-state.db",
            "HH:mm:ss",
            options,
            "conv-blank",
            "Diagnostics",
            null,
            new List<(string Role, string Text, DateTime Time, string? Model)> {
                ("Assistant", "\u200B", now, "gpt-5.3-codex")
            },
            persistedMessages: null);

        Assert.Empty(bundle.Live.Messages);
        Assert.Equal(0, bundle.Live.MessageCount);
        Assert.Equal(string.Empty, bundle.Live.RawTranscriptMarkdown);
        Assert.Equal(string.Empty, bundle.Live.NormalizedTranscriptMarkdown);
        Assert.Equal(string.Empty, bundle.Live.RenderedTranscriptHtml);
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

    /// <summary>
    /// Verifies transcript forensics export keeps HTML-sensitive content escaped in the JSON payload.
    /// </summary>
    [Fact]
    public void Export_EscapesHtmlSensitiveContentInJson() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 3, 8, 18, 9, 40, DateTimeKind.Local);
        var bundle = TranscriptForensicsExporter.Build(
            "default",
            @"C:\db\app-state.db",
            "HH:mm:ss",
            options,
            "conv-escaped",
            "Diagnostics",
            null,
            new List<(string Role, string Text, DateTime Time, string? Model)> {
                ("Assistant", "<script>alert('xss')</script>", now, "gpt-5.3-codex")
            },
            persistedMessages: null);

        var root = CreateTempDirectory();
        try {
            var outputPath = Path.Combine(root, "forensics.json");
            TranscriptForensicsExporter.Export(outputPath, bundle);

            var json = File.ReadAllText(outputPath);
            Assert.Contains("\\u003Cscript\\u003Ealert(\\u0027xss\\u0027)\\u003C/script\\u003E", json, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert('xss')</script>", json, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }


    /// <summary>
    /// Verifies forensic export refuses weak title-plus-count matches to avoid attaching the wrong persisted conversation.
    /// </summary>
    [Fact]
    public void FindPersistedConversationState_ReturnsNullWhenOnlyTitleAndMessageCountMatch() {
        var state = new ChatAppState {
            Conversations = new List<ChatConversationState> {
                new() {
                    Id = "persisted-1",
                    Title = "Forest",
                    Messages = new List<ChatMessageState> {
                        new() { Role = "Assistant", Text = "One" }
                    }
                }
            }
        };

        var match = MainWindow.FindPersistedConversationState(state, "live-1", null);

        Assert.Null(match);
    }

    /// <summary>
    /// Verifies thread id remains an acceptable persisted-conversation correlation key when conversation ids differ.
    /// </summary>
    [Fact]
    public void FindPersistedConversationState_MatchesByThreadIdWhenConversationIdDiffers() {
        var state = new ChatAppState {
            Conversations = new List<ChatConversationState> {
                new() {
                    Id = "persisted-1",
                    Title = "Forest",
                    ThreadId = "thread-42",
                    Messages = new List<ChatMessageState> {
                        new() { Role = "Assistant", Text = "One" }
                    }
                }
            }
        };

        var match = MainWindow.FindPersistedConversationState(state, "live-1", "thread-42");

        Assert.NotNull(match);
        Assert.Equal("persisted-1", match!.Id);
    }

    /// <summary>
    /// Verifies thread id matching remains case-sensitive to avoid correlating the wrong persisted conversation.
    /// </summary>
    [Fact]
    public void FindPersistedConversationState_DoesNotMatchThreadIdWithDifferentCase() {
        var state = new ChatAppState {
            Conversations = new List<ChatConversationState> {
                new() {
                    Id = "persisted-1",
                    ThreadId = "Thread-42",
                    Messages = new List<ChatMessageState> {
                        new() { Role = "Assistant", Text = "One" }
                    }
                }
            }
        };

        var match = MainWindow.FindPersistedConversationState(state, "live-1", "thread-42");

        Assert.Null(match);
    }

    /// <summary>
    /// Verifies ambiguous thread id matches do not attach an arbitrary persisted conversation.
    /// </summary>
    [Fact]
    public void FindPersistedConversationState_ReturnsNullWhenThreadIdMatchesMultipleConversations() {
        var state = new ChatAppState {
            Conversations = new List<ChatConversationState> {
                new() {
                    Id = "persisted-1",
                    ThreadId = "thread-42",
                    Messages = new List<ChatMessageState> {
                        new() { Role = "Assistant", Text = "One" }
                    }
                },
                new() {
                    Id = "persisted-2",
                    ThreadId = "thread-42",
                    Messages = new List<ChatMessageState> {
                        new() { Role = "Assistant", Text = "Two" }
                    }
                }
            }
        };

        var match = MainWindow.FindPersistedConversationState(state, "live-1", "thread-42");

        Assert.Null(match);
    }

    /// <summary>
    /// Verifies transcript forensics output paths normalize the extension and create the target directory.
    /// </summary>
    [Fact]
    public void ResolveTranscriptForensicsOutputPath_NormalizesExtensionAndCreatesDirectory() {
        var root = CreateTempDirectory();
        try {
            var selectedPath = Path.Combine(root, "nested", "bundle.trace");

            var resolvedPath = MainWindow.ResolveTranscriptForensicsOutputPath(selectedPath);

            Assert.Equal(Path.Combine(root, "nested", "bundle.json"), resolvedPath, ignoreCase: true);
            Assert.True(Directory.Exists(Path.GetDirectoryName(resolvedPath)));
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
