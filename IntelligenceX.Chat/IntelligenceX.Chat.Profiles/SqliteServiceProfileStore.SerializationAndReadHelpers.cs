using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.Chat.Profiles;

internal sealed partial class SqliteServiceProfileStore {
    private static string SerializeTransport(IntelligenceX.OpenAI.OpenAITransportKind kind) {
        return kind switch {
            IntelligenceX.OpenAI.OpenAITransportKind.Native => "native",
            IntelligenceX.OpenAI.OpenAITransportKind.AppServer => "appserver",
            IntelligenceX.OpenAI.OpenAITransportKind.CompatibleHttp => "compatible-http",
            IntelligenceX.OpenAI.OpenAITransportKind.CopilotCli => "copilot-cli",
            _ => "native"
        };
    }

    private static string SerializeCompatibleAuthMode(IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode mode) {
        return mode switch {
            IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.None => "none",
            IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.Basic => "basic",
            _ => "bearer"
        };
    }

    private static bool TryParseTransport(string? value, out IntelligenceX.OpenAI.OpenAITransportKind kind) {
        kind = IntelligenceX.OpenAI.OpenAITransportKind.Native;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        switch (value.Trim().ToLowerInvariant()) {
            case "native":
                kind = IntelligenceX.OpenAI.OpenAITransportKind.Native;
                return true;
            case "appserver":
            case "app-server":
            case "codex":
                kind = IntelligenceX.OpenAI.OpenAITransportKind.AppServer;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                kind = IntelligenceX.OpenAI.OpenAITransportKind.CompatibleHttp;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                kind = IntelligenceX.OpenAI.OpenAITransportKind.CopilotCli;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseCompatibleAuthMode(string? value, out IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode mode) {
        mode = IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.Bearer;
        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        switch (value.Trim().ToLowerInvariant()) {
            case "none":
            case "off":
                mode = IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.None;
                return true;
            case "basic":
                mode = IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.Basic;
                return true;
            case "bearer":
            case "api-key":
            case "apikey":
            case "token":
                mode = IntelligenceX.OpenAI.CompatibleHttp.OpenAICompatibleHttpAuthMode.Bearer;
                return true;
            default:
                return false;
        }
    }

    private static string? ReadString(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string NormalizeWriteGovernanceMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "yolo" ? "yolo" : DefaultWriteGovernanceMode;
    }

    private static string NormalizeWriteAuditSinkMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "file" => "file",
            "fileappendonly" => "file",
            "jsonl" => "file",
            "sql" => "sqlite",
            "sqlite" => "sqlite",
            "sqliteappendonly" => "sqlite",
            _ => DefaultWriteAuditSinkMode
        };
    }

    private static string NormalizeAuthenticationRuntimePreset(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "strict" => "strict",
            "lab" => "lab",
            _ => DefaultAuthenticationRuntimePreset
        };
    }

    private static string? NormalizeOptionalPath(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static byte[]? ReadBytes(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        if (value is byte[] bytes) {
            return bytes;
        }
        return null;
    }

    private static bool ReadBool(DataRow row, string col, bool defaultValue) {
        if (!row.Table.Columns.Contains(col)) {
            return defaultValue;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return defaultValue;
        }
        if (value is bool b) {
            return b;
        }
        if (value is long l) {
            return l != 0;
        }
        if (value is int i) {
            return i != 0;
        }
        if (int.TryParse(value.ToString(), out var parsed)) {
            return parsed != 0;
        }
        return defaultValue;
    }

    private static int ReadInt(DataRow row, string col, int defaultValue) {
        if (!row.Table.Columns.Contains(col)) {
            return defaultValue;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return defaultValue;
        }
        if (value is int i) {
            return i;
        }
        if (value is long l) {
            return (int)l;
        }
        return int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }

    private static double? ReadDouble(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        if (value is double d) {
            return d;
        }
        if (value is float f) {
            return f;
        }
        if (value is long l) {
            return l;
        }
        return double.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable table) {
            return table;
        }

        if (queryResult is DataSet dataSet && dataSet.Tables.Count > 0) {
            return dataSet.Tables[0];
        }

        return null;
    }

    public void Dispose() {
        _db.Dispose();
    }
}
