using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using MailKit;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Fetches a single IMAP message by UID, including bodies (truncated).
/// </summary>
public sealed class EmailImapGetTool : EmailToolBase, ITool {
    private sealed record GetRequest(
        long Uid,
        string? Folder,
        long MaxBodyBytes);

    private static readonly ToolDefinition DefinitionValue = new(
        "email_imap_get",
        "Fetch a single IMAP message by UID (returns headers + text/html bodies, truncated).",
        ToolSchema.Object(
                ("folder", ToolSchema.String("Optional folder name (defaults to configured default or Inbox).")),
                ("uid", ToolSchema.Integer("IMAP UID of the message.")),
                ("max_body_bytes", ToolSchema.Integer("Optional maximum bytes per body (capped).")))
            .Required("uid")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailImapGetTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public EmailImapGetTool(EmailToolOptions options) : base(options) { }

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
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<GetRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!TryReadPositiveInt64(arguments, "uid", out var uid)) {
                return ToolRequestBindingResult<GetRequest>.Failure("uid must be a positive integer.");
            }

            return ToolRequestBindingResult<GetRequest>.Success(new GetRequest(
                Uid: uid,
                Folder: reader.OptionalString("folder"),
                MaxBodyBytes: reader.CappedInt64("max_body_bytes", Options.MaxBodyBytes, 1, Options.MaxBodyBytes)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<GetRequest> context, CancellationToken cancellationToken) {
        try {
            var imap = Options.Imap;
            if (imap is null) {
                return ToolResultV2.Error("not_configured", "IMAP is not configured.");
            }
            imap.Validate();

            var request = context.Request;
            var folder = request.Folder ?? imap.DefaultFolder;
            var maxBodyBytes = request.MaxBodyBytes;

            using var client = await ImapSessionService
                .ConnectAsync(EmailSessionRequests.BuildImapSessionRequest(imap), cancellationToken)
                .ConfigureAwait(false);
            try {
                var message = await ImapMessageReader
                    .ReadAsync(
                        client,
                        new ImapMessageReadRequest(
                            Uid: new MailKit.UniqueId((uint)request.Uid),
                            Folder: folder,
                            MaxBodyBytes: maxBodyBytes),
                        cancellationToken)
                    .ConfigureAwait(false);

                var attachments = message.Attachments.Select(static attachment => new {
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType
                }).ToArray();

                var root = new {
                    Uid = message.Uid,
                    Folder = message.Folder,
                    Subject = message.Subject,
                    From = message.From,
                    To = message.To,
                    DateUtc = message.DateUtc.ToString("O"),
                    TextBody = message.TextBody,
                    TextTruncated = message.TextTruncated,
                    HtmlBody = message.HtmlBody,
                    HtmlTruncated = message.HtmlTruncated,
                    HasAttachments = message.HasAttachments,
                    Attachments = attachments
                };

                var summaryPreview = message.TextBody;
                const int previewMax = 1000;
                if (summaryPreview.Length > previewMax) {
                    summaryPreview = summaryPreview.Substring(0, previewMax) + "...";
                }

                var summaryMarkdown = ToolMarkdown.SummaryFacts(
                    title: "IMAP message",
                    facts: new (string Key, string Value)[] {
                        ("UID", message.Uid.ToString()),
                        ("Folder", message.Folder),
                        ("Subject", message.Subject),
                        ("From", message.From),
                        ("Date (UTC)", message.DateUtc.ToString("O")),
                        ("Attachments", message.HasAttachments ? "yes" : "no"),
                        ("Text truncated", message.TextTruncated ? "yes" : "no"),
                        ("HTML truncated", message.HtmlTruncated ? "yes" : "no")
                    },
                    codeLanguage: "text",
                    codeContent: summaryPreview);

                var meta = ToolOutputHints.Meta(count: 1, truncated: message.TextTruncated || message.HtmlTruncated)
                    .Add("max_body_bytes", maxBodyBytes)
                    .Add("text_truncated", message.TextTruncated)
                    .Add("html_truncated", message.HtmlTruncated)
                    .Add("attachments_count", attachments.Length);

                return ToolResultV2.OkModel(
                    model: root,
                    meta: meta,
                    summaryMarkdown: summaryMarkdown,
                    render: ToolOutputHints.RenderCode(language: "text", contentPath: "text_body"));
            } finally {
                try {
                    if (client.IsConnected) {
                        client.Disconnect(true);
                    }
                } catch {
                    // best-effort
                }
            }
        } catch (FolderNotFoundException ex) {
            return ToolResultV2.Error("not_found", $"Folder not found: {ex.Message}");
        }
    }

    private static bool TryReadPositiveInt64(JsonObject? arguments, string key, out long value) {
        value = 0;
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var parsed = kv.Value.AsInt64();
            if (parsed.HasValue && parsed.Value > 0) {
                value = parsed.Value;
                return true;
            }

            return false;
        }

        return false;
    }
}
