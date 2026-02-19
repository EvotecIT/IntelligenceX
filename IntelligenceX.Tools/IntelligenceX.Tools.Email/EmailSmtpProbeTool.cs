using System;
using System.Collections.Generic;
using System.Net;
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
        var smtpOptions = Options.Smtp;
        if (smtpOptions is null) {
            return ToolResponse.Error("not_configured", "SMTP is not configured.");
        }
        smtpOptions.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var secure = ParseSecureSocketOptions(smtpOptions.SecureSocketOptions);
        var smtp = new Smtp {
            DryRun = true,
            Timeout = smtpOptions.TimeoutMs,
            RetryCount = smtpOptions.RetryCount
        };

        try {
            var connectResult = await smtp.ConnectAsync(
                    smtpOptions.Server,
                    smtpOptions.Port,
                    secure,
                    smtpOptions.UseSsl)
                .ConfigureAwait(false);
            if (!connectResult.Status) {
                return ToolResponse.Error(
                    "connect_failed",
                    connectResult.Error ?? "Connect failed.",
                    isTransient: true);
            }

            var authResult = smtp.Authenticate(new NetworkCredential(smtpOptions.UserName, smtpOptions.Password));
            if (!authResult.Status) {
                return ToolResponse.Error(
                    "auth_failed",
                    authResult.Error ?? "Authentication failed.",
                    isTransient: false);
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
                SecureSocketOptions = secure.ToString(),
                UseSsl = smtpOptions.UseSsl,
                ProbeCreatedUtc = probeRecord.ProbedAtUtc.ToString("O"),
                ProbeMaxAgeSeconds = Options.SmtpProbeMaxAgeSeconds
            };

            var summaryFacts = new List<(string Key, string Value)> {
                ("Probe ID", probeRecord.ProbeId),
                ("Server", $"{smtpOptions.Server}:{smtpOptions.Port}"),
                ("Secure socket options", secure.ToString()),
                ("Use SSL", smtpOptions.UseSsl ? "true" : "false"),
                ("Probe max age (seconds)", Options.SmtpProbeMaxAgeSeconds.ToString())
            };

            var meta = ToolOutputHints.Meta(count: 1, truncated: false)
                .Add("provider", "smtp")
                .Add("auth_probe_id", probeRecord.ProbeId);

            return ToolResponse.OkFactsModel(
                model: root,
                title: "SMTP probe",
                facts: summaryFacts,
                meta: meta);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error(
                "smtp_probe_failed",
                $"SMTP probe failed. {ex.Message}",
                isTransient: true);
        } finally {
            try {
                smtp.Disconnect();
            } catch {
                // best-effort
            }
            try {
                smtp.Dispose();
            } catch {
                // best-effort
            }
        }
    }
}
