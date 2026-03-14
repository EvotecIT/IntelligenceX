using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.FileSystem;

internal static class FileSystemToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["fs_list"] = new[] {
                "list directory entries under allowed roots to orient within a local workspace or evidence folder"
            },
            ["fs_read"] = new[] {
                "read a local text file from allowed roots for focused inspection"
            },
            ["fs_search"] = new[] {
                "search local text files under allowed roots for patterns, errors, or indicators"
            }
        };
}
