using System;

namespace IntelligenceX.Chat.ExportArtifacts;

public static partial class OfficeImoArtifactWriter {

    private static string BuildSpreadsheetColumnName(int columnIndexOneBased) {
        if (columnIndexOneBased < 1) {
            throw new ArgumentOutOfRangeException(nameof(columnIndexOneBased));
        }

        var columnName = string.Empty;
        var column = columnIndexOneBased;
        while (column > 0) {
            var remainder = (column - 1) % 26;
            columnName = (char)('A' + remainder) + columnName;
            column = (column - 1) / 26;
        }

        return columnName;
    }
}
