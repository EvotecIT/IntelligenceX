using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Boot;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns Windows boot option posture and pending reboot state (read-only).
/// </summary>
public sealed class SystemBootConfigurationTool : SystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "system_boot_configuration",
        "Return Windows boot option posture (testsigning/debug/nointegritychecks/bootlog) and optional pending reboot state (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_reboot_pending", ToolSchema.Boolean("When true (default), include reboot-pending evaluation.")))
            .NoAdditionalProperties());

    private sealed record SystemBootConfigurationResult(
        string ComputerName,
        BootOptionsInfo BootOptions,
        bool? RebootPending,
        bool InsecureBootFlags,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBootConfigurationTool"/> class.
    /// </summary>
    public SystemBootConfigurationTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_boot_configuration");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var includeRebootPending = ToolArgs.GetBoolean(arguments, "include_reboot_pending", defaultValue: true);
        var target = ResolveTargetComputerName(computerName);

        try {
            var boot = BootOptionsQuery.Query(computerName);

            bool? rebootPending = null;
            if (includeRebootPending) {
                rebootPending = RebootState.IsRebootPending(computerName);
            }

            var warnings = new List<string>();
            var insecureFlags = boot.TestSigning || boot.Debug || boot.NoIntegrityChecks;
            if (boot.TestSigning) warnings.Add("Boot test signing is enabled.");
            if (boot.Debug) warnings.Add("Kernel debug boot option is enabled.");
            if (boot.NoIntegrityChecks) warnings.Add("No-integrity-checks boot option is enabled.");
            if (rebootPending == true) warnings.Add("System reports pending reboot state.");

            var model = new SystemBootConfigurationResult(
                ComputerName: target,
                BootOptions: boot,
                RebootPending: rebootPending,
                InsecureBootFlags: insecureFlags,
                Warnings: warnings);

            return Task.FromResult(ToolResponse.OkFactsModel(
                model: model,
                title: "System boot configuration",
                facts: new[] {
                    ("Computer", target),
                    ("TestSigning", boot.TestSigning.ToString()),
                    ("Debug", boot.Debug.ToString()),
                    ("NoIntegrityChecks", boot.NoIntegrityChecks.ToString()),
                    ("BootLog", boot.BootLog.ToString()),
                    ("RebootPending", rebootPending?.ToString() ?? string.Empty),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: target,
                    mutate: meta => meta.Add("include_reboot_pending", includeRebootPending)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Boot configuration query failed."));
        }
    }
}
