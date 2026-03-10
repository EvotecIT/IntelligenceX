using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Office;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns Office macro and Protected View posture for the local or remote Windows host.
/// </summary>
public sealed class SystemOfficePostureTool : SystemToolBase, ITool {
    private sealed record OfficePostureRequest(
        string? ComputerName,
        string Target);

    private sealed record OfficePostureResponse(
        string ComputerName,
        string? PolicyVersion,
        bool? PolicyRootPresent,
        int MacroRestrictiveCount,
        int ProtectedViewRelaxedCount,
        IReadOnlyList<OfficeApplicationPostureInfo> Applications,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_office_posture",
        "Return Office macro and Protected View posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemOfficePostureTool"/> class.
    /// </summary>
    public SystemOfficePostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<OfficePostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<OfficePostureRequest>.Success(new OfficePostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<OfficePostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_office_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await OfficePosture
                .GetAsync(request.ComputerName, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var warnings = BuildWarnings(posture);
            var model = new OfficePostureResponse(
                ComputerName: effectiveComputerName,
                PolicyVersion: posture.PolicyVersion,
                PolicyRootPresent: posture.PolicyRootPresent,
                MacroRestrictiveCount: posture.MacroRestrictiveCount,
                ProtectedViewRelaxedCount: posture.ProtectedViewRelaxedCount,
                Applications: posture.Applications,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_office_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(posture.Applications.Count, 1),
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Office posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Policy Version", posture.PolicyVersion ?? "unknown"),
                    ("Policy Root Present", FormatNullableBool(posture.PolicyRootPresent)),
                    ("Macro Restrictive Count", posture.MacroRestrictiveCount.ToString(CultureInfo.InvariantCulture)),
                    ("Protected View Relaxed Count", posture.ProtectedViewRelaxedCount.ToString(CultureInfo.InvariantCulture)),
                    ("Applications", posture.Applications.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Office posture query failed.");
        }
    }

    private static IReadOnlyList<string> BuildWarnings(OfficePostureInfo posture) {
        var warnings = new List<string>();
        foreach (var application in posture.Applications) {
            if (application.MacroPolicyRestrictive == false) {
                warnings.Add($"{application.Application} macros are not restricted by policy.");
            }

            if (application.ProtectedViewInternetFilesEnabled == false
                || application.ProtectedViewUnsafeLocationsEnabled == false
                || application.ProtectedViewAttachmentsEnabled == false) {
                warnings.Add($"{application.Application} has one or more Protected View surfaces relaxed.");
            }
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
