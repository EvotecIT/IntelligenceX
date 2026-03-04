using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.InstalledApplications;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists installed applications from registry uninstall keys (read-only, capped).
/// </summary>
public sealed class SystemInstalledApplicationsTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_installed_applications",
        "List installed applications (name/version/publisher/install date) from registry uninstall keys (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive filter against application name.")),
                ("publisher_contains", ToolSchema.String("Optional case-insensitive filter against publisher.")),
                ("max_results", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SystemInstalledApplicationsResult(
        string ComputerName,
        string? NameContains,
        string? PublisherContains,
        int Scanned,
        bool Truncated,
        IReadOnlyList<InstalledApplicationInfo> Applications);

    private sealed record InstalledApplicationsRequest(
        string? ComputerName,
        string? NameContains,
        string? PublisherContains,
        int MaxResults);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInstalledApplicationsTool"/> class.
    /// </summary>
    public SystemInstalledApplicationsTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<InstalledApplicationsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => ToolRequestBindingResult<InstalledApplicationsRequest>.Success(new InstalledApplicationsRequest(
            ComputerName: reader.OptionalString("computer_name"),
            NameContains: reader.OptionalString("name_contains"),
            PublisherContains: reader.OptionalString("publisher_contains"),
            MaxResults: ResolveMaxResults(arguments))));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<InstalledApplicationsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var windowsError = ValidateWindowsSupport("system_installed_applications");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var target = ResolveTargetComputerName(request.ComputerName);

        IEnumerable<InstalledApplicationInfo> query;
        try {
            query = InstalledApplications.Query(request.ComputerName);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Installed applications query failed."));
        }

        var filtered = query
            .Where(x => string.IsNullOrWhiteSpace(request.NameContains)
                || x.Name?.Contains(request.NameContains, StringComparison.OrdinalIgnoreCase) == true)
            .Where(x => string.IsNullOrWhiteSpace(request.PublisherContains)
                || x.Publisher?.Contains(request.PublisherContains, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        var rows = CapRows(filtered, request.MaxResults, out var scanned, out var truncated);

        var result = new SystemInstalledApplicationsResult(
            ComputerName: target,
            NameContains: request.NameContains,
            PublisherContains: request.PublisherContains,
            Scanned: scanned,
            Truncated: truncated,
            Applications: rows);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "applications_view",
            title: "Installed applications (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, target);
                AddMaxResultsMeta(meta, request.MaxResults);
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (!string.IsNullOrWhiteSpace(request.PublisherContains)) {
                    meta.Add("publisher_contains", request.PublisherContains);
                }
            });
        return Task.FromResult(response);
    }
}
