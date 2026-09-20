using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Desktop;

/// <summary>Stable activation identity shared by native hosts, isolating previews from installed copies.</summary>
public static class DesktopAppInstanceIdentity {
    /// <summary>Creates a Windows installation-scoped key; an optional mode keeps diagnostic hosts independent.</summary>
    public static string ForInstallation(DesktopCompanion app, string installationDirectory, string? mode = null) {
        if (!Enum.IsDefined(typeof(DesktopCompanion), app)) throw new ArgumentOutOfRangeException(nameof(app));
        if (string.IsNullOrWhiteSpace(installationDirectory)) throw new ArgumentException("Installation directory is required.", nameof(installationDirectory));
        var normalized = Path.GetFullPath(installationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized + "\n" + (mode ?? string.Empty)));
        return "IntelligenceX." + app + "." + BitConverter.ToString(digest).Replace("-", string.Empty);
    }
}
