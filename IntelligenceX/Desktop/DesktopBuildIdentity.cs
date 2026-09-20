using System.Linq;
using System.Reflection;

namespace IntelligenceX.Desktop;

/// <summary>Displays an explicit build-time preview label without changing normal release names.</summary>
public static class DesktopBuildIdentity {
    /// <summary>Appends the entry assembly's optional preview label to the supplied product name.</summary>
    public static string DisplayName(string productName) {
        var label = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "IntelligenceXPreviewLabel")?.Value;
        return string.IsNullOrWhiteSpace(label) ? productName : productName + " · " + label.Trim();
    }
}
