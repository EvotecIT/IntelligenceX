using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Email;

internal static class EmailToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["email_imap_search"] = new[] {
                "search an IMAP mailbox by subject, sender, or date range before opening a specific message"
            },
            ["email_imap_get"] = new[] {
                "retrieve a single IMAP message with headers and truncated body content for analysis"
            },
            ["email_smtp_probe"] = new[] {
                "verify SMTP connectivity and authentication before attempting a strict send workflow"
            },
            ["email_smtp_send"] = new[] {
                "prepare or send an SMTP message with explicit intent after probe-backed validation"
            }
        };
}
