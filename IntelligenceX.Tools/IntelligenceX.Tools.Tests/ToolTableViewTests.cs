using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolTableViewTests {
    [Fact]
    public void TryParse_ShouldRejectUnsupportedColumn() {
        var args = new JsonObject()
            .Add("columns", new JsonArray().Add("id").Add("nope"));

        var ok = ToolTableView.TryParse(
            arguments: args,
            allowedColumns: new[] { "id", "name" },
            maxTop: 100,
            request: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("unsupported", error ?? string.Empty);
    }

    [Fact]
    public void TryParse_ShouldResolveProjectionAliasesForColumnsAndSortBy() {
        var args = new JsonObject()
            .Add("columns", new JsonArray().Add("rule_name").Add("deprecated"))
            .Add("sort_by", "deprecated")
            .Add("sort_direction", "desc");

        var ok = ToolTableView.TryParse(
            arguments: args,
            allowedColumns: new[] { "rule_name", "is_deprecated", "scope" },
            maxTop: 100,
            request: out var request,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new[] { "rule_name", "is_deprecated" }, request.Columns);
        Assert.Equal("is_deprecated", request.SortBy);
        Assert.Equal(ToolTableSortDirection.Desc, request.SortDirection);
    }

    [Fact]
    public void TryParse_ShouldRejectAmbiguousSuffixAlias() {
        var args = new JsonObject()
            .Add("columns", new JsonArray().Add("status"));

        var ok = ToolTableView.TryParse(
            arguments: args,
            allowedColumns: new[] { "overall_status", "preflight_status", "rule_name" },
            maxTop: 100,
            request: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("unsupported", error ?? string.Empty);
    }

    [Fact]
    public void Apply_ShouldSelectSortAndTopRows() {
        var rows = new[] {
            new Row(2, "b", 2048),
            new Row(1, "a", 1024),
            new Row(3, "c", 3072)
        };

        var specs = new[] {
            new ToolTableColumnSpec<Row>(new ToolColumn("id", "ID", "int"), static x => x.Id),
            new ToolTableColumnSpec<Row>(new ToolColumn("name", "Name", "string"), static x => x.Name),
            new ToolTableColumnSpec<Row>(new ToolColumn("memory_usage", "Memory", "bytes"), static x => x.MemoryUsage)
        };

        var request = new ToolTableViewRequest {
            Columns = new[] { "name", "id" },
            SortBy = "id",
            SortDirection = ToolTableSortDirection.Desc,
            Top = 2
        };

        var result = ToolTableView.Apply(rows, request, specs, previewMaxRows: 20);

        Assert.Equal(2, result.Count);
        Assert.True(result.TruncatedByView);
        Assert.Collection(result.Columns,
            c => Assert.Equal("name", c.Key),
            c => Assert.Equal("id", c.Key));

        Assert.Equal(2, result.Rows.Count);
        var first = result.Rows[0].AsObject();
        Assert.NotNull(first);
        Assert.Equal("c", first!.GetString("name"));
        Assert.Equal(3, first.GetInt64("id"));

        Assert.Equal(2, result.PreviewRows.Count);
        Assert.Equal("c", result.PreviewRows[0][0]);
        Assert.Equal("3", result.PreviewRows[0][1]);
    }

    [Fact]
    public void AutoColumns_ShouldInferSnakeCaseAndPrimitiveTypes() {
        var specs = ToolAutoTableColumns.GetColumnSpecs<AutoRow>();
        var keys = ToolAutoTableColumns.GetColumnKeys<AutoRow>();

        Assert.Contains(specs, static x => x.Column.Key == "id" && x.Column.Type == "int");
        Assert.Contains(specs, static x => x.Column.Key == "display_name" && x.Column.Type == "string");
        Assert.Contains(specs, static x => x.Column.Key == "start_time_utc" && x.Column.Type == "datetime");
        Assert.Contains(specs, static x => x.Column.Key == "tags" && x.Column.Type == "array");
        Assert.Contains("id", keys);
        Assert.Contains("display_name", keys);
        Assert.Contains("start_time_utc", keys);
    }

    [Fact]
    public void EnvelopeAutoColumns_ShouldIncludeAvailableColumnsAndViewRows() {
        var ok = ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: new JsonObject().Add("columns", new JsonArray().Add("display_name")).Add("top", 1),
            model: new AutoModel {
                Items = new[] {
                    new AutoRow(1, "alpha", DateTime.UnixEpoch, new[] { "a" }),
                    new AutoRow(2, "beta", DateTime.UnixEpoch, new[] { "b" })
                }
            },
            sourceRows: new[] {
                new AutoRow(1, "alpha", DateTime.UnixEpoch, new[] { "a" }),
                new AutoRow(2, "beta", DateTime.UnixEpoch, new[] { "b" })
            },
            viewRowsPath: "items_view",
            title: "Items",
            maxTop: 100,
            baseTruncated: false,
            response: out var response,
            scanned: 2);

        Assert.True(ok);
        Assert.Contains("\"ok\":true", response);
        Assert.Contains("\"items_view\":[{\"display_name\":\"alpha\"}]", response);
        Assert.Contains("\"available_columns\":[", response);
        Assert.Contains("\"display_name\"", response);
    }

    [Fact]
    public void EnvelopeAutoColumns_ShouldReturnProjectionMetadataOnInvalidViewArguments() {
        var ok = ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: new JsonObject().Add("columns", new JsonArray().Add("missing_column")),
            model: new AutoModel {
                Items = new[] {
                    new AutoRow(1, "alpha", DateTime.UnixEpoch, new[] { "a" })
                }
            },
            sourceRows: new[] {
                new AutoRow(1, "alpha", DateTime.UnixEpoch, new[] { "a" })
            },
            viewRowsPath: "items_view",
            title: "Items",
            maxTop: 100,
            baseTruncated: false,
            response: out var response);

        Assert.False(ok);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("\"error_code\":\"invalid_argument\"", response);
        Assert.Contains("\"available_columns\":[", response);
        Assert.Contains("\"projection_arguments\":[\"columns\",\"sort_by\",\"sort_direction\",\"top\"]", response);
    }

    [Fact]
    public void DynamicEnvelope_ShouldProjectDictionaryRows() {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = new[] {
            new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["user"] = "alice",
                ["count"] = 2
            },
            new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["user"] = "bob",
                ["count"] = 1
            }
        };

        var ok = ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(
            arguments: new JsonObject()
                .Add("columns", new JsonArray().Add("user"))
                .Add("sort_by", "user")
                .Add("sort_direction", "asc")
                .Add("top", 1),
            model: new { Rows = rows },
            rows: rows,
            title: "Dynamic",
            rowsPath: "rows_view",
            baseTruncated: false,
            response: out var response,
            scanned: 2);

        Assert.True(ok);
        Assert.Contains("\"ok\":true", response);
        Assert.Contains("\"rows_view\":[{\"user\":\"alice\"}]", response);
        Assert.Contains("\"available_columns\":[\"user\",\"count\"]", response);
    }

    [Fact]
    public void DynamicEnvelope_ShouldRejectUnsupportedColumn() {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = new[] {
            new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["user"] = "alice"
            }
        };

        var ok = ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(
            arguments: new JsonObject().Add("columns", new JsonArray().Add("missing")),
            model: new { Rows = rows },
            rows: rows,
            title: "Dynamic",
            rowsPath: "rows_view",
            baseTruncated: false,
            response: out var response);

        Assert.False(ok);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("\"error_code\":\"invalid_argument\"", response);
    }

    private sealed record Row(int Id, string Name, long MemoryUsage);
    private sealed record AutoRow(int Id, string DisplayName, DateTime StartTimeUtc, IReadOnlyList<string> Tags);

    private sealed class AutoModel {
        public IReadOnlyList<AutoRow> Items { get; init; } = Array.Empty<AutoRow>();
    }
}
