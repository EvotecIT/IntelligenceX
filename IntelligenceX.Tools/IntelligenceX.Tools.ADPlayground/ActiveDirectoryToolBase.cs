using System;
using System.Collections.Generic;
using System.Threading;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Base class for Active Directory tools.
/// </summary>
public abstract class ActiveDirectoryToolBase : ToolBase {
    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveDirectoryToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected ActiveDirectoryToolBase(ActiveDirectoryToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Gets the configured tool options.
    /// </summary>
    protected ActiveDirectoryToolOptions Options { get; }

    /// <summary>
    /// Resolves optional Active Directory scope arguments with safe fallbacks.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token used for RootDSE reads.</param>
    /// <returns>Resolved domain controller and search base DN.</returns>
    protected (string? DomainController, string? SearchBaseDn) ResolveDomainControllerAndSearchBase(
        JsonObject? arguments,
        CancellationToken cancellationToken) {
        var context = LdapToolContextHelper.ResolveSearchContext(
            explicitDomainController: ToolArgs.GetOptionalTrimmed(arguments, "domain_controller"),
            explicitBaseDn: ToolArgs.GetOptionalTrimmed(arguments, "search_base_dn"),
            defaultDomainController: ToolArgs.NormalizeOptional(Options.DomainController),
            defaultBaseDn: ToolArgs.NormalizeOptional(Options.DefaultSearchBaseDn),
            cancellationToken: cancellationToken);

        return (context.DomainController, context.BaseDn);
    }

    /// <summary>
    /// Resolves tool-requested AD attribute list via ADPlayground policy helper.
    /// </summary>
    protected static List<string> ResolveAttributes(
        JsonObject? arguments,
        string attributesKey,
        IEnumerable<string>? allowedAttributes,
        IEnumerable<string>? defaultAttributes,
        IEnumerable<string>? requiredAttributes,
        int? maxAttributeCount = null) {
        var requested = ToolArgs.ReadStringArray(arguments?.GetArray(attributesKey));
        var resolved = LdapToolContextHelper.ResolveAttributeList(
            requestedAttributes: requested,
            allowedAttributes: allowedAttributes,
            defaultAttributes: defaultAttributes,
            requiredAttributes: requiredAttributes,
            maxAttributeCount: maxAttributeCount);

        return new List<string>(resolved);
    }

    /// <summary>
    /// Serializes a JSON object result with <c>ok=true</c>.
    /// </summary>
    /// <param name="obj">Result object.</param>
    /// <returns>JSON string.</returns>
    protected static string Ok(JsonObject obj) {
        return ToolResponse.Ok(root: obj);
    }

    /// <summary>
    /// Serializes a JSON object result with <c>ok=true</c> and optional UI hints.
    /// </summary>
    /// <param name="root">Root payload fields (kept at the tool output root).</param>
    /// <param name="meta">Optional metadata object.</param>
    /// <param name="summaryMarkdown">Optional markdown summary for UI traces.</param>
    /// <param name="render">Optional render hint object.</param>
    /// <returns>JSON string.</returns>
    protected static string Ok(JsonObject root, JsonObject? meta, string? summaryMarkdown, JsonObject? render) {
        return ToolResponse.Ok(root, meta, summaryMarkdown, render);
    }

    /// <summary>
    /// Serializes a JSON error result with <c>ok=false</c>.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <returns>JSON string.</returns>
    protected static string Error(string? message) {
        var msg = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message!;
        return ToolResponse.Error("error", msg);
    }

    /// <summary>
    /// Serializes a JSON error result with a stable error code.
    /// </summary>
    protected static string Error(string errorCode, string error, IEnumerable<string>? hints = null, bool isTransient = false) {
        return ToolResponse.Error(errorCode, error, hints, isTransient);
    }
}

