using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns PowerShell logging policy posture for the local or remote Windows host.
/// </summary>
public sealed class SystemPowerShellLoggingPostureTool : SystemToolBase, ITool {
    private sealed record PowerShellLoggingPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record PowerShellLoggingPostureResponse(
        string ComputerName,
        PsLoggingScopeState WindowsPowerShell,
        PsLoggingScopeState PowerShellCore,
        int WindowsModuleNameCount,
        int CoreModuleNameCount,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_powershell_logging_posture",
        "Return PowerShell script-block/module/transcription logging posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:powershell_logging_posture", "intent:script_audit_policy", "scope:host_powershell_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPowerShellLoggingPostureTool"/> class.
    /// </summary>
    public SystemPowerShellLoggingPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<PowerShellLoggingPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<PowerShellLoggingPostureRequest>.Success(new PowerShellLoggingPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<PowerShellLoggingPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_powershell_logging_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = PsLoggingPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var windowsModuleCount = posture.Windows.ModuleNames?.Length ?? 0;
            var coreModuleCount = posture.Core.ModuleNames?.Length ?? 0;
            var effectiveComputerName = request.Target;
            var model = new PowerShellLoggingPostureResponse(
                ComputerName: effectiveComputerName,
                WindowsPowerShell: posture.Windows,
                PowerShellCore: posture.Core,
                WindowsModuleNameCount: windowsModuleCount,
                CoreModuleNameCount: coreModuleCount,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_powershell_logging_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(windowsModuleCount + coreModuleCount, 1),
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "PowerShell logging posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Windows Script Block Logging", FormatNullableBool(posture.Windows.EnableScriptBlockLogging)),
                    ("Windows Module Logging", FormatNullableBool(posture.Windows.EnableModuleLogging)),
                    ("Windows Transcription", FormatNullableBool(posture.Windows.EnableTranscription)),
                    ("Core Script Block Logging", FormatNullableBool(posture.Core.EnableScriptBlockLogging)),
                    ("Core Module Logging", FormatNullableBool(posture.Core.EnableModuleLogging)),
                    ("Core Transcription", FormatNullableBool(posture.Core.EnableTranscription)),
                    ("Windows Module Names", windowsModuleCount.ToString(CultureInfo.InvariantCulture)),
                    ("Core Module Names", coreModuleCount.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "PowerShell logging posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(PsLoggingPolicyState posture) {
        var warnings = new List<string>();
        AddScopeWarnings(warnings, "Windows PowerShell", posture.Windows);
        AddScopeWarnings(warnings, "PowerShell Core", posture.Core);
        return warnings;
    }

    private static void AddScopeWarnings(List<string> warnings, string scopeName, PsLoggingScopeState scope) {
        if (scope.EnableScriptBlockLogging == false) {
            warnings.Add($"{scopeName} script block logging is disabled.");
        }
        if (scope.EnableModuleLogging == false) {
            warnings.Add($"{scopeName} module logging is disabled.");
        }
        if (scope.EnableTranscription == false) {
            warnings.Add($"{scopeName} transcription is disabled.");
        }
        if (scope.EnableTranscription == true && string.IsNullOrWhiteSpace(scope.TranscriptionOutputDirectory)) {
            warnings.Add($"{scopeName} transcription is enabled but no output directory is configured.");
        }
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
