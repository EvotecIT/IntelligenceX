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

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInstalledApplicationsTool"/> class.
    /// </summary>
    public SystemInstalledApplicationsTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows()) {
            return Task.FromResult(ToolResponse.Error("not_supported", "system_installed_applications is available only on Windows hosts."));
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName!;
        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var publisherContains = ToolArgs.GetOptionalTrimmed(arguments, "publisher_contains");
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        IEnumerable<InstalledApplicationInfo> query;
        try {
            query = InstalledApplications.Query(computerName);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Installed applications query failed."));
        }

        var filtered = query
            .Where(x => string.IsNullOrWhiteSpace(nameContains)
                || x.Name?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true)
            .Where(x => string.IsNullOrWhiteSpace(publisherContains)
                || x.Publisher?.Contains(publisherContains, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new SystemInstalledApplicationsResult(
            ComputerName: target,
            NameContains: nameContains,
            PublisherContains: publisherContains,
            Scanned: scanned,
            Truncated: truncated,
            Applications: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "applications_view",
            title: "Installed applications (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("computer_name", target);
                meta.Add("max_results", maxResults);
                if (!string.IsNullOrWhiteSpace(nameContains)) {
                    meta.Add("name_contains", nameContains);
                }
                if (!string.IsNullOrWhiteSpace(publisherContains)) {
                    meta.Add("publisher_contains", publisherContains);
                }
            });
        return Task.FromResult(response);
    }
}
