using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.DirectoryServices;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns AD schema version rows across forest domains with optional mismatch filtering (read-only).
/// </summary>
public sealed class AdSchemaVersionTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_schema_version",
        "Get Active Directory schema version information across domains and optionally show only mismatches (read-only).",
        ToolSchema.Object(
                ("mismatched_only", ToolSchema.Boolean("When true, returns only rows whose schema version differs from the first observed domain version.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdSchemaVersionResult(
        bool MismatchedOnly,
        int Scanned,
        bool Truncated,
        int? ReferenceVersion,
        int MismatchCount,
        IReadOnlyList<SchemaVersionInfo> Mismatches,
        IReadOnlyList<SchemaVersionInfo> Versions);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSchemaVersionTool"/> class.
    /// </summary>
    public AdSchemaVersionTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var mismatchedOnly = ToolArgs.GetBoolean(arguments, "mismatched_only", defaultValue: false);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryExecute(
                action: () => {
                    var reader = new SchemaVersionReader();
                    return reader.GetSchemaVersions().ToArray();
                },
                result: out SchemaVersionInfo[] versions,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Schema version query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var referenceVersion = versions.Length > 0 ? versions[0].Version : (int?)null;
        var mismatches = referenceVersion.HasValue
            ? versions.Where(x => x.Version != referenceVersion.Value).ToArray()
            : Array.Empty<SchemaVersionInfo>();

        IReadOnlyList<SchemaVersionInfo> selectedRows = mismatchedOnly ? mismatches : versions;
        var rows = CapRows(selectedRows, maxResults, out var scanned, out var truncated);

        var result = new AdSchemaVersionResult(
            MismatchedOnly: mismatchedOnly,
            Scanned: scanned,
            Truncated: truncated,
            ReferenceVersion: referenceVersion,
            MismatchCount: mismatches.Length,
            Mismatches: mismatches,
            Versions: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "versions_view",
            title: "Active Directory: Schema Versions (preview)",
            baseTruncated: truncated,
            scanned: scanned,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("mismatched_only", mismatchedOnly);
                meta.Add("mismatch_count", mismatches.Length);
                if (referenceVersion.HasValue) {
                    meta.Add("reference_version", referenceVersion.Value);
                }
            });
        return Task.FromResult(response);
    }
}

