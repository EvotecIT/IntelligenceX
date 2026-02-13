using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Returns email pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class EmailPackInfoTool : EmailToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "email_pack_info",
        "Return email pack capabilities, configuration hints, output contract, and recommended usage patterns.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailPackInfoTool"/> class.
    /// </summary>
    public EmailPackInfoTool(EmailToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "email",
            engine: "Mailozaurr",
            tools: ToolRegistryEmailExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use email_imap_search to find candidate messages.",
                "Use email_imap_get to fetch specific message content/metadata.",
                "Use email_smtp_send only when outbound email actions are requested."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover candidate messages",
                    suggestedTools: new[] { "email_imap_search" }),
                ToolPackGuidance.FlowStep(
                    goal: "Extract full message evidence",
                    suggestedTools: new[] { "email_imap_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Perform outbound action only on explicit intent",
                    suggestedTools: new[] { "email_smtp_send" },
                    notes: "Default behavior should remain dry-run unless send=true is set.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "mailbox_discovery",
                    summary: "Search IMAP mailboxes using filters and return rich message metadata.",
                    primaryTools: new[] { "email_imap_search" }),
                ToolPackGuidance.Capability(
                    id: "message_extraction",
                    summary: "Read complete message content (headers/text/html/attachments metadata) with truncation safety.",
                    primaryTools: new[] { "email_imap_get" }),
                ToolPackGuidance.Capability(
                    id: "outbound_delivery",
                    summary: "Send messages via SMTP with explicit confirmation and dry-run-first behavior.",
                    primaryTools: new[] { "email_smtp_send" })
            },
            toolCatalog: ToolRegistryEmailExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Raw message arrays are preserved for model reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            setupHints: new {
                ImapConfigured = Options.Imap is not null,
                SmtpConfigured = Options.Smtp is not null,
                MaxBodyBytes = Options.MaxBodyBytes,
                MaxListResults = Options.MaxListResults
            });

        return Task.FromResult(ToolResponse.OkModel(root));
    }
}
