using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists KDS root keys used for gMSA enablement posture (read-only).
/// </summary>
public sealed class AdKdsRootKeysTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_kds_root_keys",
        "List KDS root keys with effective-time posture for gMSA readiness checks (read-only).",
        ToolSchema.Object(
                ("effective_only", ToolSchema.Boolean("When true, include only keys effective at reference_time_utc.")),
                ("not_effective_only", ToolSchema.Boolean("When true, include only keys not yet effective at reference_time_utc.")),
                ("reference_time_utc", ToolSchema.String("Optional ISO-8601 UTC reference time used for effective posture (default now).")),
                ("max_results", ToolSchema.Integer("Maximum key rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record KdsRootKeyRow(
        Guid KeyId,
        DateTime? CreationTime,
        DateTime? EffectiveTime,
        bool IsEffective,
        int? DaysUntilEffective);

    private sealed record AdKdsRootKeysResult(
        bool EffectiveOnly,
        bool NotEffectiveOnly,
        DateTime ReferenceTimeUtc,
        int Scanned,
        bool Truncated,
        IReadOnlyList<KdsRootKeyRow> Keys);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdKdsRootKeysTool"/> class.
    /// </summary>
    public AdKdsRootKeysTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var maxResults = ResolveMaxResults(arguments);
        var effectiveOnly = ToolArgs.GetBoolean(arguments, "effective_only", defaultValue: false);
        var notEffectiveOnly = ToolArgs.GetBoolean(arguments, "not_effective_only", defaultValue: false);
        if (effectiveOnly && notEffectiveOnly) {
            return Task.FromResult(Error(
                "invalid_argument",
                "effective_only and not_effective_only cannot both be true."));
        }

        if (!ToolTime.TryParseUtcOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "reference_time_utc"),
                out var referenceTimeUtc,
                out var referenceTimeError)) {
            return Task.FromResult(Error("invalid_argument", $"reference_time_utc: {referenceTimeError}"));
        }

        var referenceUtc = referenceTimeUtc ?? DateTime.UtcNow;

        if (!TryExecute(
                action: static () => new KdsRootKeyChecker().GetRootKeys().ToArray(),
                result: out var keys,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "KDS root key query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var projected = keys
            .Select(key => {
                var effectiveTime = key.EffectiveTime?.ToUniversalTime();
                var isEffective = !effectiveTime.HasValue || effectiveTime.Value <= referenceUtc;
                var daysUntilEffective = effectiveTime.HasValue && effectiveTime.Value > referenceUtc
                    ? Math.Max(0, (int)Math.Ceiling((effectiveTime.Value - referenceUtc).TotalDays))
                    : (int?)null;
                return new KdsRootKeyRow(
                    KeyId: key.KeyId,
                    CreationTime: key.CreationTime?.ToUniversalTime(),
                    EffectiveTime: effectiveTime,
                    IsEffective: isEffective,
                    DaysUntilEffective: daysUntilEffective);
            })
            .Where(row => !effectiveOnly || row.IsEffective)
            .Where(row => !notEffectiveOnly || !row.IsEffective)
            .OrderByDescending(static row => row.EffectiveTime)
            .ThenBy(static row => row.KeyId)
            .ToArray();

        var rows = CapRows(
            allRows: projected,
            maxResults: maxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdKdsRootKeysResult(
            EffectiveOnly: effectiveOnly,
            NotEffectiveOnly: notEffectiveOnly,
            ReferenceTimeUtc: referenceUtc,
            Scanned: scanned,
            Truncated: truncated,
            Keys: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "keys_view",
            title: "Active Directory: KDS Root Keys (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("effective_only", effectiveOnly);
                meta.Add("not_effective_only", notEffectiveOnly);
                meta.Add("reference_time_utc", ToolTime.FormatUtc(referenceUtc));
                AddMaxResultsMeta(meta, maxResults);
            }));
    }
}
