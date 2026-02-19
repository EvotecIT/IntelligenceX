using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Append-only JSONL sink for write-governance audit records.
/// </summary>
public sealed class AppendOnlyJsonlToolWriteAuditSink : IToolWriteAuditSink {
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new append-only sink targeting a JSONL file path.
    /// </summary>
    /// <param name="filePath">Target JSONL file path.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is empty.</exception>
    public AppendOnlyJsonlToolWriteAuditSink(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new ArgumentException("Audit file path cannot be empty.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath.Trim());
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Gets the resolved JSONL file path.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public void Append(ToolWriteAuditRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["timestamp_utc"] = record.TimestampUtc.ToString("O"),
            ["tool_name"] = record.ToolName,
            ["canonical_tool_name"] = record.CanonicalToolName,
            ["governance_contract_id"] = record.GovernanceContractId,
            ["is_authorized"] = record.IsAuthorized,
            ["error_code"] = record.ErrorCode,
            ["error"] = record.Error,
            ["execution_id"] = record.ExecutionId,
            ["audit_correlation_id"] = record.AuditCorrelationId,
            ["actor_id"] = record.ActorId,
            ["change_reason"] = record.ChangeReason,
            ["rollback_plan_id"] = record.RollbackPlanId,
            ["immutable_audit_provider_id"] = record.ImmutableAuditProviderId,
            ["rollback_provider_id"] = record.RollbackProviderId
        };

        var line = JsonLite.Serialize(payload);
        lock (_gate) {
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(line);
        }
    }
}
