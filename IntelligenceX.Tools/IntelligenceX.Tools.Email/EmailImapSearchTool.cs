using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Search an IMAP mailbox and return message metadata (safe: does not return full bodies).
/// </summary>
public sealed class EmailImapSearchTool : EmailToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "email_imap_search",
        "Search an IMAP mailbox and return message metadata (uid, from, to, subject, date).",
        ToolSchema.Object(
                ("folder", ToolSchema.String("Optional folder name (defaults to configured default or Inbox).")),
                ("subject_contains", ToolSchema.String()),
                ("from_contains", ToolSchema.String()),
                ("to_contains", ToolSchema.String()),
                ("body_contains", ToolSchema.String()),
                ("has_attachment", ToolSchema.Boolean()),
                ("since_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("before_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("query", ToolSchema.String("Optional Mailozaurr query string (e.g. subject:\"x\" since:2025-01-01).")),
                ("max_results", ToolSchema.Integer("Optional maximum results (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailImapSearchTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public EmailImapSearchTool(EmailToolOptions options) : base(options) { }

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
        var imap = Options.Imap;
        if (imap is null) {
            return ToolResponse.Error("not_configured", "IMAP is not configured.");
        }
        imap.Validate();

        var folder = arguments?.GetString("folder") ?? imap.DefaultFolder;
        var subject = arguments?.GetString("subject_contains");
        var from = arguments?.GetString("from_contains");
        var to = arguments?.GetString("to_contains");
        var body = arguments?.GetString("body_contains");
        var hasAttachment = arguments?.GetBoolean("has_attachment") ?? false;
        var queryString = arguments?.GetString("query");

        if (!ToolTime.TryParseUtcOptional(arguments?.GetString("since_utc"), out var since, out var sinceErr)) {
            return ToolResponse.Error("invalid_argument", $"since_utc: {sinceErr}");
        }
        if (!ToolTime.TryParseUtcOptional(arguments?.GetString("before_utc"), out var before, out var beforeErr)) {
            return ToolResponse.Error("invalid_argument", $"before_utc: {beforeErr}");
        }
        if (since.HasValue && before.HasValue && since.Value > before.Value) {
            return ToolResponse.Error("invalid_argument", "since_utc must be <= before_utc.");
        }

        var max = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxListResults, 1, Options.MaxListResults);

        using var client = await ImapClientFactory.ConnectAsync(imap, cancellationToken).ConfigureAwait(false);
        try {
            var messages = await MailboxSearcher.SearchImapAsync(
                client,
                folder: folder,
                subject: subject,
                fromContains: from,
                toContains: to,
                bodyContains: body,
                since: since,
                before: before,
                hasAttachment: hasAttachment,
                maxResults: max,
                cancellationToken: cancellationToken,
                queryString: queryString).ConfigureAwait(false);

            var rawMessages = messages.Select(static msg => {
                var info = new ImapMessageInfo(msg);
                return new {
                Uid = (long)info.Uid.Id,
                From = info.From,
                To = info.To,
                Subject = info.Subject ?? string.Empty,
                DateUtc = info.Date.ToUniversalTime().ToString("O"),
                HasAttachments = info.HasAttachments
                };
            }).ToArray();

            var truncated = messages.Count >= max;

            ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
                arguments: arguments,
                model: new {
                    Folder = folder ?? string.Empty,
                    Count = messages.Count,
                    Truncated = truncated,
                    Messages = rawMessages
                },
                sourceRows: rawMessages,
                viewRowsPath: "messages_view",
                title: "IMAP messages (preview)",
                maxTop: MaxViewTop,
                baseTruncated: truncated,
                response: out var response,
                metaMutate: m => m.Add("max_results", max));
            return response;
        } finally {
            try {
                if (client.IsConnected) {
                    client.Disconnect(true);
                }
            } catch {
                // best-effort
            }
        }
    }
}

