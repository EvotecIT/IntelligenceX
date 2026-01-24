using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class PromptBuilder {
    public static string Build(PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings) {
        var sb = new StringBuilder();
        sb.AppendLine("You are a senior software engineer performing a code review.");
        sb.AppendLine("Focus on correctness, security, performance, and maintainability.");
        if (!string.IsNullOrWhiteSpace(settings.Persona)) {
            sb.AppendLine($"Persona: {settings.Persona}.");
        }
        if (!string.IsNullOrWhiteSpace(settings.Notes)) {
            sb.AppendLine($"Additional guidance: {settings.Notes}.");
        }
        if (!string.IsNullOrWhiteSpace(settings.SeverityThreshold)) {
            sb.AppendLine($"Only include issues with severity >= {settings.SeverityThreshold}.");
        }
        if (settings.Length == ReviewLength.Short) {
            sb.AppendLine("Keep the review short: max 6 bullets per section.");
        } else {
            sb.AppendLine("Provide a thorough review with actionable details.");
        }
        if (settings.IncludeNextSteps) {
            sb.AppendLine("Include a \"Next Steps\" section with advice for the next PR.");
        }
        sb.AppendLine();
        sb.AppendLine("Return your review in markdown with these sections:");
        sb.AppendLine("- Summary");
        sb.AppendLine("- Critical Issues (if any)");
        sb.AppendLine("- Other Issues");
        sb.AppendLine("- Tests / Coverage");
        if (settings.IncludeNextSteps) {
            sb.AppendLine("- Next Steps");
        }
        sb.AppendLine();
        sb.AppendLine("PR Context:");
        sb.AppendLine($"Title: {context.Title}");
        if (!string.IsNullOrWhiteSpace(context.Body)) {
            sb.AppendLine("Description:");
            sb.AppendLine(context.Body);
        }
        sb.AppendLine();
        sb.AppendLine("Changed files:");

        foreach (var file in files) {
            sb.AppendLine($"File: {file.Filename} ({file.Status})");
            if (!string.IsNullOrWhiteSpace(file.Patch)) {
                sb.AppendLine("Patch:");
                sb.AppendLine(file.Patch);
            } else {
                sb.AppendLine("Patch: <no diff available>");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
