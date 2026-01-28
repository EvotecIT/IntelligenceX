using System;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class CleanupPromptBuilder {
    public static string Build(PullRequestContext context, CleanupSettings settings) {
        var builder = new StringBuilder();
        builder.AppendLine("You are a maintainer cleaning up a GitHub pull request title/body.");
        builder.AppendLine("Goal: fix grammar, formatting, and structure without changing technical meaning.");
        builder.AppendLine("If the content is already clear, respond with needs_cleanup=false.");
        builder.AppendLine();
        builder.AppendLine("Allowed edits:");
        builder.AppendLine($"- {string.Join(", ", settings.AllowedEdits)}");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine("- Do not change intent or technical meaning.");
        builder.AppendLine("- Keep language consistent with the original.");
        builder.AppendLine("- Preserve code blocks and inline code.");
        builder.AppendLine("- Keep it concise.");
        builder.AppendLine();
        builder.AppendLine("Output strict JSON with keys:");
        builder.AppendLine("needs_cleanup (bool), confidence (0-1), title (string), body (string), notes (string).");
        builder.AppendLine();

        var template = settings.ResolveTemplate();
        if (!string.IsNullOrWhiteSpace(template)) {
            builder.AppendLine("Preferred template (use if it helps structure):");
            builder.AppendLine("-----");
            builder.AppendLine(template.Trim());
            builder.AppendLine("-----");
            builder.AppendLine();
        }

        builder.AppendLine("Current title:");
        builder.AppendLine(context.Title);
        builder.AppendLine();
        builder.AppendLine("Current body (may be empty):");
        builder.AppendLine("-----");
        builder.AppendLine(context.Body ?? string.Empty);
        builder.AppendLine("-----");
        builder.AppendLine();
        builder.AppendLine("Return only JSON.");
        return builder.ToString();
    }
}
