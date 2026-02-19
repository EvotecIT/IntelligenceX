using System;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns Windows Security Options policy state from registry (read-only).
/// </summary>
public sealed class SystemSecurityOptionsTool : SystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "system_security_options",
        "Return Windows Security Options policy state from registry (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    private sealed record SecurityOptionsResult(string ComputerName, SecurityOptionsState SecurityOptions);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemSecurityOptionsTool"/> class.
    /// </summary>
    public SystemSecurityOptionsTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_security_options");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = ResolveTargetComputerName(computerName);

        try {
            var state = SecurityOptionsQuery.Get(computerName);
            var model = new SecurityOptionsResult(target, state);

            return Task.FromResult(ToolResponse.OkFactsModel(
                model: model,
                title: "System Security Options",
                facts: new[] {
                    ("Computer", target),
                    ("RestrictAnonymous", state.RestrictAnonymous?.ToString() ?? string.Empty),
                    ("RequireSmbSigningServer", state.RequireSmbSigningServer?.ToString() ?? string.Empty),
                    ("Smb1", state.Smb1?.ToString() ?? string.Empty)
                },
                meta: null,
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Security options query failed."));
        }
    }
}
