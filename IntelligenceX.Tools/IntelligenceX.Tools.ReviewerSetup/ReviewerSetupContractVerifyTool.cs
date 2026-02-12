using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Setup.Onboarding;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Verifies onboarding contract metadata from setup autodetect against the canonical contract.
/// </summary>
public sealed class ReviewerSetupContractVerifyTool : ReviewerSetupToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "reviewer_setup_contract_verify",
        "Compare setup autodetect contract metadata with the canonical reviewer setup contract and report drift/mismatch risk.",
        ToolSchema.Object(
                ("autodetect_contract_version", ToolSchema.String("Required. `contractVersion` value from `intelligencex setup autodetect --json`.")),
                ("autodetect_contract_fingerprint", ToolSchema.String("Required. `contractFingerprint` value from `intelligencex setup autodetect --json`.")),
                ("pack_contract_version", ToolSchema.String("Optional. `setup_hints.contract_version` value from `reviewer_setup_pack_info`.")),
                ("pack_contract_fingerprint", ToolSchema.String("Optional. `setup_hints.contract_fingerprint` value from `reviewer_setup_pack_info`.")),
                ("include_maintenance_path", ToolSchema.Boolean("Optional. Defaults to this pack's maintenance setting.")))
            .Required("autodetect_contract_version", "autodetect_contract_fingerprint")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="ReviewerSetupContractVerifyTool"/> class.
    /// </summary>
    public ReviewerSetupContractVerifyTool(ReviewerSetupToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var autodetectContractVersion = ToolArgs.GetOptionalTrimmed(arguments, "autodetect_contract_version");
        var autodetectContractFingerprint = ToolArgs.GetOptionalTrimmed(arguments, "autodetect_contract_fingerprint");
        if (string.IsNullOrWhiteSpace(autodetectContractVersion)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "autodetect_contract_version is required."));
        }
        if (string.IsNullOrWhiteSpace(autodetectContractFingerprint)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "autodetect_contract_fingerprint is required."));
        }

        var packContractVersion = ToolArgs.GetOptionalTrimmed(arguments, "pack_contract_version");
        var packContractFingerprint = ToolArgs.GetOptionalTrimmed(arguments, "pack_contract_fingerprint");
        var includeMaintenancePath = ToolArgs.GetBoolean(arguments, "include_maintenance_path", defaultValue: Options.IncludeMaintenancePath);
        SetupOnboardingContractVerificationResult verification;
        try {
            verification = SetupOnboardingContractVerification.Verify(
                autodetectContractVersion: autodetectContractVersion!,
                autodetectContractFingerprint: autodetectContractFingerprint!,
                packContractVersion: packContractVersion,
                packContractFingerprint: packContractFingerprint,
                includeMaintenancePath: includeMaintenancePath);
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", ex.Message));
        }

        var status = verification.IsMatch ? "match" : "mismatch";
        var requiresSync = !verification.IsMatch;
        var nextActions = requiresSync
            ? new[] {
                "Contract metadata mismatch detected. Update/sync IntelligenceX CLI and reviewer setup tools before running setup/update-secret/cleanup apply commands.",
                "Re-run `intelligencex setup autodetect --json`, then call `reviewer_setup_pack_info` and `reviewer_setup_contract_verify` again."
            }
            : new[] {
                "Contract metadata matches canonical onboarding contract.",
                "Proceed with path-specific setup commands (dry-run first, then apply with explicit confirmation)."
            };

        var result = new ReviewerSetupContractVerificationResult {
            Status = status,
            RequiresSync = requiresSync,
            IncludeMaintenancePath = verification.IncludeMaintenancePath,
            ExpectedContractVersion = verification.ExpectedContractVersion,
            ExpectedContractFingerprint = verification.ExpectedContractFingerprint,
            AutodetectContractVersion = verification.AutodetectContractVersion,
            AutodetectContractFingerprint = verification.AutodetectContractFingerprint,
            PackContractVersion = verification.PackContractVersion,
            PackContractFingerprint = verification.PackContractFingerprint,
            MismatchCount = verification.MismatchCount,
            Mismatches = verification.Mismatches,
            NextActions = nextActions
        };

        var summary = requiresSync
            ? ToolMarkdown.SummaryText(
                title: "Reviewer Setup Contract Verification",
                "Mismatch detected between provided contract metadata and the canonical onboarding contract.",
                "Sync/update tooling, then re-run autodetect and verification before applying repo changes.")
            : ToolMarkdown.SummaryText(
                title: "Reviewer Setup Contract Verification",
                "Contract metadata matches the canonical onboarding contract.",
                "Safe to continue with selected onboarding path (dry-run first).");

        return Task.FromResult(ToolResponse.OkModel(result, summaryMarkdown: summary));
    }

    private sealed class ReviewerSetupContractVerificationResult {
        public required string Status { get; init; }
        public required bool RequiresSync { get; init; }
        public required bool IncludeMaintenancePath { get; init; }
        public required string ExpectedContractVersion { get; init; }
        public required string ExpectedContractFingerprint { get; init; }
        public required string AutodetectContractVersion { get; init; }
        public required string AutodetectContractFingerprint { get; init; }
        public string? PackContractVersion { get; init; }
        public string? PackContractFingerprint { get; init; }
        public required int MismatchCount { get; init; }
        public required IReadOnlyList<SetupOnboardingContractMismatch> Mismatches { get; init; }
        public required IReadOnlyList<string> NextActions { get; init; }
    }
}
