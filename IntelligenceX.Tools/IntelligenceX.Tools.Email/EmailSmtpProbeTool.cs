using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Verifies SMTP connectivity/authentication and returns a probe id for strict send gating.
/// </summary>
public sealed class EmailSmtpProbeTool : EmailToolBase, ITool {
    private sealed record ProbeRequest;
    private static readonly ToolPipelineReliabilityOptions ReliabilityOptions = new() {
        MaxAttempts = 3,
        RetryTransientErrors = true,
        RetryExceptions = true,
        AttemptTimeoutMs = 12_000,
        BaseDelayMs = 150,
        MaxDelayMs = 1_000,
        JitterRatio = 0.10d,
        EnableCircuitBreaker = true,
        CircuitFailureThreshold = 4,
        CircuitOpenMs = 10_000
    };

    private static readonly ToolDefinition DefinitionValue = new(
        SmtpProbePolicy.ProbeToolName,
        "Validate SMTP connectivity/authentication and return auth_probe_id for strict send workflows.",
        ToolSchema.Object()
            .NoAdditionalProperties(),
        authentication: ToolAuthenticationConventions.HostManaged(
            requiresAuthentication: true));

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailSmtpProbeTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public EmailSmtpProbeTool(EmailToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteRequestAsync,
            reliability: ReliabilityOptions,
            middleware: new ToolPipelineMiddleware<ProbeRequest>[] {
                EnsureSmtpConfiguredAsync
            }).ConfigureAwait(false);
    }

    private static ToolRequestBindingResult<ProbeRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBindingResult<ProbeRequest>.Success(new ProbeRequest());
    }

    private async Task<string> ExecuteRequestAsync(
        ToolPipelineContext<ProbeRequest> context,
        CancellationToken cancellationToken) {
        if (!context.TryGetItem<SmtpAccountOptions>(SmtpOptionsContextKey, out var smtpOptions) ||
            smtpOptions is null) {
            return ToolResultV2.Error("not_configured", "SMTP is not configured.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var smtp = SmtpClientFactory.Create(smtpOptions, dryRun: true);

        try {
            var connectAuthResult = await SmtpClientFactory.ConnectAndAuthenticateAsync(smtp, smtpOptions).ConfigureAwait(false);
            if (!connectAuthResult.IsSuccess) {
                return ToolResultV2.Error(
                    connectAuthResult.ErrorCode,
                    connectAuthResult.Error,
                    isTransient: connectAuthResult.IsTransient);
            }

            var now = DateTimeOffset.UtcNow;
            var probeRecord = SmtpProbePolicy.CreateSuccessRecord(smtpOptions, now);
            Options.AuthenticationProbeStore.Upsert(probeRecord);

            var root = new {
                ProbeId = probeRecord.ProbeId,
                Succeeded = true,
                Provider = "smtp",
                Server = smtpOptions.Server,
                Port = smtpOptions.Port,
                SecureSocketOptions = connectAuthResult.SecureSocketOptions.ToString(),
                UseSsl = smtpOptions.UseSsl,
                ProbeCreatedUtc = probeRecord.ProbedAtUtc.ToString("O"),
                ProbeMaxAgeSeconds = Options.SmtpProbeMaxAgeSeconds
            };

            var summaryFacts = new List<(string Key, string Value)> {
                ("Probe ID", probeRecord.ProbeId),
                ("Server", $"{smtpOptions.Server}:{smtpOptions.Port}"),
                ("Secure socket options", connectAuthResult.SecureSocketOptions.ToString()),
                ("Use SSL", smtpOptions.UseSsl ? "true" : "false"),
                ("Probe max age (seconds)", Options.SmtpProbeMaxAgeSeconds.ToString())
            };

            var meta = ToolOutputHints.Meta(count: 1, truncated: false)
                .Add("provider", "smtp")
                .Add("auth_probe_id", probeRecord.ProbeId);

            return ToolResultV2.OkFactsModel(
                model: root,
                title: "SMTP probe",
                facts: summaryFacts,
                meta: meta);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResultV2.Error(
                "smtp_probe_failed",
                $"SMTP probe failed. {ex.Message}",
                isTransient: true);
        } finally {
            SmtpClientFactory.DisposeQuietly(smtp);
        }
    }
}
