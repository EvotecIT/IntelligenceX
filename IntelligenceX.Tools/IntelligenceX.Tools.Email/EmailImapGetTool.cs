using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using MailKit;
using MailKit.Net.Imap;
using MimeKit;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Fetches a single IMAP message by UID, including bodies (truncated).
/// </summary>
public sealed class EmailImapGetTool : EmailToolBase, ITool {
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
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        try {
            var imap = Options.Imap;
            if (imap is null) {
                return ToolResponse.Error("not_configured", "IMAP is not configured.");
            }
            imap.Validate();

            var uidValue = arguments?.GetInt64("uid");
            if (!uidValue.HasValue || uidValue.Value <= 0) {
                return ToolResponse.Error("invalid_argument", "uid must be a positive integer.");
            }

            var folder = arguments?.GetString("folder") ?? imap.DefaultFolder;
            var maxBodyBytes = ToolArgs.GetCappedInt64(arguments, "max_body_bytes", Options.MaxBodyBytes, 1, Options.MaxBodyBytes);

            using var client = await ImapClientFactory.ConnectAsync(imap, cancellationToken).ConfigureAwait(false);
            try {
                var mailFolder = ResolveFolder(client, folder);
                await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
                var uid = new UniqueId((uint)uidValue.Value);
                var message = await mailFolder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);

                var attachments = message.Attachments.Select(static a => a is MimePart part
                    ? new {
                        FileName = part.FileName ?? string.Empty,
                        ContentType = part.ContentType?.MimeType ?? string.Empty
                    }
                    : new {
                        FileName = string.Empty,
                        ContentType = a.ContentType?.MimeType ?? string.Empty
                    }).ToArray();

                var text = TruncateUtf8(message.TextBody, maxBodyBytes, out var textTruncated);
                var html = TruncateUtf8(message.HtmlBody, maxBodyBytes, out var htmlTruncated);
                var fromText = string.Join(", ", message.From.Mailboxes.Select(static m => m.ToString()));
                var toText = string.Join(", ", message.To.Mailboxes.Select(static m => m.ToString()));
                var hasAttachments = attachments.Length > 0;

                var root = new {
                    Uid = (long)uid.Id,
                    Folder = folder ?? string.Empty,
                    Subject = message.Subject ?? string.Empty,
                    From = fromText,
                    To = toText,
                    DateUtc = message.Date.UtcDateTime.ToString("O"),
                    TextBody = text ?? string.Empty,
                    TextTruncated = textTruncated,
                    HtmlBody = html ?? string.Empty,
                    HtmlTruncated = htmlTruncated,
                    HasAttachments = hasAttachments,
                    Attachments = attachments
                };

                var summaryPreview = text ?? string.Empty;
                const int previewMax = 1000;
                if (summaryPreview.Length > previewMax) {
                    summaryPreview = summaryPreview.Substring(0, previewMax) + "...";
                }

                var summaryMarkdown = ToolMarkdown.SummaryFacts(
                    title: "IMAP message",
                    facts: new (string Key, string Value)[] {
                        ("UID", uid.Id.ToString()),
                        ("Folder", folder ?? string.Empty),
                        ("Subject", message.Subject ?? string.Empty),
                        ("From", fromText),
                        ("Date (UTC)", message.Date.UtcDateTime.ToString("O")),
                        ("Attachments", hasAttachments ? "yes" : "no"),
                        ("Text truncated", textTruncated ? "yes" : "no"),
                        ("HTML truncated", htmlTruncated ? "yes" : "no")
                    },
                    codeLanguage: "text",
                    codeContent: summaryPreview);

                var meta = ToolOutputHints.Meta(count: 1, truncated: textTruncated || htmlTruncated)
                    .Add("max_body_bytes", maxBodyBytes)
                    .Add("text_truncated", textTruncated)
                    .Add("html_truncated", htmlTruncated)
                    .Add("attachments_count", attachments.Length);

                return ToolResponse.OkModel(
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
            return ToolResponse.Error("not_found", $"Folder not found: {ex.Message}");
        }
    }

    private static IMailFolder ResolveFolder(ImapClient client, string? folder) {
        if (string.IsNullOrWhiteSpace(folder)) {
            return client.Inbox;
        }
        try {
            return client.GetFolder(folder);
        } catch (FolderNotFoundException) {
            if (client.PersonalNamespaces.Count == 0) {
                throw;
            }
            return client.GetFolder(client.PersonalNamespaces[0]).GetSubfolder(folder);
        }
    }

    private static string? TruncateUtf8(string? value, long maxBytes, out bool truncated) {
        truncated = false;
        if (string.IsNullOrEmpty(value)) {
            return value;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.LongLength <= maxBytes) {
            return value;
        }
        truncated = true;
        var slice = bytes.AsSpan(0, (int)Math.Min(maxBytes, int.MaxValue));
        return Encoding.UTF8.GetString(slice);
    }

}
