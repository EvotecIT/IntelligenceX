using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Setup.Onboarding;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Returns reviewer setup path contract and command guidance for model-driven orchestration.
/// </summary>
public sealed class ReviewerSetupPackInfoTool : ReviewerSetupToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "reviewer_setup_pack_info",
        "Return reviewer onboarding path definitions (new-setup, refresh-auth, cleanup, maintenance) and recommended command flows. Call this first before onboarding automation.",
        ToolSchema.Object().NoAdditionalProperties(),
        routing: new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "reviewer_setup",
            Role = ToolRoutingTaxonomy.RolePackInfo
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="ReviewerSetupPackInfoTool"/> class.
    /// </summary>
    public ReviewerSetupPackInfoTool(ReviewerSetupToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "reviewersetup",
            engine: "IntelligenceX.Cli.Setup",
            tools: ToolRegistryReviewerSetupExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Run setup autodetect to classify current state before selecting a path.",
                "Verify autodetect contract metadata with reviewer_setup_contract_verify before planning mutating commands.",
                "Select exactly one onboarding path: new-setup, refresh-auth, cleanup, or maintenance.",
                "Run path-specific setup commands in dry-run first, then apply.",
                "After apply, verify workflow/config/secret state."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Collect preflight and path recommendation",
                    suggestedTools: new[] { "reviewer_setup_pack_info" },
                    notes: "Run `intelligencex setup autodetect --json` for machine-friendly checks."),
                ToolPackGuidance.FlowStep(
                    goal: "Validate contract parity",
                    suggestedTools: new[] { "reviewer_setup_contract_verify" },
                    notes: "Compare autodetect contractVersion/contractFingerprint with this pack before apply/cleanup commands."),
                ToolPackGuidance.FlowStep(
                    goal: "Execute path-specific onboarding",
                    suggestedTools: new[] { "reviewer_setup_pack_info" },
                    notes: "Use command templates from setup_hints.commandTemplates."),
                ToolPackGuidance.FlowStep(
                    goal: "Verify and close onboarding",
                    suggestedTools: new[] { "reviewer_setup_pack_info" },
                    notes: "Use dry-run/verify output to decide next actions.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "path_contract",
                    summary: "Defines normalized onboarding paths and required auth/repo requirements.",
                    primaryTools: new[] { "reviewer_setup_pack_info" }),
                ToolPackGuidance.Capability(
                    id: "command_templates",
                    summary: "Provides CLI templates for autodetect, setup, auth refresh, and cleanup.",
                    primaryTools: new[] { "reviewer_setup_pack_info" }),
                ToolPackGuidance.Capability(
                    id: "contract_verification",
                    summary: "Validates autodetect contract metadata against the canonical onboarding contract to detect drift.",
                    primaryTools: new[] { "reviewer_setup_contract_verify" })
            },
            toolCatalog: ToolRegistryReviewerSetupExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Treat path ids and command templates as canonical onboarding contract data.",
            viewProjectionPolicy: "No projection arguments are used in this pack.",
            setupHints: BuildSetupHints(Options.IncludeMaintenancePath),
            note: "Execution tools are intentionally separated from path metadata so hosts can enforce local policy (for example explicit write approval).");

        var summary = ToolMarkdown.SummaryText(
            title: "Reviewer Setup Pack",
            "Call this tool first to discover onboarding paths and command templates.",
            "Use `new-setup`, `refresh-auth`, `cleanup`, or `maintenance` as stable path ids.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }

    private static object BuildCommandTemplates() {
        var templates = SetupOnboardingContract.GetCommandTemplates();
        return new {
            autoDetect = templates.AutoDetect,
            newSetupDryRun = templates.NewSetupDryRun,
            newSetupApply = templates.NewSetupApply,
            refreshAuthDryRun = templates.RefreshAuthDryRun,
            refreshAuthApply = templates.RefreshAuthApply,
            cleanupDryRun = templates.CleanupDryRun,
            cleanupApply = templates.CleanupApply,
            maintenanceWizard = templates.MaintenanceWizard
        };
    }

    private static object BuildSetupHints(bool includeMaintenancePath) {
        return new {
            contractVersion = SetupOnboardingContract.ContractVersion,
            contractFingerprint = SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath),
            paths = BuildPaths(includeMaintenancePath),
            commandTemplates = BuildCommandTemplates()
        };
    }

    private static object[] BuildPaths(bool includeMaintenancePath) {
        var paths = SetupOnboardingContract.GetPaths(includeMaintenancePath);
        var results = new object[paths.Count];
        for (var i = 0; i < paths.Count; i++) {
            var path = paths[i];
            results[i] = new {
                id = path.Id,
                operation = path.Operation,
                requiresGitHubAuth = path.RequiresGitHubAuth,
                requiresRepoSelection = path.RequiresRepoSelection,
                requiresAiAuth = path.RequiresAiAuth
            };
        }

        return results;
    }
}
