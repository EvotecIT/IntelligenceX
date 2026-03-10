using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns PowerShell logging posture details from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemPowerShellLoggingPostureTool : SystemToolBase, ITool {
    private sealed record PowerShellLoggingPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeModuleNames);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_powershell_logging_posture",
        "Return Windows PowerShell and PowerShell Core logging posture (script block, module logging, transcription) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_module_names", ToolSchema.Boolean("When true, include configured module-logging name lists in the raw payload.")))
            .NoAdditionalProperties());

    private sealed record SystemPowerShellLoggingPostureResult(
        string ComputerName,
        PsLoggingPolicyState Policy,
        IReadOnlyList<string> Warnings);

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
                Target: ResolveTargetComputerName(computerName),
                IncludeModuleNames: reader.Boolean("include_module_names", defaultValue: false)));
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
            var raw = PsLoggingPolicyQuery.Get(request.ComputerName);
            var policy = new PsLoggingPolicyState {
                Windows = CloneScope(raw.Windows, request.IncludeModuleNames),
                Core = CloneScope(raw.Core, request.IncludeModuleNames)
            };

            var warnings = new List<string>();
            AddScopeWarnings(warnings, "Windows PowerShell", raw.Windows);
            AddScopeWarnings(warnings, "PowerShell Core", raw.Core);

            var model = new SystemPowerShellLoggingPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System PowerShell logging posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("WindowsScriptBlockLogging", raw.Windows.EnableScriptBlockLogging?.ToString() ?? string.Empty),
                    ("WindowsModuleLogging", raw.Windows.EnableModuleLogging?.ToString() ?? string.Empty),
                    ("WindowsTranscription", raw.Windows.EnableTranscription?.ToString() ?? string.Empty),
                    ("CoreScriptBlockLogging", raw.Core.EnableScriptBlockLogging?.ToString() ?? string.Empty),
                    ("CoreModuleLogging", raw.Core.EnableModuleLogging?.ToString() ?? string.Empty),
                    ("CoreTranscription", raw.Core.EnableTranscription?.ToString() ?? string.Empty),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => meta.Add("include_module_names", request.IncludeModuleNames)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "PowerShell logging posture query failed."));
        }
    }

    private static PsLoggingScopeState CloneScope(PsLoggingScopeState source, bool includeModuleNames) {
        return new PsLoggingScopeState {
            EnableScriptBlockLogging = source.EnableScriptBlockLogging,
            EnableScriptBlockInvocationLogging = source.EnableScriptBlockInvocationLogging,
            EnableModuleLogging = source.EnableModuleLogging,
            ModuleNames = includeModuleNames ? source.ModuleNames : null,
            EnableTranscription = source.EnableTranscription,
            TranscriptionOutputDirectory = source.TranscriptionOutputDirectory,
            EnableInvocationHeader = source.EnableInvocationHeader
        };
    }

    private static void AddScopeWarnings(List<string> warnings, string scopeName, PsLoggingScopeState scope) {
        if (scope.EnableScriptBlockLogging != true) {
            warnings.Add($"{scopeName} script block logging is not enabled.");
        }
        if (scope.EnableModuleLogging != true) {
            warnings.Add($"{scopeName} module logging is not enabled.");
        }
        if (scope.EnableTranscription != true) {
            warnings.Add($"{scopeName} transcription is not enabled.");
        }
    }
}
