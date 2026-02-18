using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Reads Active Directory tombstone/deleted-object lifetime settings (read-only).
/// </summary>
public sealed class AdRecycleBinLifetimeTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_recycle_bin_lifetime",
        "Read AD tombstone lifetime and msDS-deletedObjectLifetime settings for current or specified forest (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name. When omitted, uses current forest.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdRecycleBinLifetimeTool"/> class.
    /// </summary>
    public AdRecycleBinLifetimeTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");

        if (!TryExecute(
                action: () => new RecycleBinLifetimeReader().GetDeletedObjectLifetime(forestName),
                result: out DeletedObjectLifetime lifetime,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Recycle bin lifetime query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var model = new {
            ForestName = forestName,
            lifetime.DistinguishedName,
            lifetime.TombstoneLifetime,
            lifetime.MsDsDeletedObjectLifetime
        };

        var facts = new[] {
            ("Forest", string.IsNullOrWhiteSpace(forestName) ? "(current)" : forestName!),
            ("Directory Service DN", lifetime.DistinguishedName),
            ("Tombstone lifetime (days)", lifetime.TombstoneLifetime.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Deleted object lifetime (days)", lifetime.MsDsDeletedObjectLifetime.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: model,
            title: "Active Directory: Recycle Bin Lifetime",
            facts: facts,
            meta: ToolOutputHints.Meta(count: facts.Length, truncated: false),
            keyHeader: "Setting",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

