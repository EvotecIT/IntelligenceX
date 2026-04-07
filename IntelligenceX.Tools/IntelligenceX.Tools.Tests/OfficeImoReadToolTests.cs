using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.OfficeIMO;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class OfficeImoReadToolTests {
    [Fact]
    public void DirectoryBuildProps_PinsCurrentPublishedOfficeImoReaderPackageVersion() {
        var propsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Directory.Build.props"));
        var props = LoadMsBuildProperties(propsPath);

        Assert.Equal("0.1.16", props["OfficeImoReaderNuGetVersion"]);
    }

    [Fact]
    public void OfficeImoChunkTable_WhenColumnsAndRowsAssignedMutableLists_AreCopiedAndReadOnly() {
        var columns = new List<string> { "name", "value" };
        var row = new List<string> { "alpha", "1" };
        var rows = new List<IReadOnlyList<string>> { row };

        var table = new OfficeImoChunkTable {
            Columns = columns,
            Rows = rows
        };

        columns.Add("extra");
        row.Add("2");
        rows.Add(new List<string> { "beta", "3" });

        Assert.Equal(new[] { "name", "value" }, table.Columns);
        Assert.Single(table.Rows);
        Assert.Equal(new[] { "alpha", "1" }, table.Rows[0]);

        var columnsList = Assert.IsAssignableFrom<IList<string>>(table.Columns);
        Assert.Throws<NotSupportedException>(() => columnsList.Add("x"));
        var rowsList = Assert.IsAssignableFrom<IList<IReadOnlyList<string>>>(table.Rows);
        Assert.Throws<NotSupportedException>(() => rowsList.Add(Array.Empty<string>()));
        var rowList = Assert.IsAssignableFrom<IList<string>>(table.Rows[0]);
        Assert.Throws<NotSupportedException>(() => rowList.Add("x"));
    }

    [Fact]
    public void OfficeImoChunkTable_WhenRowsContainNull_PreservesRowShapeWithEmptyRows() {
        IReadOnlyList<string>?[] rows = {
            new[] { "alpha", "1" },
            null
        };

        var table = new OfficeImoChunkTable {
            Columns = new[] { "name", "value" },
            Rows = rows!
        };

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "alpha", "1" }, table.Rows[0]);
        Assert.Empty(table.Rows[1]);
    }

    [Fact]
    public void OfficeImoChunk_WhenTablesAndWarningsAssignedMutableLists_AreCopiedAndReadOnly() {
        var table = new OfficeImoChunkTable { Columns = new[] { "name" }, Rows = new[] { new[] { "alpha" } } };
        var tables = new List<OfficeImoChunkTable> { table };
        var warnings = new List<string> { "one" };

        var chunk = new OfficeImoChunk {
            Tables = tables,
            Warnings = warnings
        };

        tables.Add(new OfficeImoChunkTable { Columns = new[] { "value" }, Rows = new[] { new[] { "1" } } });
        warnings.Add("two");

        Assert.NotNull(chunk.Tables);
        Assert.NotNull(chunk.Warnings);
        Assert.Single(chunk.Tables!);
        Assert.Single(chunk.Warnings!);
        Assert.Equal("one", chunk.Warnings![0]);

        var tablesList = Assert.IsAssignableFrom<IList<OfficeImoChunkTable>>(chunk.Tables!);
        Assert.Throws<NotSupportedException>(() => tablesList.Add(table));
        var warningsList = Assert.IsAssignableFrom<IList<string>>(chunk.Warnings!);
        Assert.Throws<NotSupportedException>(() => warningsList.Add("x"));
    }

    [Fact]
    public void OfficeImoChunk_WhenTablesAndWarningsAssignedNull_RemainNull() {
        var chunk = new OfficeImoChunk {
            Tables = null,
            Warnings = null
        };

        Assert.Null(chunk.Tables);
        Assert.Null(chunk.Warnings);
    }

    [Fact]
    public void OfficeImoReadResult_WhenNextActionsAssignedNullOrEmpty_UsesEmptyList() {
        var result = new OfficeImoReadResult {
            NextActions = null!
        };

        Assert.Empty(result.NextActions);

        result.NextActions = Array.Empty<ToolNextActionModel>();
        Assert.Empty(result.NextActions);
    }

    private static IReadOnlyDictionary<string, string> LoadMsBuildProperties(string propsPath) {
        var document = XDocument.Load(propsPath);
        return document.Root?
            .Elements()
            .Where(static element => element.Name.LocalName == "PropertyGroup")
            .Elements()
            .Where(static element => !string.IsNullOrWhiteSpace(element.Name.LocalName))
            .GroupBy(static element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Value.Trim(), StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    [Fact]
    public void OfficeImoReadResult_WhenNextActionsAssignedMutableList_IsCopiedAndReadOnly() {
        var source = new List<ToolNextActionModel> {
            new() { Tool = "officeimo_read", Reason = "follow-up", Optional = true }
        };

        var result = new OfficeImoReadResult {
            NextActions = source
        };
        source.Add(new ToolNextActionModel { Tool = "ad_scope_discovery", Reason = "extra", Optional = true });

        Assert.Single(result.NextActions);
        Assert.Equal("officeimo_read", result.NextActions[0].Tool);

        var list = Assert.IsAssignableFrom<IList<ToolNextActionModel>>(result.NextActions);
        Assert.Throws<NotSupportedException>(() => list.Add(new ToolNextActionModel { Tool = "x", Reason = "y" }));
    }

    [Fact]
    public void OfficeImoReadResult_WhenHandoffAssignedNullOrEmpty_UsesSharedEmptyMap() {
        var result = new OfficeImoReadResult {
            Handoff = null!
        };

        Assert.Same(ToolChainingHints.EmptyMap, result.Handoff);

        result.Handoff = new Dictionary<string, string>(StringComparer.Ordinal);
        Assert.Same(ToolChainingHints.EmptyMap, result.Handoff);

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(result.Handoff);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void OfficeImoReadResult_WhenCheckpointAssignedNullOrEmpty_UsesSharedEmptyMap() {
        var result = new OfficeImoReadResult {
            Checkpoint = null!
        };

        Assert.Same(ToolChainingHints.EmptyMap, result.Checkpoint);

        result.Checkpoint = new Dictionary<string, string>(StringComparer.Ordinal);
        Assert.Same(ToolChainingHints.EmptyMap, result.Checkpoint);

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(result.Checkpoint);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void OfficeImoReadResult_WhenHandoffAssignedMutableMap_IsNormalizedAndReadOnly() {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [" contract "] = "officeimo_read_handoff",
            [" "] = "ignored"
        };

        var result = new OfficeImoReadResult {
            Handoff = source
        };
        source["new_key"] = "new_value";

        Assert.True(result.Handoff.TryGetValue("contract", out var contract));
        Assert.Equal("officeimo_read_handoff", contract);
        Assert.False(result.Handoff.ContainsKey("new_key"));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(result.Handoff);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void OfficeImoReadResult_WhenCheckpointAssignedMutableMap_IsNormalizedAndReadOnly() {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [" phase "] = "collect",
            [" "] = "ignored"
        };

        var result = new OfficeImoReadResult {
            Checkpoint = source
        };
        source["new_key"] = "new_value";

        Assert.True(result.Checkpoint.TryGetValue("phase", out var phase));
        Assert.Equal("collect", phase);
        Assert.False(result.Checkpoint.ContainsKey("new_key"));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(result.Checkpoint);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void OfficeImoReadResult_WhenTrimmedKeysCollide_LastWriteWins() {
        var source = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["contract"] = "first",
            [" contract "] = "second"
        };

        var result = new OfficeImoReadResult {
            Handoff = source
        };

        Assert.Single(result.Handoff);
        Assert.Equal("second", result.Handoff["contract"]);
    }

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
            Assert.True(root.TryGetProperty("flow_id", out var flowId));
            Assert.True(root.TryGetProperty("step_id", out var stepId));
            Assert.True(root.TryGetProperty("checkpoint", out var checkpoint));
            Assert.True(root.TryGetProperty("handoff", out var handoff));
            Assert.True(root.TryGetProperty("confidence", out var confidence));
            Assert.True(nextActions.ValueKind == global::System.Text.Json.JsonValueKind.Array);
            Assert.Equal("officeimo_read", nextActions[0].GetProperty("tool").GetString());
            Assert.Equal("officeimo_read_handoff", handoff.GetProperty("contract").GetString());
            Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(flowId.GetString()));
            Assert.Equal("read_result", stepId.GetString());
            Assert.True(checkpoint.TryGetProperty("files", out _));
            Assert.InRange(confidence.GetDouble(), 0d, 1d);
            Assert.True(root.TryGetProperty("documents", out var documents));
            Assert.True(root.TryGetProperty("chunks", out var chunks));
            Assert.Single(documents.EnumerateArray());
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
                Assert.Equal(0, first.GetProperty("chunks").GetArrayLength());
                Assert.Equal(0, first.GetProperty("chunks_returned").GetInt32());
                Assert.Equal(0, first.GetProperty("token_estimate_returned").GetInt32());
                Assert.True(first.GetProperty("chunks_produced").GetInt32() >= 1);
                Assert.Equal(first.GetProperty("chunks_produced").GetInt32(), root.GetProperty("chunks_produced").GetInt32());
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

    [Fact]
    public async Task OfficeImoRead_WhenMaxChunksReached_UsesUpstreamBudgetingAndMarksResultTruncated() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            File.WriteAllText(Path.Combine(tempRoot, "a.md"), "# A\n\nAlpha");
            File.WriteAllText(Path.Combine(tempRoot, "b.md"), "# B\n\nBeta");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", tempRoot)
                    .Add("output_mode", "both")
                    .Add("include_document_chunks", true)
                    .Add("max_chunks", 1),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("truncated").GetBoolean());
            Assert.Equal(2, root.GetProperty("documents").GetArrayLength());
            Assert.Equal(1, root.GetProperty("chunks").GetArrayLength());
            Assert.Equal(2, root.GetProperty("chunks_returned").GetInt32());

            var documents = root.GetProperty("documents").EnumerateArray().ToArray();
            Assert.Equal(1, documents[0].GetProperty("chunks").GetArrayLength());
            Assert.Equal(0, documents[1].GetProperty("chunks").GetArrayLength());

            var warnings = root.GetProperty("warnings").EnumerateArray()
                .Select(static x => x.GetString())
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            Assert.Contains(warnings, static warning => warning!.Contains("MaxReturnedChunks", StringComparison.OrdinalIgnoreCase));
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task OfficeImoRead_WhenMaxChunksReached_ReportsTruncationAndKeepsSharedBudgetSemantics() {
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
                    .Add("include_document_chunks", true)
                    .Add("max_chunks", 1),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("truncated").GetBoolean());
            Assert.Equal(1, root.GetProperty("chunks").GetArrayLength());
            Assert.Single(root.GetProperty("documents").EnumerateArray());
            Assert.Equal(1, root.GetProperty("documents")[0].GetProperty("chunks").GetArrayLength());
            Assert.Equal(2, root.GetProperty("chunks_returned").GetInt32());
            Assert.True(root.GetProperty("chunks_produced").GetInt32() >= 2);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void OfficeImoRead_ProjectDocuments_PromotesSourceWarningsToTopLevelWarnings() {
        var sourceType = Type.GetType("OfficeIMO.Reader.ReaderSourceDocument, OfficeIMO.Reader", throwOnError: true)!;
        var chunkType = Type.GetType("OfficeIMO.Reader.ReaderChunk, OfficeIMO.Reader", throwOnError: true)!;
        var source = Activator.CreateInstance(sourceType)!;
        sourceType.GetProperty("Path")!.SetValue(source, "example.md");
        sourceType.GetProperty("Parsed")!.SetValue(source, true);
        sourceType.GetProperty("Warnings")!.SetValue(source, new[] { "source warning" });
        sourceType.GetProperty("Chunks")!.SetValue(source, Array.CreateInstance(chunkType, 0));

        var listType = typeof(List<>).MakeGenericType(sourceType);
        var sources = Activator.CreateInstance(listType)!;
        listType.GetMethod("Add")!.Invoke(sources, new[] { source });

        var result = new OfficeImoReadResult();
        var method = typeof(OfficeImoReadTool)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate =>
                string.Equals(candidate.Name, "ProjectDocuments", StringComparison.Ordinal)
                && candidate.GetParameters().Length == 5
                && candidate.GetParameters()[0].ParameterType.IsGenericType);

        method.Invoke(null, new object[] { sources, false, true, false, result });

        Assert.Contains("source warning", result.Warnings, StringComparer.OrdinalIgnoreCase);
        Assert.Single(result.Documents);
        Assert.Contains("source warning", result.Documents[0].Warnings, StringComparer.OrdinalIgnoreCase);
    }
}
