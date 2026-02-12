using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Helpers to collect small preview tables for <c>summary_markdown</c> consistently.
/// </summary>
public static class ToolPreview {
    /// <summary>
    /// Creates a preview collector that captures up to <paramref name="maxRows"/> rows.
    /// </summary>
    /// <param name="maxRows">Maximum number of rows to collect.</param>
    /// <param name="maxCellChars">Optional maximum characters per cell.</param>
    public static PreviewTable Table(int maxRows = 20, int? maxCellChars = 200) => new(maxRows, maxCellChars);

    /// <summary>
    /// Collected preview table rows.
    /// </summary>
    public sealed class PreviewTable {
        private readonly int _maxRows;
        private readonly int? _maxCellChars;
        private readonly List<IReadOnlyList<string>> _rows;

        /// <summary>
        /// Creates a new preview table collector.
        /// </summary>
        /// <param name="maxRows">Maximum number of rows to collect.</param>
        /// <param name="maxCellChars">Optional maximum characters per cell.</param>
        public PreviewTable(int maxRows, int? maxCellChars) {
            _maxRows = maxRows <= 0 ? 0 : maxRows;
            _maxCellChars = maxCellChars;
            _rows = _maxRows > 0 ? new List<IReadOnlyList<string>>(_maxRows) : new List<IReadOnlyList<string>>(0);
        }

        /// <summary>
        /// Number of collected rows.
        /// </summary>
        public int Count => _rows.Count;
        /// <summary>
        /// Collected rows.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

        /// <summary>
        /// Attempts to add a row to the preview.
        /// </summary>
        /// <param name="cells">Cell values.</param>
        /// <returns><see langword="true"/> when the row was added.</returns>
        public bool TryAdd(params string?[] cells) {
            if (_rows.Count >= _maxRows) {
                return false;
            }

            if (cells is null || cells.Length == 0) {
                _rows.Add(Array.Empty<string>());
                return true;
            }

            var row = new string[cells.Length];
            for (var i = 0; i < cells.Length; i++) {
                row[i] = Trunc(cells[i] ?? string.Empty);
            }

            _rows.Add(row);
            return true;
        }

        private string Trunc(string value) {
            if (!_maxCellChars.HasValue || _maxCellChars.Value <= 0) {
                return value;
            }
            if (value.Length <= _maxCellChars.Value) {
                return value;
            }
            return value.Substring(0, _maxCellChars.Value) + "...";
        }
    }
}
