namespace IntelligenceX.Tools;

/// <summary>
/// Defines how write intent is detected from tool arguments.
/// </summary>
public enum ToolWriteIntentMode {
    /// <summary>
    /// Tool is always considered write-intent capable when invoked.
    /// </summary>
    Always = 0,

    /// <summary>
    /// Write intent is active when a boolean argument is true.
    /// </summary>
    BooleanFlagTrue = 1,

    /// <summary>
    /// Write intent is active when a string argument matches a configured value.
    /// </summary>
    StringEquals = 2
}
