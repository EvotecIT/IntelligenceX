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

        var windowsError = ValidateWindowsSupport("system_bitlocker_status");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = ResolveTargetComputerName(computerName);
        var protectedOnly = ToolArgs.GetBoolean(arguments, "protected_only", defaultValue: false);
        var encryptedOnly = ToolArgs.GetBoolean(arguments, "encrypted_only", defaultValue: false);
        var maxResults = ResolveMaxResults(arguments);

        IEnumerable<BitLockerVolumeInfo> query;
        try {
            query = BitLocker.Get(computerName);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "BitLocker status query failed."));
        }

        var filtered = query
            .Where(x => !protectedOnly || x.ProtectionStatus == BitLockerProtectionStatus.Protected)
            .Where(x => !encryptedOnly || x.EncryptionPercentage > 0)
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new SystemBitlockerStatusResult(
            ComputerName: target,
            ProtectedOnly: protectedOnly,
            EncryptedOnly: encryptedOnly,
            Scanned: scanned,
            Truncated: truncated,
            Volumes: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "volumes_view",
            title: "BitLocker status (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, target);
                meta.Add("protected_only", protectedOnly);
                meta.Add("encrypted_only", encryptedOnly);
                AddMaxResultsMeta(meta, maxResults);
            });
        return Task.FromResult(response);
    }
}
