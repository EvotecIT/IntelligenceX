using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.Utils;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Runs health checks for OpenAI app-server and optional Copilot CLI.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXHealth")]
[OutputType(typeof(HealthReportRecord))]
public sealed class CmdletGetIntelligenceXHealth : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">OpenAI app-server client instance. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Run a Copilot CLI health check.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Copilot { get; set; }

    /// <summary>
    /// <para type="description">Ignore .intelligencex/config.json overrides.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter NoConfig { get; set; }

    /// <summary>
    /// <para type="description">Copilot CLI path.</para>
    /// </summary>
    [Parameter]
    public string? CopilotCliPath { get; set; }

    /// <summary>
    /// <para type="description">Copilot CLI URL (host:port).</para>
    /// </summary>
    [Parameter]
    public string? CopilotCliUrl { get; set; }

    /// <summary>
    /// <para type="description">Copilot CLI working directory.</para>
    /// </summary>
    [Parameter]
    public string? CopilotWorkingDirectory { get; set; }

    /// <summary>
    /// <para type="description">Auto-install Copilot CLI if missing.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter CopilotAutoInstall { get; set; }

    /// <summary>
    /// <para type="description">Copilot auto-install method.</para>
    /// </summary>
    [Parameter]
    public CopilotCliInstallMethod CopilotInstallMethod { get; set; } = CopilotCliInstallMethod.Auto;

    /// <summary>
    /// <para type="description">Copilot auto-install prerelease.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter CopilotInstallPrerelease { get; set; }

    protected override async Task ProcessRecordAsync() {
        HealthCheckResult? openAi = null;
        HealthCheckResult? copilot = null;

        if (Client is not null || ClientContext.DefaultClient is not null) {
            var resolved = ResolveClient(Client);
            openAi = await resolved.HealthCheckAsync().ConfigureAwait(false);
        }

        if (Copilot.IsPresent) {
            var options = new CopilotClientOptions();
            if (!NoConfig.IsPresent && IntelligenceXConfig.TryLoad(out var config)) {
                config.Copilot.ApplyTo(options);
            }
            if (!string.IsNullOrWhiteSpace(CopilotCliPath)) {
                options.CliPath = CopilotCliPath;
            }
            if (!string.IsNullOrWhiteSpace(CopilotCliUrl)) {
                options.CliUrl = CopilotCliUrl;
            }
            if (!string.IsNullOrWhiteSpace(CopilotWorkingDirectory)) {
                options.WorkingDirectory = CopilotWorkingDirectory;
            }
            if (CopilotAutoInstall.IsPresent) {
                options.AutoInstallCli = true;
            }
            options.AutoInstallMethod = CopilotInstallMethod;
            options.AutoInstallPrerelease = CopilotInstallPrerelease.IsPresent;

            var client = await CopilotClient.StartAsync(options, CancelToken).ConfigureAwait(false);
            try {
                copilot = await client.HealthCheckAsync(cancellationToken: CancelToken).ConfigureAwait(false);
            } finally {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        WriteObject(new HealthReportRecord(openAi, copilot));
    }
}
