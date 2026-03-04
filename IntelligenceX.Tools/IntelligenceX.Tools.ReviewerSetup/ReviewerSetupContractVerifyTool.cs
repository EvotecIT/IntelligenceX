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
    private sealed record ContractVerifyRequest(
        string AutodetectContractVersion,
        string AutodetectContractFingerprint,
        string? PackContractVersion,
        string? PackContractFingerprint,
        bool IncludeMaintenancePath);

    private readonly ToolRequestAdapter<ContractVerifyRequest> _adapter;

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
            .NoAdditionalProperties(),
        routing: new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "reviewer_setup",
            Role = ToolRoutingTaxonomy.RoleDiagnostic
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="ReviewerSetupContractVerifyTool"/> class.
    /// </summary>
    public ReviewerSetupContractVerifyTool(ReviewerSetupToolOptions options) : base(options) {
        _adapter = new ContractVerifyAdapter(options.IncludeMaintenancePath, ExecuteAsync);
    }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            adapter: _adapter);
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ContractVerifyRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        SetupOnboardingContractVerificationResult verification;
        try {
            verification = SetupOnboardingContractVerification.Verify(
                autodetectContractVersion: request.AutodetectContractVersion,
                autodetectContractFingerprint: request.AutodetectContractFingerprint,
                packContractVersion: request.PackContractVersion,
                packContractFingerprint: request.PackContractFingerprint,
                includeMaintenancePath: request.IncludeMaintenancePath);
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResultV2.Error("invalid_argument", ex.Message));
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

        return Task.FromResult(ToolResultV2.OkModel(result, summaryMarkdown: summary));
    }

    private sealed class ContractVerifyAdapter : ToolRequestAdapter<ContractVerifyRequest> {
        private readonly bool _defaultIncludeMaintenancePath;
        private readonly Func<ToolPipelineContext<ContractVerifyRequest>, CancellationToken, Task<string>> _execute;

        public ContractVerifyAdapter(
            bool defaultIncludeMaintenancePath,
            Func<ToolPipelineContext<ContractVerifyRequest>, CancellationToken, Task<string>> execute) {
            _defaultIncludeMaintenancePath = defaultIncludeMaintenancePath;
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public override ToolRequestBindingResult<ContractVerifyRequest> Bind(JsonObject? arguments) {
            return ToolRequestBinder.Bind(arguments, reader => {
                if (!reader.TryReadRequiredString("autodetect_contract_version", out var autodetectContractVersion, out var versionError)) {
                    return ToolRequestBindingResult<ContractVerifyRequest>.Failure(versionError);
                }

                if (!reader.TryReadRequiredString("autodetect_contract_fingerprint", out var autodetectContractFingerprint, out var fingerprintError)) {
                    return ToolRequestBindingResult<ContractVerifyRequest>.Failure(fingerprintError);
                }

                return ToolRequestBindingResult<ContractVerifyRequest>.Success(new ContractVerifyRequest(
                    AutodetectContractVersion: autodetectContractVersion,
                    AutodetectContractFingerprint: autodetectContractFingerprint,
                    PackContractVersion: reader.OptionalString("pack_contract_version"),
                    PackContractFingerprint: reader.OptionalString("pack_contract_fingerprint"),
                    IncludeMaintenancePath: reader.Boolean("include_maintenance_path", defaultValue: _defaultIncludeMaintenancePath)));
            });
        }

        public override Task<string> ExecuteAsync(ToolPipelineContext<ContractVerifyRequest> context, CancellationToken cancellationToken) {
            return _execute(context, cancellationToken);
        }
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
