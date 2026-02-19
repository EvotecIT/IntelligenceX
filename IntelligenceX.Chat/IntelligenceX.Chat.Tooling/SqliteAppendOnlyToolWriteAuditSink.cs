using System;
using System.Collections.Generic;
using System.IO;
using DBAClientX;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Append-only SQLite sink for write-governance audit records.
/// </summary>
internal sealed class SqliteAppendOnlyToolWriteAuditSink : IToolWriteAuditSink, IDisposable {
    private const string TableName = "ix_tool_write_audit";

    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    public SqliteAppendOnlyToolWriteAuditSink(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("SQLite audit path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        EnsureSchema();
    }

    public void Append(ToolWriteAuditRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT INTO {TableName} (
  timestamp_utc,
  tool_name,
  canonical_tool_name,
  governance_contract_id,
  is_authorized,
  error_code,
  error,
  execution_id,
  audit_correlation_id,
  actor_id,
  change_reason,
  rollback_plan_id,
  immutable_audit_provider_id,
  rollback_provider_id
)
VALUES (
  @timestamp_utc,
  @tool_name,
  @canonical_tool_name,
  @governance_contract_id,
  @is_authorized,
  @error_code,
  @error,
  @execution_id,
  @audit_correlation_id,
  @actor_id,
  @change_reason,
  @rollback_plan_id,
  @immutable_audit_provider_id,
  @rollback_provider_id
);",
                parameters: new Dictionary<string, object?> {
                    ["@timestamp_utc"] = record.TimestampUtc.ToString("O"),
                    ["@tool_name"] = Normalize(record.ToolName),
                    ["@canonical_tool_name"] = Normalize(record.CanonicalToolName),
                    ["@governance_contract_id"] = Normalize(record.GovernanceContractId),
                    ["@is_authorized"] = record.IsAuthorized ? 1 : 0,
                    ["@error_code"] = Normalize(record.ErrorCode),
                    ["@error"] = Normalize(record.Error),
                    ["@execution_id"] = Normalize(record.ExecutionId),
                    ["@audit_correlation_id"] = Normalize(record.AuditCorrelationId),
                    ["@actor_id"] = Normalize(record.ActorId),
                    ["@change_reason"] = Normalize(record.ChangeReason),
                    ["@rollback_plan_id"] = Normalize(record.RollbackPlanId),
                    ["@immutable_audit_provider_id"] = Normalize(record.ImmutableAuditProviderId),
                    ["@rollback_provider_id"] = Normalize(record.RollbackProviderId)
                });
        }
    }

    public void Dispose() {
        _db.Dispose();
    }

    private void EnsureSchema() {
        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
CREATE TABLE IF NOT EXISTS {TableName} (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  timestamp_utc TEXT NOT NULL,
  tool_name TEXT NOT NULL,
  canonical_tool_name TEXT NOT NULL,
  governance_contract_id TEXT NOT NULL,
  is_authorized INTEGER NOT NULL,
  error_code TEXT NULL,
  error TEXT NULL,
  execution_id TEXT NOT NULL,
  audit_correlation_id TEXT NOT NULL,
  actor_id TEXT NULL,
  change_reason TEXT NULL,
  rollback_plan_id TEXT NULL,
  immutable_audit_provider_id TEXT NULL,
  rollback_provider_id TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_tool_write_audit_timestamp ON {TableName}(timestamp_utc);
CREATE INDEX IF NOT EXISTS ix_tool_write_audit_execution ON {TableName}(execution_id);
CREATE INDEX IF NOT EXISTS ix_tool_write_audit_correlation ON {TableName}(audit_correlation_id);
");
        }
    }

    private static object? Normalize(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
