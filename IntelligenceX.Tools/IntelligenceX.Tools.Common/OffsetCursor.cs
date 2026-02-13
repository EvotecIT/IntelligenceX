using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Helpers for simple opaque cursors used by tool paging.
/// </summary>
public static class OffsetCursor {
    /// <summary>
    /// Attempts to decode an offset cursor. Supports:
    /// - base64-encoded 8-byte int64
    /// - base64-encoded 4-byte int32
    /// - plain numeric strings (debug-friendly)
    /// - empty/null (treated as offset 0)
    /// </summary>
    public static bool TryDecode(string? cursor, out long offset) {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) {
            return true;
        }

        try {
            var bytes = Convert.FromBase64String(cursor.Trim());
            if (bytes.Length == 8) {
                offset = BitConverter.ToInt64(bytes, 0);
                return true;
            }
            if (bytes.Length == 4) {
                offset = BitConverter.ToInt32(bytes, 0);
                return true;
            }
        } catch {
            // fall through
        }

        return long.TryParse(cursor.Trim(), out offset);
    }

    /// <summary>
    /// Encodes a positive offset as an opaque cursor (base64 of int64).
    /// Returns empty string for offsets &lt;= 0.
    /// </summary>
    public static string Encode(long offset) {
        if (offset <= 0) {
            return string.Empty;
        }
        return Convert.ToBase64String(BitConverter.GetBytes(offset));
    }
}

