using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Convenience registration helpers for the Email tool pack.
/// </summary>
public static class ToolRegistryEmailExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterEmailPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(EmailToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterEmailPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(EmailToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all Email tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterEmailPack(this ToolRegistry registry, EmailToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(EmailToolOptions options) {
        yield return new EmailPackInfoTool(options);
        yield return new EmailImapSearchTool(options);
        yield return new EmailImapGetTool(options);
        yield return new EmailSmtpSendTool(options);
    }
}
