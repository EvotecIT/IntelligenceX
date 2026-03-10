using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns host-level Defender ASR rule posture for the local or remote Windows host.
/// </summary>
public sealed class SystemDefenderAsrPostureTool : SystemToolBase, ITool {
    private sealed record DefenderAsrPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record DefenderAsrPostureResponse(
        string ComputerName,
        int TotalRules,
        int EnabledRules,
        int AuditModeRules,
        int DisabledRules,
        IReadOnlyList<AsrRule> Rules,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_defender_asr_posture",
        "Return host-level Defender Attack Surface Reduction rule posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:defender_asr_posture", "intent:attack_surface_reduction", "scope:host_defender_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemDefenderAsrPostureTool"/> class.
    /// </summary>
    public SystemDefenderAsrPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<DefenderAsrPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<DefenderAsrPostureRequest>.Success(new DefenderAsrPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DefenderAsrPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_defender_asr_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = DefenderAsrPolicyQuery.Get(request.ComputerName);
            var enabledRules = posture.Rules.Count(static rule => rule.State == AsrRuleState.Enabled);
            var auditRules = posture.Rules.Count(static rule => rule.State == AsrRuleState.AuditMode);
            var disabledRules = posture.Rules.Count(static rule => rule.State == AsrRuleState.Disabled);
            var warnings = BuildWarnings(posture, enabledRules);
            var effectiveComputerName = request.Target;
            var model = new DefenderAsrPostureResponse(
                ComputerName: effectiveComputerName,
                TotalRules: posture.Rules.Count,
                EnabledRules: enabledRules,
                AuditModeRules: auditRules,
                DisabledRules: disabledRules,
                Rules: posture.Rules,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_defender_asr_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(posture.Rules.Count, 1),
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Defender ASR posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Total Rules", posture.Rules.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Enabled Rules", enabledRules.ToString(CultureInfo.InvariantCulture)),
                    ("Audit Mode Rules", auditRules.ToString(CultureInfo.InvariantCulture)),
                    ("Disabled Rules", disabledRules.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Defender ASR posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(DefenderAsrPolicyState posture, int enabledRules) {
        var warnings = new List<string>();
        if (posture.Rules.Count == 0) {
            warnings.Add("No Defender ASR rule state was discovered.");
        } else if (enabledRules == 0) {
            warnings.Add("No Defender ASR rules are enabled.");
        }

        if (posture.Rules.Any(static rule => rule.State == AsrRuleState.Disabled)) {
            warnings.Add("One or more Defender ASR rules are disabled.");
        }

        return warnings;
    }
}
