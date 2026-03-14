using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared follow-up route builders for promoting local artifact/file outputs into raw filesystem inspection.
/// </summary>
public static class LocalFileInspectionFollowUpCatalog {
    /// <summary>
    /// Builds the standard follow-up route into <c>fs_read</c> for a caller-provided path source field.
    /// </summary>
    public static ToolHandoffRoute[] CreateFilesystemReadRoutes(
        string pathSourceField,
        string reason,
        bool isRequired = true) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "filesystem",
                targetToolName: "fs_read",
                reason: reason,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(pathSourceField, "path", isRequired: isRequired)
                })
        };
    }
}
