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
/// Sends an email via SMTP. Defaults to dry-run unless explicitly confirmed.
/// </summary>
public sealed class EmailSmtpSendTool : EmailToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "email_smtp_send",
        "Send an email via SMTP. By default performs a dry-run unless send=true is provided.",
        ToolSchema.Object(
                ("from", ToolSchema.String("From address.")),
                ("to", ToolSchema.Array(ToolSchema.String(), "To recipients.")),
                ("cc", ToolSchema.Array(ToolSchema.String(), "Cc recipients.")),
                ("bcc", ToolSchema.Array(ToolSchema.String(), "Bcc recipients.")),
                ("reply_to", ToolSchema.String("Reply-To address.")),
                ("subject", ToolSchema.String("Subject.")),
                ("text_body", ToolSchema.String("Plain text body.")),
                ("html_body", ToolSchema.String("HTML body.")),
                ("send", ToolSchema.Boolean("When true, actually sends. Otherwise dry-run.")))
            .WithAuthenticationProbeReference(
                description: "Optional auth probe id from email_smtp_probe. Required only when strict probe gating is enabled.")
            .Required("from", "to", "subject")
            .WithWriteGovernanceMetadata()
            .NoAdditionalProperties(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "send",
            confirmationArgumentName: "send"),
        authentication: ToolAuthenticationConventions.HostManaged(
            requiresAuthentication: true,
            supportsConnectivityProbe: true,
            probeToolName: SmtpProbePolicy.ProbeToolName));

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailSmtpSendTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public EmailSmtpSendTool(EmailToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string result.</returns>
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var smtpOptions = Options.Smtp;
        if (smtpOptions is null) {
            return ToolResponse.Error("not_configured", "SMTP is not configured.");
        }
        smtpOptions.Validate();

        var from = arguments?.GetString("from") ?? string.Empty;
        var to = ToolArgs.ReadStringArray(arguments?.GetArray("to"));
        var cc = ToolArgs.ReadStringArray(arguments?.GetArray("cc"));
        var bcc = ToolArgs.ReadStringArray(arguments?.GetArray("bcc"));
        var replyTo = arguments?.GetString("reply_to");
        var subject = arguments?.GetString("subject") ?? string.Empty;
        var textBody = arguments?.GetString("text_body") ?? string.Empty;
        var htmlBody = arguments?.GetString("html_body") ?? string.Empty;
        var send = arguments?.GetBoolean("send") ?? false;
        var probeId = ToolArgs.GetOptionalTrimmed(arguments, ToolAuthenticationArgumentNames.ProbeId);

        if (string.IsNullOrWhiteSpace(from)) {
            return ToolResponse.Error("invalid_argument", "from is required.");
        }
        if (to.Count == 0) {
            return ToolResponse.Error("invalid_argument", "to must contain at least one recipient.");
        }
        if (string.IsNullOrWhiteSpace(subject)) {
            return ToolResponse.Error("invalid_argument", "subject is required.");
        }
        if (string.IsNullOrWhiteSpace(textBody) && string.IsNullOrWhiteSpace(htmlBody)) {
            return ToolResponse.Error("invalid_argument", "Either text_body or html_body must be provided.");
        }
        if (send && !SmtpProbePolicy.TryValidateForStrictSend(
                options: Options,
                smtpOptions: smtpOptions,
                probeId: probeId,
                nowUtc: DateTimeOffset.UtcNow,
                out var probeErrorCode,
                out var probeError)) {
            return ToolResponse.Error(probeErrorCode, probeError);
        }

        var secure = ParseSecureSocketOptions(smtpOptions.SecureSocketOptions);
        var smtp = new Smtp {
            DryRun = !send,
            Timeout = smtpOptions.TimeoutMs,
            RetryCount = smtpOptions.RetryCount
        };

        try {
            smtp.From = from;
            smtp.To = to;
            if (cc.Count > 0) smtp.Cc = cc;
            if (bcc.Count > 0) smtp.Bcc = bcc;
            if (!string.IsNullOrWhiteSpace(replyTo)) smtp.ReplyTo = replyTo;
            smtp.Subject = subject;
            smtp.TextBody = textBody;
            smtp.HtmlBody = htmlBody;

            // Ensure the MIME message exists before attempting send.
            await smtp.CreateMessageAsync(cancellationToken).ConfigureAwait(false);

            var connectResult = await smtp.ConnectAsync(smtpOptions.Server, smtpOptions.Port, secure, smtpOptions.UseSsl).ConfigureAwait(false);
            if (!connectResult.Status) {
                return ToolResponse.Error("connect_failed", connectResult.Error ?? "Connect failed.", isTransient: true);
            }

            var authResult = smtp.Authenticate(new NetworkCredential(smtpOptions.UserName, smtpOptions.Password));
            if (!authResult.Status) {
                return ToolResponse.Error("auth_failed", authResult.Error ?? "Authentication failed.", isTransient: false);
            }

            var sendResult = await smtp.SendAsync(cancellationToken).ConfigureAwait(false);
            if (!sendResult.Status) {
                return ToolResponse.Error("send_failed", sendResult.Error ?? "Send failed", isTransient: true);
            }

            var root = new {
                Sent = send,
                Provider = "smtp",
                Server = smtpOptions.Server,
                Port = smtpOptions.Port,
                MessageId = sendResult.MessageId ?? string.Empty,
                ProbeId = probeId ?? string.Empty
            };

            var summaryItems = new List<(string Key, string Value)> {
                ("From", from),
                ("To", string.Join("; ", to)),
                ("Subject", subject),
                ("Server", $"{smtpOptions.Server}:{smtpOptions.Port}"),
                ("Message ID", sendResult.MessageId ?? string.Empty)
            };
            if (cc.Count > 0) {
                summaryItems.Add(("Cc", string.Join("; ", cc)));
            }
            if (bcc.Count > 0) {
                summaryItems.Add(("Bcc", string.Join("; ", bcc)));
            }
            if (!string.IsNullOrWhiteSpace(probeId)) {
                summaryItems.Add(("Probe ID", probeId));
            }

            var meta = ToolOutputHints.Meta(count: 1, truncated: false)
                .Add("sent", send)
                .Add("provider", "smtp");
            if (!string.IsNullOrWhiteSpace(probeId)) {
                meta.Add("auth_probe_id", probeId);
            }

            return ToolResponse.OkWriteActionModel(
                model: root,
                action: "smtp_send",
                writeApplied: send,
                facts: summaryItems,
                meta: meta,
                summaryTitle: "SMTP send");
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
