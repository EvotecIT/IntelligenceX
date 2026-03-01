using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns system pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class SystemPackInfoTool : SystemToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_pack_info",
        "Return system pack capabilities, output contract, and recommended usage patterns. Call this first when planning system diagnostics.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPackInfoTool"/> class.
    /// </summary>
    public SystemPackInfoTool(SystemToolOptions options) : base(options) { }

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
            pack: "system",
            engine: "ComputerX",
            tools: ToolRegistrySystemExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call system_info or system_hardware_summary for baseline context.",
                "Use list tools (processes/services/ports/adapters/firewall/disks/features/apps/updates/bitlocker) for evidence collection.",
                "Use system_rdp_posture/system_smb_posture/system_boot_configuration/system_bios_summary for host-hardening and firmware posture checks.",
                "Use system_security_options when you need a registry-backed snapshot of Windows security-option posture.",
                "Use system_time_sync for quick skew checks and w32time runtime status (local or remote).",
                "Use system_patch_details for monthly MSRC patch intelligence (CVE/KB/severity details, defaulting to current UTC month).",
                "Use system_patch_compliance to correlate monthly MSRC KB coverage with installed updates and prioritize missing exploited CVEs.",
                "Use AD/TestimoX/EventLog handoff evidence (computer/host identifiers) to drive focused ComputerX follow-up rather than broad host scans.",
                "Use optional projection arguments only when the user asks for specific columns or sorting."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Collect baseline host context",
                    suggestedTools: new[] { "system_info", "system_hardware_summary", "system_whoami" }),
                ToolPackGuidance.FlowStep(
                    goal: "Collect process/network/security evidence",
                    suggestedTools: new[] { "system_process_list", "system_ports_list", "system_network_adapters", "system_firewall_rules", "system_firewall_profiles", "system_security_options", "system_rdp_posture", "system_smb_posture", "system_time_sync", "system_bitlocker_status" }),
                ToolPackGuidance.FlowStep(
                    goal: "Collect storage/feature configuration evidence",
                    suggestedTools: new[] { "system_logical_disks_list", "system_disks_list", "system_features_list", "system_scheduled_tasks_list", "system_service_list", "system_installed_applications", "system_updates_installed", "system_boot_configuration", "system_bios_summary" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess monthly patch exposure and prioritization",
                    suggestedTools: new[] { "system_patch_details", "system_patch_compliance", "system_updates_installed" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "host_baseline",
                    summary: "Return host identity/runtime baselines and principal context.",
                    primaryTools: new[] { "system_info", "system_hardware_identity", "system_hardware_summary", "system_whoami" }),
                ToolPackGuidance.Capability(
                    id: "runtime_evidence",
                    summary: "Enumerate processes, services, ports, adapters, firewall rules/profiles, security-option posture, and time sync/BitLocker posture.",
                    primaryTools: new[] { "system_process_list", "system_service_list", "system_ports_list", "system_network_adapters", "system_firewall_rules", "system_firewall_profiles", "system_security_options", "system_rdp_posture", "system_smb_posture", "system_time_sync", "system_bitlocker_status" }),
                ToolPackGuidance.Capability(
                    id: "platform_configuration",
                    summary: "Inventory disks, devices, features, scheduled tasks, installed applications/updates, and optional WSL state.",
                    primaryTools: new[] { "system_logical_disks_list", "system_disks_list", "system_devices_summary", "system_features_list", "system_scheduled_tasks_list", "system_installed_applications", "system_updates_installed", "system_boot_configuration", "system_bios_summary", "wsl_status" }),
                ToolPackGuidance.Capability(
                    id: "patch_intelligence",
                    summary: "Provide month-scoped MSRC patch intelligence with CVE/KB metadata and severity/exploitation prioritization.",
                    primaryTools: new[] { "system_patch_details", "system_patch_compliance", "system_updates_installed" })
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "ad_or_eventlog_host_to_system_scope",
                    summary: "Promote AD/EventLog host indicators into ComputerX remote host-scoping arguments.",
                    entityKinds: new[] { "computer", "host", "domain_controller" },
                    sourceTools: new[] { "ad_scope_discovery", "ad_domain_controller_facts", "ad_object_resolve", "eventlog_live_stats", "eventlog_named_events_query" },
                    targetTools: new[] { "system_info", "system_process_list", "system_service_list", "system_ports_list", "system_security_options", "system_time_sync", "system_updates_installed" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("rows[].dns_host_name", "computer_name", "Prefer canonical FQDN/hostname values and deduplicate before fan-out."),
                        ToolPackGuidance.EntityFieldMapping("rows[].computer", "computer_name", "Normalize and deduplicate host aliases for remote ComputerX calls."),
                        ToolPackGuidance.EntityFieldMapping("rows[].host", "computer_name", "Map generic host indicators to ComputerX computer_name scope.")
                    },
                    notes: "Use focused host batches from AD/EventLog evidence to reduce noisy host-wide diagnostics."),
                ToolPackGuidance.EntityHandoff(
                    id: "system_patch_findings_to_ad_eventlog_followup",
                    summary: "Route patch compliance findings into AD identity ownership and EventLog correlation workflows.",
                    entityKinds: new[] { "computer", "kb", "cve" },
                    sourceTools: new[] { "system_patch_compliance", "system_patch_details" },
                    targetTools: new[] { "ad_object_resolve", "ad_search", "eventlog_live_query", "eventlog_named_events_query" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("rows[].computer_name", "identity", "Resolve impacted hosts in AD before assigning remediation ownership."),
                        ToolPackGuidance.EntityFieldMapping("rows[].missing_kbs[]", "query", "Use KB identifiers for follow-up evidence searches and ticket correlation."),
                        ToolPackGuidance.EntityFieldMapping("rows[].cve_id", "query", "Use CVE identifiers to correlate host telemetry and incident timelines.")
                    },
                    notes: "After patch gap detection, pivot into AD ownership context and EventLog timeline evidence.")
            },
            toolCatalog: ToolRegistrySystemExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw engine arrays/objects. Do not rely only on *_view fields.",
            viewProjectionPolicy: "Projection arguments are view-only and intended for display shaping (columns/sort_by/sort_direction/top).",
            correlationGuidance: "Correlate using raw payload fields across multiple tool calls.",
            setupHints: new {
                MaxResults = Options.MaxResults,
                SupportedPlatforms = Environment.OSVersion.Platform.ToString(),
                Note = "Some tools are Windows-only; if unavailable, use platform-neutral tools first."
            });

        var summary = ToolMarkdown.SummaryText(
            title: "System Pack",
            "Use raw payload fields for reasoning and correlation.",
            "Use `*_view` fields only for presentation filtering/sorting.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}
