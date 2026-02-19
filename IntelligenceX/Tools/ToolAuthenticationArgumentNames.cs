using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Canonical argument names used by tool authentication contracts.
/// </summary>
public static class ToolAuthenticationArgumentNames {
    /// <summary>
    /// Profile identifier argument used for host-managed secret resolution.
    /// </summary>
    public const string ProfileId = "auth_profile_id";

    /// <summary>
    /// Optional run-as profile identifier argument.
    /// </summary>
    public const string RunAsProfileId = "run_as_profile_id";

    /// <summary>
    /// Canonical authentication-related argument names.
    /// </summary>
    public static IReadOnlyList<string> CanonicalArguments { get; } = new[] {
        ProfileId,
        RunAsProfileId
    };
}
