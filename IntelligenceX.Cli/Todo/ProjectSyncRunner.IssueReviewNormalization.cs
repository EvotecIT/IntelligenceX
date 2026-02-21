using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class ProjectSyncRunner {
    private static string? NormalizeIssueReviewAction(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "close" => "close",
            "keep-open" => "keep-open",
            "needs-human-review" => "needs-human-review",
            "ignore" => "ignore",
            _ => null
        };
    }
}
