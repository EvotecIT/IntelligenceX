using System;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared factories for common authentication contract patterns.
/// </summary>
public static class ToolAuthenticationConventions {
    /// <summary>
    /// Creates a host-managed authentication contract (credentials resolved outside tool arguments).
    /// </summary>
    public static ToolAuthenticationContract HostManaged(
        bool requiresAuthentication = true,
        bool supportsConnectivityProbe = false,
        string? probeToolName = null) {
        return new ToolAuthenticationContract {
            IsAuthenticationAware = true,
            RequiresAuthentication = requiresAuthentication,
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            Mode = ToolAuthenticationMode.HostManaged,
            SupportsConnectivityProbe = supportsConnectivityProbe,
            ProbeIdArgumentName = ToolAuthenticationArgumentNames.ProbeId,
            ProbeToolName = NormalizeProbeName(supportsConnectivityProbe, probeToolName)
        };
    }

    /// <summary>
    /// Creates a profile-reference authentication contract.
    /// </summary>
    public static ToolAuthenticationContract ProfileReference(
        string profileIdArgumentName = ToolAuthenticationArgumentNames.ProfileId,
        bool requiresAuthentication = true,
        bool supportsConnectivityProbe = false,
        string? probeToolName = null) {
        if (string.IsNullOrWhiteSpace(profileIdArgumentName)) {
            throw new ArgumentException("Profile id argument name is required.", nameof(profileIdArgumentName));
        }

        return new ToolAuthenticationContract {
            IsAuthenticationAware = true,
            RequiresAuthentication = requiresAuthentication,
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            Mode = ToolAuthenticationMode.ProfileReference,
            ProfileIdArgumentName = profileIdArgumentName.Trim(),
            SupportsConnectivityProbe = supportsConnectivityProbe,
            ProbeIdArgumentName = ToolAuthenticationArgumentNames.ProbeId,
            ProbeToolName = NormalizeProbeName(supportsConnectivityProbe, probeToolName)
        };
    }

    /// <summary>
    /// Creates a run-as profile-reference authentication contract.
    /// </summary>
    public static ToolAuthenticationContract RunAsReference(
        string runAsProfileIdArgumentName = ToolAuthenticationArgumentNames.RunAsProfileId,
        bool requiresAuthentication = true,
        bool supportsConnectivityProbe = false,
        string? probeToolName = null) {
        if (string.IsNullOrWhiteSpace(runAsProfileIdArgumentName)) {
            throw new ArgumentException("Run-as profile id argument name is required.", nameof(runAsProfileIdArgumentName));
        }

        return new ToolAuthenticationContract {
            IsAuthenticationAware = true,
            RequiresAuthentication = requiresAuthentication,
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            Mode = ToolAuthenticationMode.RunAsReference,
            RunAsProfileIdArgumentName = runAsProfileIdArgumentName.Trim(),
            SupportsConnectivityProbe = supportsConnectivityProbe,
            ProbeIdArgumentName = ToolAuthenticationArgumentNames.ProbeId,
            ProbeToolName = NormalizeProbeName(supportsConnectivityProbe, probeToolName)
        };
    }

    private static string NormalizeProbeName(bool supportsConnectivityProbe, string? probeToolName) {
        if (!supportsConnectivityProbe) {
            return string.Empty;
        }

        var normalized = (probeToolName ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }
}
