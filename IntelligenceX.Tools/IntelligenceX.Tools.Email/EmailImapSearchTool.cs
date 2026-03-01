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
    private sealed record SearchRequest(
        string? Folder,
        string? SubjectContains,
        string? FromContains,
        string? ToContains,
        string? BodyContains,
        bool HasAttachment,
        DateTime? SinceUtc,
        DateTime? BeforeUtc,
        string? Query,
        int MaxResults);

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
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<SearchRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("since_utc"), out var since, out var sinceErr)) {
                return ToolRequestBindingResult<SearchRequest>.Failure($"since_utc: {sinceErr}");
            }
            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("before_utc"), out var before, out var beforeErr)) {
                return ToolRequestBindingResult<SearchRequest>.Failure($"before_utc: {beforeErr}");
            }
            if (since.HasValue && before.HasValue && since.Value > before.Value) {
                return ToolRequestBindingResult<SearchRequest>.Failure("since_utc must be <= before_utc.");
            }

            return ToolRequestBindingResult<SearchRequest>.Success(new SearchRequest(
                Folder: reader.OptionalString("folder"),
                SubjectContains: reader.OptionalString("subject_contains"),
                FromContains: reader.OptionalString("from_contains"),
                ToContains: reader.OptionalString("to_contains"),
                BodyContains: reader.OptionalString("body_contains"),
                HasAttachment: reader.Boolean("has_attachment", defaultValue: false),
                SinceUtc: since,
                BeforeUtc: before,
                Query: reader.OptionalString("query"),
                MaxResults: reader.CappedInt32("max_results", Options.MaxListResults, 1, Options.MaxListResults)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<SearchRequest> context, CancellationToken cancellationToken) {
        var imap = Options.Imap;
        if (imap is null) {
            return ToolResultV2.Error("not_configured", "IMAP is not configured.");
        }
        imap.Validate();

        var request = context.Request;
        var folder = request.Folder ?? imap.DefaultFolder;

        using var client = await ImapClientFactory.ConnectAsync(imap, cancellationToken).ConfigureAwait(false);
        try {
            var messages = await MailboxSearcher.SearchImapAsync(
                client,
                folder: folder,
                subject: request.SubjectContains,
                fromContains: request.FromContains,
                toContains: request.ToContains,
                bodyContains: request.BodyContains,
                since: request.SinceUtc,
                before: request.BeforeUtc,
                hasAttachment: request.HasAttachment,
                maxResults: request.MaxResults,
                cancellationToken: cancellationToken,
                queryString: request.Query).ConfigureAwait(false);

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

            var truncated = messages.Count >= request.MaxResults;
            return ToolResultV2.OkAutoTableResponse(
                arguments: context.Arguments,
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
                metaMutate: m => AddMaxResultsMeta(m, request.MaxResults));
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
