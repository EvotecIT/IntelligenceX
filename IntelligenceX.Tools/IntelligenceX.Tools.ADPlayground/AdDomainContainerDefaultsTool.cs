using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns default user/computer container settings (redircmp/redirusr posture) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDomainContainerDefaultsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    internal readonly record struct DomainContainerDefaultsBindingContract(
        string? DomainName,
        string? ForestName,
        bool ChangedOnly);

    private sealed record DomainContainerDefaultsRequest(
        string? DomainName,
        string? ForestName,
        bool ChangedOnly);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_container_defaults",
        "Get default user/computer container redirection settings and change indicators for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("changed_only", ToolSchema.Boolean("When true, return only domains where user/computer default container was changed.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DomainContainerDefaultsRow(
        string DomainName,
        string? DefaultComputerContainer,
        string? DefaultUserContainer,
        bool ComputerContainerChanged,
        bool UserContainerChanged,
        bool AnyChanged);

    private sealed record DomainContainerDefaultsError(
        string Domain,
        string Message);

    private sealed record AdDomainContainerDefaultsResult(
        string? DomainName,
        string? ForestName,
        bool ChangedOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DomainContainerDefaultsError> Errors,
        IReadOnlyList<DomainContainerDefaultsRow> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainContainerDefaultsTool"/> class.
    /// </summary>
    public AdDomainContainerDefaultsTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DomainContainerDefaultsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!TryReadBooleanCompat(arguments, "changed_only", defaultValue: false, out var changedOnly, out var changedOnlyError)) {
                return ToolRequestBindingResult<DomainContainerDefaultsRequest>.Failure(changedOnlyError!);
            }

            return ToolRequestBindingResult<DomainContainerDefaultsRequest>.Success(new DomainContainerDefaultsRequest(
                DomainName: reader.OptionalString("domain_name"),
                ForestName: reader.OptionalString("forest_name"),
                ChangedOnly: changedOnly));
        });
    }

    internal static ToolRequestBindingResult<DomainContainerDefaultsBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<DomainContainerDefaultsBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<DomainContainerDefaultsBindingContract>.Success(new DomainContainerDefaultsBindingContract(
            DomainName: request.DomainName,
            ForestName: request.ForestName,
            ChangedOnly: request.ChangedOnly));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DomainContainerDefaultsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var domainName = request.DomainName;
        var forestName = request.ForestName;
        var maxResults = ResolveMaxResults(context.Arguments);
        var changedOnly = request.ChangedOnly;

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "domain-container-defaults",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<DomainContainerDefaultsRow>(targetDomains.Length);
        var errors = new List<DomainContainerDefaultsError>();
        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = DomainContainerDefaultsService.GetSnapshot(domain);
                var anyChanged = snapshot.ComputerContainerChanged || snapshot.UserContainerChanged;
                rows.Add(new DomainContainerDefaultsRow(
                    DomainName: snapshot.DomainName,
                    DefaultComputerContainer: snapshot.DefaultComputerContainer,
                    DefaultUserContainer: snapshot.DefaultUserContainer,
                    ComputerContainerChanged: snapshot.ComputerContainerChanged,
                    UserContainerChanged: snapshot.UserContainerChanged,
                    AnyChanged: anyChanged));
            },
            errorFactory: (domain, ex) => new DomainContainerDefaultsError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !changedOnly || row.AnyChanged)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<DomainContainerDefaultsRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDomainContainerDefaultsResult(
            DomainName: domainName,
            ForestName: forestName,
            ChangedOnly: changedOnly,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Domain Container Defaults (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("changed_only", changedOnly);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            }));
    }

    private static bool TryReadBooleanCompat(
        JsonObject? arguments,
        string key,
        bool defaultValue,
        out bool value,
        out string? error) {
        value = defaultValue;
        error = null;
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return true;
        }

        if (!arguments.TryGetValue(key, out var rawValue) || rawValue is null) {
            return true;
        }

        if (rawValue.Kind == JsonValueKind.Boolean) {
            value = rawValue.AsBoolean(defaultValue);
            return true;
        }

        if (rawValue.Kind == JsonValueKind.Number) {
            var numericValue = rawValue.AsInt64();
            if (!numericValue.HasValue) {
                error = $"{key} must be a boolean or a parseable boolean string/number.";
                return false;
            }

            value = numericValue.Value != 0;
            return true;
        }

        if (rawValue.Kind != JsonValueKind.String) {
            error = $"{key} must be a boolean or a parseable boolean string/number.";
            return false;
        }

        var raw = rawValue.AsString();
        if (string.IsNullOrWhiteSpace(raw)) {
            error = $"{key} must be a boolean or a parseable boolean string/number.";
            return false;
        }

        var normalized = raw.Trim();
        if (bool.TryParse(normalized, out var parsed)) {
            value = parsed;
            return true;
        }

        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)) {
            value = numeric != 0;
            return true;
        }

        error = $"{key} must be a boolean or a parseable boolean string/number.";
        return false;
    }
}
