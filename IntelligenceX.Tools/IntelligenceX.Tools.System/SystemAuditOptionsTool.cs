using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Audit;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns advanced audit-policy option posture for the local or remote Windows host.
/// </summary>
public sealed class SystemAuditOptionsTool : SystemToolBase, ITool {
    private sealed record AuditOptionsRequest(
        string? ComputerName,
        string Target);

    private sealed record AuditOptionsResponse(
        string ComputerName,
        bool? ForceSubcategoryOverride,
        bool? AuditBaseObjects,
        bool? AuditBaseDirectories,
        int? CrashOnAuditFail,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_audit_options",
        "Return advanced audit-policy option posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:audit_policy_options", "intent:audit_hardening", "scope:host_audit_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAuditOptionsTool"/> class.
    /// </summary>
    public SystemAuditOptionsTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<AuditOptionsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<AuditOptionsRequest>.Success(new AuditOptionsRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<AuditOptionsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_audit_options");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = AuditOptionsQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new AuditOptionsResponse(
                ComputerName: effectiveComputerName,
                ForceSubcategoryOverride: posture.ForceSubcategoryOverride,
                AuditBaseObjects: posture.AuditBaseObjects,
                AuditBaseDirectories: posture.AuditBaseDirectories,
                CrashOnAuditFail: posture.CrashOnAuditFail,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_audit_options",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Audit options posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Force Subcategory Override", FormatNullableBool(posture.ForceSubcategoryOverride)),
                    ("Audit Base Objects", FormatNullableBool(posture.AuditBaseObjects)),
                    ("Audit Base Directories", FormatNullableBool(posture.AuditBaseDirectories)),
                    ("Crash On Audit Fail", posture.CrashOnAuditFail?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Audit options query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(AuditOptionsState posture) {
        var warnings = new List<string>();
        if (posture.ForceSubcategoryOverride == false) {
            warnings.Add("Advanced audit subcategory settings do not override legacy category settings.");
        }
        if (posture.CrashOnAuditFail is > 0) {
            warnings.Add("CrashOnAuditFail is enabled; failed security-audit logging can impact host availability.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
