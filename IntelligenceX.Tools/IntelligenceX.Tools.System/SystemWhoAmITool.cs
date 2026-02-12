using System;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Identity;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns current user identity information.
/// </summary>
public sealed class SystemWhoAmITool : SystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "system_whoami",
        "Return current user identity information (read-only).",
        ToolSchema.Object()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemWhoAmITool"/> class.
    /// </summary>
    public SystemWhoAmITool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var attempt = CurrentIdentityQueryExecutor.TryExecute(
            request: new CurrentIdentityQueryRequest { IncludeSid = true },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return Task.FromResult(ErrorFromFailure(attempt.Failure, static x => x.Code, static x => x.Message, defaultMessage: "Current identity query failed."));
        }
        var identity = attempt.Result!.Identity;

        var facts = new[] {
            ("User", identity.AccountName),
            ("SID", identity.UserSid)
        };

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: identity,
            title: "Identity",
            facts: facts,
            meta: null,
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

