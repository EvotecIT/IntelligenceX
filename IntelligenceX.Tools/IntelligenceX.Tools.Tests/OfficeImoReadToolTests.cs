using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.OfficeIMO;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class OfficeImoReadToolTests {
    [Fact]
    public async Task OfficeImoRead_WhenExtensionsOmitted_DefaultsIncludePdf() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            var pdfPath = Path.Combine(tempRoot, "sample.pdf");
            var txtPath = Path.Combine(tempRoot, "ignored.txt");
            File.WriteAllText(pdfPath, "not-a-real-pdf");
            File.WriteAllText(txtPath, "plain text");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", tempRoot),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());

            var files = root.GetProperty("files").EnumerateArray()
                .Select(static x => x.GetString())
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => Path.GetFileName(x!))
                .ToArray();

            Assert.Contains("sample.pdf", files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("ignored.txt", files, StringComparer.OrdinalIgnoreCase);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task OfficeImoRead_WhenOutputModeDocuments_ReturnsDocumentContractShape() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            var markdownPath = Path.Combine(tempRoot, "knowledge.md");
            File.WriteAllText(markdownPath, "# SOP\n\nLine 1");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", markdownPath)
                    .Add("output_mode", "documents")
                    .Add("include_document_chunks", false),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("documents", root.GetProperty("output_mode").GetString());
            Assert.True(root.TryGetProperty("next_actions", out var nextActions));
            Assert.True(root.TryGetProperty("cursor", out var cursor));
            Assert.True(root.TryGetProperty("resume_token", out var resumeToken));
            Assert.True(root.TryGetProperty("handoff", out var handoff));
            Assert.True(root.TryGetProperty("confidence", out var confidence));
            Assert.True(nextActions.ValueKind == global::System.Text.Json.JsonValueKind.Array);
            Assert.Equal("officeimo_read", nextActions[0].GetProperty("tool").GetString());
            Assert.Equal("officeimo_read_handoff", handoff.GetProperty("contract").GetString());
            Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
            Assert.InRange(confidence.GetDouble(), 0d, 1d);
            Assert.True(root.TryGetProperty("documents", out var documents));
            Assert.True(root.TryGetProperty("chunks", out var chunks));
            Assert.Equal(0, chunks.GetArrayLength());
            Assert.Equal(0, root.GetProperty("chunks_returned").GetInt32());
            Assert.Equal(0, root.GetProperty("token_estimate_returned").GetInt32());

            var files = root.GetProperty("files").EnumerateArray()
                .Select(static x => x.GetString())
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => Path.GetFileName(x!))
                .ToArray();

            Assert.Contains("knowledge.md", files, StringComparer.OrdinalIgnoreCase);

            if (documents.GetArrayLength() > 0) {
                var first = documents[0];
                Assert.Equal("knowledge.md", Path.GetFileName(first.GetProperty("path").GetString() ?? string.Empty));
                Assert.Equal(0, first.GetProperty("chunks_returned").GetInt32());
                Assert.Equal(0, first.GetProperty("token_estimate_returned").GetInt32());
                Assert.True(first.GetProperty("chunks_produced").GetInt32() >= 1);
            }
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task OfficeImoRead_WhenOutputModeInvalid_ReturnsInvalidArgument() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            var markdownPath = Path.Combine(tempRoot, "knowledge.md");
            File.WriteAllText(markdownPath, "# SOP");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", markdownPath)
                    .Add("output_mode", "nope"),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task OfficeImoRead_WhenOutputModeBoth_IncludesBothArraysAndCoherentCounters() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            var markdownPath = Path.Combine(tempRoot, "knowledge.md");
            File.WriteAllText(markdownPath, "# Top\n\nParagraph 1.\n\n## Child\n\nParagraph 2.");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", markdownPath)
                    .Add("output_mode", "both")
                    .Add("include_document_chunks", true),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("both", root.GetProperty("output_mode").GetString());

            var flatChunks = root.GetProperty("chunks");
            var documents = root.GetProperty("documents");
            Assert.True(flatChunks.GetArrayLength() > 0);
            Assert.True(documents.GetArrayLength() > 0);

            var documentChunkCount = 0;
            var documentTokenTotal = 0;
            foreach (var source in documents.EnumerateArray()) {
                var sourceChunks = source.GetProperty("chunks");
                var sourceChunkCount = sourceChunks.GetArrayLength();
                documentChunkCount += sourceChunkCount;
                Assert.Equal(sourceChunkCount, source.GetProperty("chunks_returned").GetInt32());

                var sourceTokenTotal = 0;
                foreach (var c in sourceChunks.EnumerateArray()) {
                    if (c.TryGetProperty("token_estimate", out var token) && token.ValueKind == global::System.Text.Json.JsonValueKind.Number) {
                        sourceTokenTotal += token.GetInt32();
                    }
                }
                Assert.Equal(sourceTokenTotal, source.GetProperty("token_estimate_returned").GetInt32());
                documentTokenTotal += sourceTokenTotal;
            }

            var flatTokenTotal = 0;
            foreach (var c in flatChunks.EnumerateArray()) {
                Assert.True(c.TryGetProperty("source_id", out _));
                Assert.True(c.TryGetProperty("source_hash", out _));
                Assert.True(c.TryGetProperty("chunk_hash", out _));
                if (c.TryGetProperty("token_estimate", out var token) && token.ValueKind == global::System.Text.Json.JsonValueKind.Number) {
                    flatTokenTotal += token.GetInt32();
                }
            }

            var expectedChunkObjectsInPayload = flatChunks.GetArrayLength() + documentChunkCount;
            var expectedTokenEstimateInPayload = flatTokenTotal + documentTokenTotal;

            Assert.Equal(expectedChunkObjectsInPayload, root.GetProperty("chunks_returned").GetInt32());
            Assert.Equal(expectedTokenEstimateInPayload, root.GetProperty("token_estimate_returned").GetInt32());
            Assert.True(root.GetProperty("chunks_produced").GetInt32() >= flatChunks.GetArrayLength());
            Assert.True(root.GetProperty("files_scanned").GetInt32() >= 1);
            Assert.True(root.GetProperty("files_parsed").GetInt32() >= 1);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }
}
