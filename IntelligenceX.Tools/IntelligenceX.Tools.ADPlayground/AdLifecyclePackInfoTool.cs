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

        var root = BuildGuidance(Options);

        var summary = ToolMarkdown.SummaryText(
            title: "AD Lifecycle Pack",
            "This pack is dangerous and should stay disabled by default until lifecycle writes are explicitly needed.",
            "Use ad_user_lifecycle, ad_computer_lifecycle, ad_group_lifecycle, or ad_ou_lifecycle in dry-run mode first, then verify the resulting identity and state with the read-only AD pack.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }

    internal static ToolPackInfoModel BuildGuidance(ActiveDirectoryToolOptions options) {
        return ToolPackGuidance.Create(
            pack: "active_directory_lifecycle",
            engine: "ADPlayground",
            tools: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolNames(options),
            recommendedFlow: new[] {
                "Enable the active_directory_lifecycle pack explicitly before using write-capable AD workflows.",
                "Call ad_environment_discover from the read-only AD pack first when domain scope or controller context is unclear.",
                "Use ad_user_lifecycle, ad_computer_lifecycle, ad_group_lifecycle, or ad_ou_lifecycle with apply=false first to preview the intended change, related memberships, and required fields.",
                "Switch apply=true only after the requested mutation, identity, and rollback context are explicit.",
                "Use ad_ou_lifecycle for create/update/move/delete and accidental-deletion protection changes on organizational units.",
                "Follow lifecycle writes with ad_object_get, ad_object_resolve, or ad_user_groups_resolved in the read-only AD pack to verify resulting state, and reuse computer_name or group identity follow-up pivots when the lifecycle result exposes them for System or EventLog checks."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover scope and validate AD context",
                    suggestedTools: new[] { "ad_environment_discover", "ad_scope_discovery" },
                    notes: "These tools live in the read-only Active Directory pack."),
                ToolPackGuidance.FlowStep(
                    goal: "Preview the lifecycle action",
                    suggestedTools: new[] { "ad_user_lifecycle", "ad_computer_lifecycle", "ad_group_lifecycle", "ad_ou_lifecycle" },
                    notes: "Keep apply=false for preview/dry-run mode, including joiner memberships, offboard cleanup plans, computer account provisioning, group membership changes, or OU create/move/protection changes."),
                ToolPackGuidance.FlowStep(
                    goal: "Apply the approved mutation and verify state",
                    suggestedTools: new[] { "ad_user_lifecycle", "ad_computer_lifecycle", "ad_group_lifecycle", "ad_ou_lifecycle", "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                    notes: "Use apply=true only with explicit governance metadata and clear operator intent.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "user_joiner_leaver",
                    summary: "Governed user lifecycle writes covering create, update, move, enable/disable, offboard cleanup, delete, and password reset actions.",
                    primaryTools: new[] { "ad_user_lifecycle" },
                    notes: "Dry-run first. Supports typed user-profile changes, planned group additions/removals, OU moves, and offboard attribute cleanup in one dangerous write-capable capability."),
                ToolPackGuidance.Capability(
                    id: "computer_account_lifecycle",
                    summary: "Governed computer account lifecycle writes covering create, update, move/rename, enable/disable, delete, and machine password reset actions.",
                    primaryTools: new[] { "ad_computer_lifecycle" },
                    notes: "Dry-run first. Supports typed host attributes, SPNs, computer-account relocation/rename, and cleanup without falling back to generic shell execution."),
                ToolPackGuidance.Capability(
                    id: "group_account_lifecycle",
                    summary: "Governed group lifecycle writes covering create, update, move/rename, delete, and member add/remove actions.",
                    primaryTools: new[] { "ad_group_lifecycle" },
                    notes: "Dry-run first. Supports typed group metadata, group relocation/rename, and membership changes without requiring generic shell execution."),
                ToolPackGuidance.Capability(
                    id: "organizational_unit_lifecycle",
                    summary: "Governed organizational-unit lifecycle writes covering create, update, move/rename, delete, accidental-deletion protection, and block-inheritance changes.",
                    primaryTools: new[] { "ad_ou_lifecycle" },
                    notes: "Dry-run first. Useful for quarantine/staging OU preparation, delegated OU refactors, and explicit protection posture changes.")
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "lifecycle_to_readonly_verification",
                    summary: "Promote changed user identities into the read-only AD pack for identity and membership verification after governed writes.",
                    entityKinds: new[] { "identity", "user" },
                    sourceTools: new[] { "ad_user_lifecycle" },
                    targetTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("distinguished_name", "identity", "Prefer the distinguished name when available for direct post-change verification."),
                        ToolPackGuidance.EntityFieldMapping("identity", "identity", "Fallback to the original identity when the write result did not produce a DN.")
                    }),
                ToolPackGuidance.EntityHandoff(
                    id: "computer_lifecycle_to_host_followup",
                    summary: "Promote changed computer accounts into ComputerX/System follow-up using the lifecycle result's resolved computer_name.",
                    entityKinds: new[] { "computer", "host" },
                    sourceTools: new[] { "ad_computer_lifecycle" },
                    targetTools: new[] { "system_info", "system_metrics_summary" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("computer_name", "computer_name", "Prefer the lifecycle result's resolved computer_name so ComputerX follow-up reuses the same governed target."),
                        ToolPackGuidance.EntityFieldMapping("distinguished_name", "identity", "Keep the distinguished name available for AD-side verification in parallel.")
                    }),
                ToolPackGuidance.EntityHandoff(
                    id: "computer_lifecycle_to_eventlog_followup",
                    summary: "Promote changed computer accounts into EventLog channel discovery using the lifecycle result's resolved computer_name.",
                    entityKinds: new[] { "computer", "host", "event_source" },
                    sourceTools: new[] { "ad_computer_lifecycle" },
                    targetTools: new[] { "eventlog_channels_list" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("computer_name", "machine_name", "Prefer the lifecycle result's resolved computer_name so EventViewerX follow-up stays on the same governed host.")
                    }),
                ToolPackGuidance.EntityHandoff(
                    id: "group_lifecycle_to_membership_verification",
                    summary: "Promote changed groups into resolved membership verification after governed group updates.",
                    entityKinds: new[] { "group", "membership" },
                    sourceTools: new[] { "ad_group_lifecycle" },
                    targetTools: new[] { "ad_group_members_resolved", "ad_object_get" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("distinguished_name", "identity", "Prefer the distinguished name when verifying the post-change membership set."),
                        ToolPackGuidance.EntityFieldMapping("identity", "identity", "Fallback to the original group identity when the write result did not produce a DN.")
                    })
            },
            recipes: new[] {
                ToolPackGuidance.Recipe(
                    id: "joiner_onboarding",
                    summary: "Create a new user with initial attributes and memberships using a governed dry-run-first workflow.",
                    whenToUse: "Use for onboarding or new-starter requests where the user account, password posture, and initial group access need to be staged and verified safely.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Confirm domain scope and target OU",
                            suggestedTools: new[] { "ad_environment_discover", "ad_scope_discovery" },
                            notes: "Resolve the target domain, default naming context, and onboarding OU before preparing the write."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview the governed user creation",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Use operation=create with apply=false and include organizational_unit, identity fields, and groups_to_add so the preview returns required inputs and rollback context."),
                        ToolPackGuidance.FlowStep(
                            goal: "Apply the approved onboarding mutation",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Switch apply=true only after the operator confirms the identity, OU, initial password handling, and membership plan."),
                        ToolPackGuidance.FlowStep(
                            goal: "Verify the resulting account and access state",
                            suggestedTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                            notes: "Use the returned distinguished name when available to confirm the account landed in the expected OU with the expected attributes and the intended group footprint.")
                    },
                    verificationTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                    notes: "Prefer user-centric membership changes in ad_user_lifecycle unless the request specifically needs governed group-side updates."),
                ToolPackGuidance.Recipe(
                    id: "mover_access_transition",
                    summary: "Adjust an existing user's governed access footprint during department or team moves.",
                    whenToUse: "Use for mover scenarios where the identity stays active but governed group memberships, OU/container placement, or re-enable access steps need a safe preview/apply sequence.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Resolve the current user and current access posture",
                            suggestedTools: new[] { "ad_object_resolve", "ad_object_get" },
                            notes: "Capture the current distinguished name, manager, and group-related context before changing memberships."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview governed user profile and membership changes",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Use operation=update with apply=false when the mover plan needs typed profile updates plus groups_to_add/groups_to_remove on the user itself."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview governed user OU moves when required",
                            suggestedTools: new[] { "ad_user_lifecycle", "ad_ou_lifecycle" },
                            notes: "Use operation=move with apply=false when the user account itself needs to land in a different OU, and use ad_ou_lifecycle only when the target container also needs governed preparation or refactoring."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview account re-enable steps when access is being restored",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Use operation=enable with apply=false only when the mover workflow includes reactivating a disabled account and optionally reapplying initial groups_to_add."),
                        ToolPackGuidance.FlowStep(
                            goal: "Apply the approved access transition and verify",
                            suggestedTools: new[] { "ad_user_lifecycle", "ad_group_lifecycle", "ad_ou_lifecycle", "ad_object_get", "ad_user_groups_resolved", "ad_group_members_resolved" },
                            notes: "Apply only the approved writes, then confirm the final account state and resulting group footprint.")
                    },
                    verificationTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved", "ad_group_members_resolved" },
                    notes: "Keep the mover workflow dry-run-first and preserve the pre-change group list in case rollback is needed."),
                ToolPackGuidance.Recipe(
                    id: "leaver_offboarding",
                    summary: "Disable or offboard an account with governed cleanup of group access and selected attributes.",
                    whenToUse: "Use for leaver workflows, emergency disable requests, or controlled offboarding where access removal and evidence-friendly verification matter.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Resolve the account and capture pre-change state",
                            suggestedTools: new[] { "ad_object_resolve", "ad_object_get" },
                            notes: "Confirm the target identity and gather current memberships or attributes that will be cleared or preserved."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview the disable or offboard plan",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Use operation=disable or operation=offboard with apply=false and include groups_to_remove, clear_attributes, and optional password reset handling."),
                        ToolPackGuidance.FlowStep(
                            goal: "Apply the approved offboarding mutation",
                            suggestedTools: new[] { "ad_user_lifecycle" },
                            notes: "Switch apply=true only after rollback context, cleanup scope, and the exact identity are explicit."),
                        ToolPackGuidance.FlowStep(
                            goal: "Verify the disabled state and residual access",
                            suggestedTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                            notes: "Confirm the account state, remaining memberships, and whether any cleanup follow-up is still required.")
                    },
                    verificationTools: new[] { "ad_object_get", "ad_object_resolve", "ad_user_groups_resolved" },
                    notes: "For emergency disables, prefer operation=disable first, then use operation=offboard when the broader cleanup plan is ready."),
                ToolPackGuidance.Recipe(
                    id: "quarantine_ou_preparation",
                    summary: "Prepare or refactor quarantine/staging OUs with governed create, move, protection, and inheritance controls.",
                    whenToUse: "Use when a quarantine, staging, or delegated administration OU needs to be created, renamed, moved, protected, or adjusted before identities are placed there.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Discover the target domain and parent naming context",
                            suggestedTools: new[] { "ad_environment_discover", "ad_scope_discovery" },
                            notes: "Confirm the forest/domain scope and the intended parent distinguished name before OU changes."),
                        ToolPackGuidance.FlowStep(
                            goal: "Preview the OU lifecycle change",
                            suggestedTools: new[] { "ad_ou_lifecycle" },
                            notes: "Use apply=false with operation=create, update, move, or delete and include protection or block-inheritance settings when they matter."),
                        ToolPackGuidance.FlowStep(
                            goal: "Apply the approved OU mutation",
                            suggestedTools: new[] { "ad_ou_lifecycle" },
                            notes: "Switch apply=true only after the target DN, new name, and protection posture are explicit."),
                        ToolPackGuidance.FlowStep(
                            goal: "Verify the resulting OU shape",
                            suggestedTools: new[] { "ad_object_get", "ad_object_resolve" },
                            notes: "Confirm the OU distinguished name, accidental-deletion protection, and inheritance posture after the write.")
                    },
                    verificationTools: new[] { "ad_object_get", "ad_object_resolve" },
                    notes: "This recipe prepares the quarantine container itself; pair it with user or computer lifecycle recipes when identities also need governed changes.")
            },
            toolCatalog: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolCatalog(options),
            runtimeCapabilities: new ToolPackRuntimeCapabilitiesModel {
                PreferredEntryTools = new[] { "ad_environment_discover", "ad_user_lifecycle", "ad_group_lifecycle", "ad_ou_lifecycle" },
                RuntimePrerequisites = new[] {
                    "Enable the active_directory_lifecycle pack explicitly before invoking governed AD write workflows.",
                    "Use apply=false first so the lifecycle tools return preview, validation, and rollback context before any mutation is attempted.",
                    "Provide explicit write-governance metadata and operator intent before switching a lifecycle action to apply=true."
                },
                Notes = "Treat this pack as a governed write surface: discover scope in the read-only AD pack first, preview the mutation, then verify the resulting state with ad_object_get or ad_object_resolve."
            },
            rawPayloadPolicy: "Preserve the lifecycle action payload, requested attributes, and mutation result fields for audit-friendly reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            correlationGuidance: "Correlate lifecycle changes with the read-only AD, System, and EventLog packs for validation, blast-radius review, and operational evidence.",
            setupHints: new {
                DomainController = options.DomainController,
                DefaultSearchBaseDn = options.DefaultSearchBaseDn,
                MaxResults = options.MaxResults,
                DangerLevel = "dangerous_write",
                RecommendedVerificationPack = "active_directory"
            });
    }
}
