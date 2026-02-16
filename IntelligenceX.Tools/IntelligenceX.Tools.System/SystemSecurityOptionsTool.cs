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

        if (!OperatingSystem.IsWindows()) {
            return Task.FromResult(ToolResponse.Error("not_supported", "system_security_options is available only on Windows hosts."));
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName;

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
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", ex.Message));
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"Security options query failed: {ex.Message}"));
        }
    }
}
