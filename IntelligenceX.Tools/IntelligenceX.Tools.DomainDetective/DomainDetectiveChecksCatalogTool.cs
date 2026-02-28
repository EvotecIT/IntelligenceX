using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Returns supported DomainDetective check names and alias normalization guidance.
/// </summary>
public sealed class DomainDetectiveChecksCatalogTool : DomainDetectiveToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "domaindetective_checks_catalog",
        "Return supported DomainDetective check names, baseline defaults, and alias normalization guidance.",
        ToolSchema.Object(
                ("include_aliases", ToolSchema.Boolean("Include alias-to-canonical normalization entries (default: true).")),
                ("include_default_checks", ToolSchema.Boolean("Include baseline default checks used by domaindetective_domain_summary (default: true).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectiveChecksCatalogTool"/> class.
    /// </summary>
    public DomainDetectiveChecksCatalogTool(DomainDetectiveToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeAliases = ToolArgs.GetBoolean(arguments, "include_aliases", defaultValue: true);
        var includeDefaultChecks = ToolArgs.GetBoolean(arguments, "include_default_checks", defaultValue: true);

        var supportedChecks = DomainDetectiveCheckNameCatalog.GetSupportedCheckNames();
        var defaultChecks = includeDefaultChecks
            ? DomainDetectiveCheckNameCatalog.DefaultChecks
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var aliases = includeAliases
            ? DomainDetectiveCheckNameCatalog.AliasByToken
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new DomainDetectiveCheckAliasModel {
                    Token = pair.Key,
                    Canonical = pair.Value
                })
                .ToArray()
            : Array.Empty<DomainDetectiveCheckAliasModel>();
        var defaultCheckLookup = new HashSet<string>(defaultChecks, StringComparer.OrdinalIgnoreCase);
        var checkRows = supportedChecks
            .Select(check => new DomainDetectiveCheckRowModel {
                Check = check,
                IsDefault = defaultCheckLookup.Contains(check)
            })
            .ToArray();

        var result = new DomainDetectiveChecksCatalogResultModel {
            Source = ResolveCatalogSource(),
            MaxChecksPerRun = Options.MaxChecks,
            SupportedChecks = supportedChecks,
            DefaultChecks = defaultChecks,
            CheckRows = checkRows,
            Aliases = aliases,
            NormalizationRules = new[] {
                "Input is case-insensitive and non-alphanumeric separators are ignored.",
                "Known aliases (for example NAMESERVERS, SPFRECORD, DMARCRECORDS) normalize to canonical check names."
            }
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "DomainDetective checks catalog",
            facts: new[] {
                ("Supported checks", result.SupportedChecks.Count.ToString()),
                ("Default checks", result.DefaultChecks.Count.ToString()),
                ("Aliases", result.Aliases.Count.ToString()),
                ("Max checks per run", result.MaxChecksPerRun.ToString())
            });

        var meta = ToolOutputHints.Meta(count: result.SupportedChecks.Count, truncated: false)
            .Add("supported_checks", result.SupportedChecks.Count)
            .Add("default_checks", result.DefaultChecks.Count)
            .Add("aliases", result.Aliases.Count)
            .Add("max_checks_per_run", result.MaxChecksPerRun);
        var renderHints = new JsonArray()
            .Add(ToolOutputHints.RenderTable(
                "check_rows",
                new ToolColumn("check", "Check", "string"),
                new ToolColumn("is_default", "Default", "bool")));
        if (result.Aliases.Count > 0) {
            renderHints.Add(ToolOutputHints.RenderTable(
                "aliases",
                new ToolColumn("token", "Alias", "string"),
                new ToolColumn("canonical", "Canonical", "string")));
        }

        return Task.FromResult(ToolOutputEnvelope.OkFlatWithRenderValue(
            root: ToolJson.ToJsonObjectSnakeCase(result),
            meta: meta,
            summaryMarkdown: summary,
            render: JsonValue.From(renderHints)));
    }

    private static string ResolveCatalogSource() {
#if DOMAINDETECTIVE_ENABLED
        return "enum";
#else
        return "fallback";
#endif
    }

    private sealed class DomainDetectiveChecksCatalogResultModel {
        public string Source { get; init; } = string.Empty;
        public int MaxChecksPerRun { get; init; }
        public IReadOnlyList<string> SupportedChecks { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DefaultChecks { get; init; } = Array.Empty<string>();
        public IReadOnlyList<DomainDetectiveCheckRowModel> CheckRows { get; init; } = Array.Empty<DomainDetectiveCheckRowModel>();
        public IReadOnlyList<DomainDetectiveCheckAliasModel> Aliases { get; init; } = Array.Empty<DomainDetectiveCheckAliasModel>();
        public IReadOnlyList<string> NormalizationRules { get; init; } = Array.Empty<string>();
    }

    private sealed class DomainDetectiveCheckRowModel {
        public string Check { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
    }

    private sealed class DomainDetectiveCheckAliasModel {
        public string Token { get; init; } = string.Empty;
        public string Canonical { get; init; } = string.Empty;
    }
}
