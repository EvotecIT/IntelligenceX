using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Security.BitLocker;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists BitLocker volume status for local or remote host (read-only, capped).
/// </summary>
public sealed class SystemBitlockerStatusTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_bitlocker_status",
        "List BitLocker volume protection/conversion status for a host (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("protected_only", ToolSchema.Boolean("When true, return only volumes with protection status On.")),
                ("encrypted_only", ToolSchema.Boolean("When true, return only volumes with encryption percentage > 0.")),
                ("max_results", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SystemBitlockerStatusResult(
        string ComputerName,
        bool ProtectedOnly,
        bool EncryptedOnly,
        int Scanned,
        bool Truncated,
        IReadOnlyList<BitLockerVolumeInfo> Volumes);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBitlockerStatusTool"/> class.
    /// </summary>
    public SystemBitlockerStatusTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows()) {
            return Task.FromResult(ToolResponse.Error("not_supported", "system_bitlocker_status is available only on Windows hosts."));
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName!;
        var protectedOnly = ToolArgs.GetBoolean(arguments, "protected_only", defaultValue: false);
        var encryptedOnly = ToolArgs.GetBoolean(arguments, "encrypted_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        IEnumerable<BitLockerVolumeInfo> query;
        try {
            query = BitLocker.Get(computerName);
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"BitLocker status query failed: {ex.Message}"));
        }

        var filtered = query
            .Where(x => !protectedOnly || x.ProtectionStatus == BitLockerProtectionStatus.Protected)
            .Where(x => !encryptedOnly || x.EncryptionPercentage > 0)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<BitLockerVolumeInfo> rows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > rows.Count;

        var result = new SystemBitlockerStatusResult(
            ComputerName: target,
            ProtectedOnly: protectedOnly,
            EncryptedOnly: encryptedOnly,
            Scanned: scanned,
            Truncated: truncated,
            Volumes: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "volumes_view",
            title: "BitLocker status (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("computer_name", target);
                meta.Add("protected_only", protectedOnly);
                meta.Add("encrypted_only", encryptedOnly);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}
