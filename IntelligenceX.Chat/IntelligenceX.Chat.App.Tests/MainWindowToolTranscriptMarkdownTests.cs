using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for debug vs non-debug tool transcript markdown selection.
/// </summary>
public sealed class MainWindowToolTranscriptMarkdownTests {
    /// <summary>
    /// Ensures debug-mode transcript formatting keeps failure diagnostics.
    /// </summary>
    [Fact]
    public void BuildToolRunTranscriptMarkdown_UsesDebugFormatterWhenDebugModeEnabled() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c1",
                    Name = "diag"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c1",
                    Output = "{}",
                    Ok = false,
                    ErrorCode = "tool_timeout",
                    Error = "Tool timed out after 60s.",
                    Hints = new[] { "Retry later." },
                    SummaryMarkdown = "### Debug summary"
                }
            }
        };

        var markdown = MainWindow.BuildToolRunTranscriptMarkdown(tools, debugMode: true, _ => "Diagnostic Tool");

        Assert.Contains("**Tool outputs:**", markdown);
        Assert.Contains("failure descriptor:", markdown);
        Assert.Contains("Tool timed out after 60s.", markdown);
        Assert.Contains("Retry later.", markdown);
    }

    /// <summary>
    /// Ensures non-debug transcript formatting does not append tool transcript bubbles.
    /// </summary>
    [Fact]
    public void BuildToolRunTranscriptMarkdown_ReturnsEmptyWhenDebugModeDisabled() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c2",
                    Name = "chart_pack"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c2",
                    Output = "{\"chart\":{\"type\":\"line\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}",
                    Ok = false,
                    Error = "debug-only error"
                }
            }
        };

        var markdown = MainWindow.BuildToolRunTranscriptMarkdown(tools, debugMode: false, _ => "Chart Pack");

        Assert.Equal(string.Empty, markdown);
    }

    /// <summary>
    /// Ensures non-debug transcript formatting does not require a display-name resolver.
    /// </summary>
    [Fact]
    public void BuildToolRunTranscriptMarkdown_NonDebug_DoesNotRequireResolver() {
        var tools = new ToolRunDto {
            Calls = Array.Empty<ToolCallDto>(),
            Outputs = Array.Empty<ToolOutputDto>()
        };

        var markdown = MainWindow.BuildToolRunTranscriptMarkdown(tools, debugMode: false, null!);

        Assert.Equal(string.Empty, markdown);
    }
}
