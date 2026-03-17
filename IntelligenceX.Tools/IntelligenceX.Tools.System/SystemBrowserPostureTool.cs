using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Browsers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns browser machine-policy posture and optional local extension summary for the local or remote Windows host.
/// </summary>
public sealed class SystemBrowserPostureTool : SystemToolBase, ITool {
    private sealed record BrowserPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeExtensions,
        int MaxExtensions);

    private sealed record BrowserPostureResponse(
        string ComputerName,
        bool PolicyCollectionAttempted,
        bool ExtensionInventoryAttempted,
        bool ExtensionInventoryCollectedLocally,
        BrowserPolicyInfo EdgePolicy,
        BrowserPolicyInfo ChromePolicy,
        BrowserPolicyInfo FirefoxPolicy,
        int ExtensionCountTotal,
        int EdgeExtensionCount,
        int ChromeExtensionCount,
        int FirefoxExtensionCount,
        IReadOnlyList<BrowserExtensionInfo> Extensions,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_browser_posture",
        "Return browser machine-policy posture and optional local extension inventory for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_extensions", ToolSchema.Boolean("When true, include capped extension inventory rows when collected locally. Default false.")),
                ("max_extensions", ToolSchema.Integer("Optional maximum extension rows when include_extensions=true (capped). Default 50.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBrowserPostureTool"/> class.
    /// </summary>
    public SystemBrowserPostureTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<BrowserPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<BrowserPostureRequest>.Success(new BrowserPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeExtensions: reader.Boolean("include_extensions", defaultValue: false),
                MaxExtensions: ToolArgs.GetCappedInt32(arguments, "max_extensions", 50, 1, 250)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<BrowserPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_browser_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await BrowserPosture
                .GetAsync(request.ComputerName, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var extensions = request.IncludeExtensions
                ? CapRows(posture.Extensions, request.MaxExtensions, out _, out _)
                : Array.Empty<BrowserExtensionInfo>();
            var warnings = BrowserPostureRiskEvaluator.Evaluate(
                posture,
                new BrowserPostureRiskOptions {
                    IncludeExtensions = request.IncludeExtensions,
                    IsLocalTarget = IsLocalTarget(request.ComputerName, request.Target)
                });
            var model = new BrowserPostureResponse(
                ComputerName: effectiveComputerName,
                PolicyCollectionAttempted: posture.PolicyCollectionAttempted,
                ExtensionInventoryAttempted: posture.ExtensionInventoryAttempted,
                ExtensionInventoryCollectedLocally: posture.ExtensionInventoryCollectedLocally,
                EdgePolicy: posture.EdgePolicy,
                ChromePolicy: posture.ChromePolicy,
                FirefoxPolicy: posture.FirefoxPolicy,
                ExtensionCountTotal: posture.ExtensionCountTotal,
                EdgeExtensionCount: posture.EdgeExtensionCount,
                ChromeExtensionCount: posture.ChromeExtensionCount,
                FirefoxExtensionCount: posture.FirefoxExtensionCount,
                Extensions: extensions,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName, mutate: x => {
                x.Add("include_extensions", request.IncludeExtensions);
                if (request.IncludeExtensions) {
                    x.Add("max_extensions", request.MaxExtensions);
                }
                x.Add("extension_inventory_collected_locally", posture.ExtensionInventoryCollectedLocally);
            });
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_browser_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 3 + posture.ExtensionCountTotal,
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Browser posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Policy Collection Attempted", posture.PolicyCollectionAttempted ? "true" : "false"),
                    ("Extension Inventory Attempted", posture.ExtensionInventoryAttempted ? "true" : "false"),
                    ("Extension Inventory Local", posture.ExtensionInventoryCollectedLocally ? "true" : "false"),
                    ("Edge Extensions", posture.EdgeExtensionCount.ToString(CultureInfo.InvariantCulture)),
                    ("Chrome Extensions", posture.ChromeExtensionCount.ToString(CultureInfo.InvariantCulture)),
                    ("Firefox Extensions", posture.FirefoxExtensionCount.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Browser posture query failed.");
        }
    }

}
