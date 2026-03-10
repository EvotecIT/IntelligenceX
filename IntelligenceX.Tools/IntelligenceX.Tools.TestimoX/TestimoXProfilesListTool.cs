using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Lists curated TestimoX execution profiles and their selection heuristics.
/// </summary>
public sealed class TestimoXProfilesListTool : TestimoXToolBase, ITool {
    private sealed record ProfilesListRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_profiles_list",
        "List curated TestimoX execution profiles with selection guidance and included/excluded areas.",
        ToolSchema.Object().WithTableViewOptions().NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "profiles",
            "selection"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXProfilesListTool"/> class.
    /// </summary>
    public TestimoXProfilesListTool(TestimoXToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ProfilesListRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<ProfilesListRequest>.Success(new ProfilesListRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ProfilesListRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_profiles_list." },
                isTransient: false));
        }

        IReadOnlyList<RuleSelectionProfileInfo> profiles;
        try {
            profiles = RuleSelectionProfileCatalog.GetProfiles();
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, "TestimoX profile discovery failed."));
        }

        var rows = profiles
            .OrderBy(static profile => profile.Profile.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(static profile => new TestimoProfileCatalogRow(
                Profile: profile.Profile.ToString(),
                DisplayName: profile.DisplayName,
                Description: profile.Description,
                AssessmentAreas: profile.AssessmentAreas ?? Array.Empty<string>(),
                IncludeCategories: profile.IncludeCategories.Select(static value => value.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                ExcludeCategories: profile.ExcludeCategories.Select(static value => value.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                IncludeTags: profile.IncludeTags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                ExcludeTags: profile.ExcludeTags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                MaxCost: profile.MaxCost?.ToString() ?? string.Empty,
                ExcludeHeavy: profile.ExcludeHeavy,
                IncludeHidden: profile.IncludeHidden,
                ExcludeDeprecated: profile.ExcludeDeprecated))
            .ToList();

        var model = new TestimoProfilesCatalogResult(
            ProfileCount: rows.Count,
            Profiles: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "profiles_view",
            title: "TestimoX profiles",
            baseTruncated: false,
            maxTop: Math.Max(8, rows.Count),
            scanned: rows.Count,
            metaMutate: meta => meta.Add("profile_count", rows.Count)));
    }

    private sealed record TestimoProfilesCatalogResult(
        int ProfileCount,
        IReadOnlyList<TestimoProfileCatalogRow> Profiles);

    private sealed record TestimoProfileCatalogRow(
        string Profile,
        string DisplayName,
        string Description,
        IReadOnlyList<string> AssessmentAreas,
        IReadOnlyList<string> IncludeCategories,
        IReadOnlyList<string> ExcludeCategories,
        IReadOnlyList<string> IncludeTags,
        IReadOnlyList<string> ExcludeTags,
        string MaxCost,
        bool ExcludeHeavy,
        bool IncludeHidden,
        bool ExcludeDeprecated);
}
