namespace IntelligenceX.Treatment;

/// <summary>
/// Options used while building provider prompts.
/// </summary>
public sealed class TreatmentPromptBuildOptions {
    /// <summary>
    /// Initializes default prompt build options.
    /// </summary>
    public TreatmentPromptBuildOptions() { }

    /// <summary>
    /// Base directory used to resolve relative local artifact paths.
    /// </summary>
    public string? BaseDirectory { get; set; }
    /// <summary>
    /// Whether text-like local artifact files should be inlined.
    /// </summary>
    public bool InlineLocalFiles { get; set; } = true;
    /// <summary>
    /// Maximum number of characters to inline per artifact file.
    /// </summary>
    public int MaxInlineFileCharacters { get; set; } = 120000;
}
