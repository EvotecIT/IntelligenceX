namespace IntelligenceX.Tools;

/// <summary>
/// Declares how a tool expects authentication to be supplied.
/// </summary>
public enum ToolAuthenticationMode {
    /// <summary>
    /// Tool does not declare authentication behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Authentication is resolved by host/service configuration, not tool arguments.
    /// </summary>
    HostManaged = 1,

    /// <summary>
    /// Authentication is selected via a profile reference argument.
    /// </summary>
    ProfileReference = 2,

    /// <summary>
    /// Authentication can use a run-as profile reference argument.
    /// </summary>
    RunAsReference = 3
}
