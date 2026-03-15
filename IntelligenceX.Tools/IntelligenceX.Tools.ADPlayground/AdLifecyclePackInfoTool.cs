using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns Active Directory lifecycle pack capabilities and usage guidance for model-driven planning.
/// </summary>
public sealed class AdLifecyclePackInfoTool : ActiveDirectoryToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "ad_lifecycle_pack_info",
        description: "Return Active Directory lifecycle/write-pack capabilities, output contract, and dry-run-first guidance for joiner/leaver workflows.",
        packId: "active_directory_lifecycle",
        category: "active_directory",
        tags: new[] {
            "pack:active_directory_lifecycle",
            "domain_family:ad_domain",
            "domain_signals:dc,ldap,active_directory,adplayground,identity_lifecycle,joiner,leaver,offboarding,password_reset"
        },
        domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
        domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
        domainSignalTokens: ActiveDirectoryLifecycleRoutingCatalog.SignalTokens);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLifecyclePackInfoTool"/> class.
    /// </summary>
    public AdLifecyclePackInfoTool(ActiveDirectoryToolOptions options) : base(options) { }

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
            pack: "active_directory_lifecycle",
            engine: "ADPlayground",
            tools: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Enable the active_directory_lifecycle pack explicitly before using write-capable AD workflows.",
                "Call ad_environment_discover from the read-only AD pack first when domain scope or controller context is unclear.",
                "Use ad_user_lifecycle, ad_computer_lifecycle, or ad_group_lifecycle with apply=false first to preview the intended change, related memberships, and required fields.",
                "Switch apply=true only after the requested mutation, identity, and rollback context are explicit.",
                "Follow lifecycle writes with ad_object_get or ad_object_resolve in the read-only AD pack to verify resulting state."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover scope and validate AD context",
                    suggestedTools: new[] { "ad_environment_discover", "ad_scope_discovery" },
                    notes: "These tools live in the read-only Active Directory pack."),
                ToolPackGuidance.FlowStep(
                    goal: "Preview the lifecycle action",
                    suggestedTools: new[] { "ad_user_lifecycle", "ad_computer_lifecycle", "ad_group_lifecycle" },
                    notes: "Keep apply=false for preview/dry-run mode, including joiner memberships, offboard cleanup plans, computer account provisioning, or group membership changes."),
                ToolPackGuidance.FlowStep(
                    goal: "Apply the approved mutation and verify state",
                    suggestedTools: new[] { "ad_user_lifecycle", "ad_computer_lifecycle", "ad_group_lifecycle", "ad_object_get", "ad_object_resolve" },
                    notes: "Use apply=true only with explicit governance metadata and clear operator intent.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "user_joiner_leaver",
                    summary: "Governed user lifecycle writes covering create with memberships, enable/disable, offboard cleanup, delete, and password reset actions.",
                    primaryTools: new[] { "ad_user_lifecycle" },
                    notes: "Dry-run first. Supports planned group additions/removals and offboard attribute cleanup in one dangerous write-capable capability."),
                ToolPackGuidance.Capability(
                    id: "computer_account_lifecycle",
                    summary: "Governed computer account lifecycle writes covering create, update, enable/disable, delete, and machine password reset actions.",
                    primaryTools: new[] { "ad_computer_lifecycle" },
                    notes: "Dry-run first. Supports typed host attributes, SPNs, and computer-account cleanup without falling back to generic shell execution."),
                ToolPackGuidance.Capability(
                    id: "group_account_lifecycle",
                    summary: "Governed group lifecycle writes covering create, update, delete, and member add/remove actions.",
                    primaryTools: new[] { "ad_group_lifecycle" },
                    notes: "Dry-run first. Supports typed group metadata and membership changes without requiring generic shell execution.")
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "lifecycle_to_readonly_verification",
                    summary: "Promote changed user identities into the read-only AD pack for verification and follow-up evidence.",
                    entityKinds: new[] { "identity", "user" },
                    sourceTools: new[] { "ad_user_lifecycle" },
                    targetTools: new[] { "ad_object_get", "ad_object_resolve" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("distinguished_name", "identity", "Prefer the distinguished name when available for direct post-change verification."),
                        ToolPackGuidance.EntityFieldMapping("identity", "identity", "Fallback to the original identity when the write result did not produce a DN.")
                    })
            },
            toolCatalog: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve the lifecycle action payload, requested attributes, and mutation result fields for audit-friendly reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            correlationGuidance: "Correlate lifecycle changes with the read-only AD, System, and EventLog packs for validation, blast-radius review, and operational evidence.",
            setupHints: new {
                DomainController = Options.DomainController,
                DefaultSearchBaseDn = Options.DefaultSearchBaseDn,
                MaxResults = Options.MaxResults,
                DangerLevel = "dangerous_write",
                RecommendedVerificationPack = "active_directory"
            });

        var summary = ToolMarkdown.SummaryText(
            title: "AD Lifecycle Pack",
            "This pack is dangerous and should stay disabled by default until lifecycle writes are explicitly needed.",
            "Use ad_user_lifecycle, ad_computer_lifecycle, or ad_group_lifecycle in dry-run mode first, then verify the resulting identity and state with the read-only AD pack.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}
