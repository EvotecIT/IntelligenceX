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
/// Sends an email via SMTP. Defaults to dry-run unless explicitly confirmed.
/// </summary>
public sealed class EmailSmtpSendTool : EmailToolBase, ITool {
    private sealed record SendRequest(
        string From,
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Bcc,
        string? ReplyTo,
        string Subject,
        string TextBody,
        string HtmlBody,
        bool Send,
        string? ProbeId);

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
            .Required("from", "to", "subject")
            .WithWriteGovernanceAndAuthenticationProbe(
                description: "Optional auth probe id from email_smtp_probe. Required only when strict probe gating is enabled."),
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
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteRequestAsync,
            middleware: new ToolPipelineMiddleware<SendRequest>[] {
                EnsureSmtpConfiguredAsync,
                ValidateStrictProbeAsync
            }).ConfigureAwait(false);
    }

    private static ToolRequestBindingResult<SendRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("from", out var from, out var fromError)) {
                return ToolRequestBindingResult<SendRequest>.Failure(fromError);
            }

            var to = reader.StringArray("to");
            if (to.Count == 0) {
                return ToolRequestBindingResult<SendRequest>.Failure("to must contain at least one recipient.");
            }

            if (!reader.TryReadRequiredString("subject", out var subject, out var subjectError)) {
                return ToolRequestBindingResult<SendRequest>.Failure(subjectError);
            }

            var textBody = reader.OptionalString("text_body") ?? string.Empty;
            var htmlBody = reader.OptionalString("html_body") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(textBody) && string.IsNullOrWhiteSpace(htmlBody)) {
                return ToolRequestBindingResult<SendRequest>.Failure("Either text_body or html_body must be provided.");
            }

            return ToolRequestBindingResult<SendRequest>.Success(new SendRequest(
                From: from,
                To: to,
                Cc: reader.StringArray("cc"),
                Bcc: reader.StringArray("bcc"),
                ReplyTo: reader.OptionalString("reply_to"),
                Subject: subject,
                TextBody: textBody,
                HtmlBody: htmlBody,
                Send: reader.Boolean("send"),
                ProbeId: reader.OptionalString(ToolAuthenticationArgumentNames.ProbeId)));
        });
    }

    private Task<string> ValidateStrictProbeAsync(
        ToolPipelineContext<SendRequest> context,
        CancellationToken cancellationToken,
        ToolPipelineNext<SendRequest> next) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.Request.Send) {
            return next(context, cancellationToken);
        }

        if (!context.TryGetItem<SmtpAccountOptions>(SmtpOptionsContextKey, out var smtpOptions) ||
            smtpOptions is null) {
            return Task.FromResult(ToolResultV2.Error("not_configured", "SMTP is not configured."));
        }

        if (!SmtpProbePolicy.TryValidateForStrictSend(
                options: Options,
                smtpOptions: smtpOptions,
                probeId: context.Request.ProbeId,
                nowUtc: DateTimeOffset.UtcNow,
                out var probeErrorCode,
                out var probeError)) {
            return Task.FromResult(ToolResultV2.Error(probeErrorCode, probeError));
        }

        return next(context, cancellationToken);
    }

    private async Task<string> ExecuteRequestAsync(
        ToolPipelineContext<SendRequest> context,
        CancellationToken cancellationToken) {
        if (!context.TryGetItem<SmtpAccountOptions>(SmtpOptionsContextKey, out var smtpOptions) ||
            smtpOptions is null) {
            return ToolResultV2.Error("not_configured", "SMTP is not configured.");
        }

        var request = context.Request;
        var smtp = SmtpClientFactory.Create(smtpOptions, dryRun: !request.Send);

        try {
            smtp.From = request.From;
            smtp.To = new List<string>(request.To);
            if (request.Cc.Count > 0) smtp.Cc = new List<string>(request.Cc);
            if (request.Bcc.Count > 0) smtp.Bcc = new List<string>(request.Bcc);
            if (!string.IsNullOrWhiteSpace(request.ReplyTo)) smtp.ReplyTo = request.ReplyTo;
            smtp.Subject = request.Subject;
            smtp.TextBody = request.TextBody;
            smtp.HtmlBody = request.HtmlBody;

            // Ensure the MIME message exists before attempting send.
            await smtp.CreateMessageAsync(cancellationToken).ConfigureAwait(false);

            var connectAuthResult = await SmtpClientFactory.ConnectAndAuthenticateAsync(smtp, smtpOptions).ConfigureAwait(false);
            if (!connectAuthResult.IsSuccess) {
                return ToolResultV2.Error(
                    connectAuthResult.ErrorCode,
                    connectAuthResult.Error,
                    isTransient: connectAuthResult.IsTransient);
            }

            var sendResult = await smtp.SendAsync(cancellationToken).ConfigureAwait(false);
            if (!sendResult.Status) {
                return ToolResultV2.Error("send_failed", sendResult.Error ?? "Send failed", isTransient: true);
            }

            var root = new {
                Sent = request.Send,
                Provider = "smtp",
                Server = smtpOptions.Server,
                Port = smtpOptions.Port,
                MessageId = sendResult.MessageId ?? string.Empty,
                ProbeId = request.ProbeId ?? string.Empty
            };

            var summaryItems = new List<(string Key, string Value)> {
                ("From", request.From),
                ("To", string.Join("; ", request.To)),
                ("Subject", request.Subject),
                ("Server", $"{smtpOptions.Server}:{smtpOptions.Port}"),
                ("Message ID", sendResult.MessageId ?? string.Empty)
            };
            if (request.Cc.Count > 0) {
                summaryItems.Add(("Cc", string.Join("; ", request.Cc)));
            }
            if (request.Bcc.Count > 0) {
                summaryItems.Add(("Bcc", string.Join("; ", request.Bcc)));
            }
            if (!string.IsNullOrWhiteSpace(request.ProbeId)) {
                summaryItems.Add(("Probe ID", request.ProbeId));
            }

            var meta = ToolOutputHints.Meta(count: 1, truncated: false)
                .Add("sent", request.Send)
                .Add("provider", "smtp");
            if (!string.IsNullOrWhiteSpace(request.ProbeId)) {
                meta.Add("auth_probe_id", request.ProbeId);
            }

            return ToolResultV2.OkWriteActionModel(
                model: root,
                action: "smtp_send",
                writeApplied: request.Send,
                facts: summaryItems,
                meta: meta,
                summaryTitle: "SMTP send");
        } finally {
            SmtpClientFactory.DisposeQuietly(smtp);
        }
    }
}
