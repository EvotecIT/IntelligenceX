using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.OpenAI;
using IntelligenceX.Utils;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Checks the active IntelligenceX connection and optional native Copilot access.</para>
/// <para type="description">Copilot checks use direct HTTPS model discovery. No CLI installation or process is required.</para>
/// <example><para>Check the active connection and Copilot subscription access.</para>
/// <code>Get-IntelligenceXHealth -Copilot</code></example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXHealth")]
[OutputType(typeof(HealthReportRecord))]
public sealed class CmdletGetIntelligenceXHealth : IntelligenceXCmdlet {
    /// <summary><para type="description">Client to check. Defaults to the active client.</para></summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary><para type="description">Also check Copilot model access using native HTTP.</para></summary>
    [Parameter]
    public SwitchParameter Copilot { get; set; }

    /// <summary><para type="description">Ignore .intelligencex/config.json overrides.</para></summary>
    [Parameter]
    public SwitchParameter NoConfig { get; set; }

    /// <summary><para type="description">Optional explicitly trusted Copilot HTTPS API root.</para></summary>
    [Parameter]
    public string? CopilotBaseUrl { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        HealthCheckResult? active = null, copilot = null;
        if (Client is not null || ClientContext.DefaultClient is not null)
            active = await ResolveClient(Client).HealthCheckAsync(cancellationToken: CancelToken).ConfigureAwait(false);
        if (Copilot.IsPresent) {
            var options = new IntelligenceXClientOptions { TransportKind = OpenAITransportKind.CopilotNative };
            if (!NoConfig.IsPresent && IntelligenceXConfig.TryLoad(out var config)) config.Copilot.ApplyTo(options.CopilotOptions);
            if (!string.IsNullOrWhiteSpace(CopilotBaseUrl)) options.CopilotOptions.BaseUrl = CopilotBaseUrl!;
            using var client = await IntelligenceXClient.ConnectAsync(options, CancelToken).ConfigureAwait(false);
            copilot = await client.HealthCheckAsync(cancellationToken: CancelToken).ConfigureAwait(false);
        }
        WriteObject(new HealthReportRecord(active, copilot));
    }
}