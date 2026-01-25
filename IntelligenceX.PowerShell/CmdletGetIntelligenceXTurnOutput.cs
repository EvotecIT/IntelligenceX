using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Extracts outputs from a turn.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXTurnOutput")]
[OutputType(typeof(TurnOutput))]
public sealed class CmdletGetIntelligenceXTurnOutput : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Turn to read outputs from.</para>
    /// </summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public TurnInfo? Turn { get; set; }

    /// <summary>
    /// <para type="description">Return only image outputs.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Images { get; set; }

    /// <summary>
    /// <para type="description">Return only text outputs.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Text { get; set; }

    /// <summary>
    /// <para type="description">Return only the first N outputs.</para>
    /// </summary>
    [Parameter]
    public int First { get; set; }

    /// <summary>
    /// <para type="description">Save image outputs to the specified directory.</para>
    /// </summary>
    [Parameter]
    public string? SaveImagesTo { get; set; }

    /// <summary>
    /// <para type="description">Download image URLs when saving images.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter DownloadUrls { get; set; }

    /// <summary>
    /// <para type="description">Overwrite existing files when saving images.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    /// <summary>
    /// <para type="description">Prefix for saved image file names.</para>
    /// </summary>
    [Parameter]
    public string? FileNamePrefix { get; set; }

    /// <summary>
    /// <para type="description">Output original TurnOutput objects when saving images.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override async Task ProcessRecordAsync() {
        if (Turn is null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(SaveImagesTo)) {
            var images = TurnOutputSaver.ApplyFirst(Turn.ImageOutputs, First);
            var saved = await TurnOutputSaver.SaveImagesAsync(images, SaveImagesTo!, Turn.Id, DownloadUrls.IsPresent,
                    Overwrite.IsPresent, FileNamePrefix, null, WriteWarning, WriteVerbose)
                .ConfigureAwait(false);
            if (PassThru.IsPresent) {
                WriteObject(images, true);
            } else {
                WriteObject(saved, true);
            }
            return;
        }

        if (Images.IsPresent) {
            WriteObject(TurnOutputSaver.ApplyFirst(Turn.ImageOutputs, First), true);
            return;
        }

        if (Text.IsPresent) {
            foreach (var output in TurnOutputSaver.ApplyFirst(Turn.Outputs, First)) {
                if (output.IsText) {
                    WriteObject(output);
                }
            }
            return;
        }

        WriteObject(TurnOutputSaver.ApplyFirst(Turn.Outputs, First), true);
    }
}
