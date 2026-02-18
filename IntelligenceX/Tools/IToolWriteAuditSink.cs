namespace IntelligenceX.Tools;

/// <summary>
/// Append-only sink abstraction for immutable write-governance audit records.
/// </summary>
public interface IToolWriteAuditSink {
    /// <summary>
    /// Appends a write-governance audit record.
    /// </summary>
    /// <param name="record">Record to append.</param>
    void Append(ToolWriteAuditRecord record);
}
