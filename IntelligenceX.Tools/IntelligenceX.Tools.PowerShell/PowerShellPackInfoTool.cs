using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Returns IX.PowerShell pack capabilities and usage guidance for model-driven planning.
/// </summary>
public sealed class PowerShellPackInfoTool : PowerShellToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "powershell_pack_info",
        description: "Return IX.PowerShell pack capabilities, output contract, and safe usage guidance (pwsh/windows_powershell/cmd). Call this first when planning shell execution.",
        packId: "powershell");

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellPackInfoTool"/> class.
    /// </summary>
    public PowerShellPackInfoTool(PowerShellToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<PackInfoRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<PackInfoRequest>.Success(new PackInfoRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<PackInfoRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "powershell",
            engine: "IntelligenceX.Engines.PowerShell",
            tools: ToolRegistryPowerShellExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call powershell_environment_discover first to read policy, host availability, and runtime limits.",
                "Use powershell_hosts when you only need host inventory.",
                "Use powershell_run with host=pwsh/windows_powershell/cmd and intent=read_only by default.",
                "Switch to intent=read_write only with explicit approval.",
                "Prefer short, auditable commands and cap timeout/output when possible."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover runtime policy and host availability",
                    suggestedTools: new[] { "powershell_environment_discover" }),
                ToolPackGuidance.FlowStep(
                    goal: "Optional host-only inventory",
                    suggestedTools: new[] { "powershell_hosts" }),
                ToolPackGuidance.FlowStep(
                    goal: "Execute command/script with explicit intent and bounded runtime options",
                    suggestedTools: new[] { "powershell_run" },
                    notes: "Default to read_only. Use read_write only when required and policy allows it.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "environment_discovery",
                    summary: "Expose IX.PowerShell policy and runtime discovery data for autonomous tool planning.",
                    primaryTools: new[] { "powershell_environment_discover" }),
                ToolPackGuidance.Capability(
                    id: "host_discovery",
                    summary: "Report locally available shell hosts (pwsh/windows_powershell/cmd).",
                    primaryTools: new[] { "powershell_hosts" }),
                ToolPackGuidance.Capability(
                    id: "runtime_execution",
                    summary: "Execute command/script text with explicit read_only/read_write intent and typed output envelopes.",
                    primaryTools: new[] { "powershell_run" },
                    notes: "Dangerous capability. Enable only when explicitly allowed by policy.")
            },
            toolCatalog: ToolRegistryPowerShellExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve full stdout/stderr/output fields for model reasoning and follow-up actions.",
            viewProjectionPolicy: "Projection arguments are optional and view-only (this pack does not currently use projection fields).",
            correlationGuidance: "Correlate command output with System/EventLog/AD tooling as needed.",
            setupHints: new {
                Enabled = Options.Enabled,
                AllowWrite = Options.AllowWrite,
                RequireExplicitWriteFlag = Options.RequireExplicitWriteFlag,
                EnableMutationHeuristic = Options.EnableMutationHeuristic,
                DefaultTimeoutMs = Options.DefaultTimeoutMs,
                MaxTimeoutMs = Options.MaxTimeoutMs,
                DefaultMaxOutputChars = Options.DefaultMaxOutputChars,
                MaxOutputChars = Options.MaxOutputChars,
                DangerLevel = "dangerous_write"
            });

        var summary = ToolMarkdown.SummaryText(
            title: "IX.PowerShell Pack",
            "This pack is dangerous and should be explicitly enabled by policy.",
            "Use powershell_environment_discover before powershell_run to verify policy and host availability.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}
