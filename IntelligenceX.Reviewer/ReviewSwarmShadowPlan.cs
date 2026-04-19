using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewSwarmShadowReviewerPlan {
    public string Id { get; init; } = string.Empty;
    public string? AgentProfile { get; init; }
    public ReviewAgentProfileSettings? ResolvedAgentProfile { get; init; }
    public ReviewProvider Provider { get; init; }
    public string Model { get; init; } = string.Empty;
    public ReasoningEffort? ReasoningEffort { get; init; }
}

internal sealed class ReviewSwarmShadowAggregatorPlan {
    public string? AgentProfile { get; init; }
    public ReviewAgentProfileSettings? ResolvedAgentProfile { get; init; }
    public ReviewProvider Provider { get; init; }
    public string Model { get; init; } = string.Empty;
    public ReasoningEffort? ReasoningEffort { get; init; }
}

internal sealed class ReviewSwarmShadowPlan {
    public bool Enabled { get; init; }
    public bool ShadowMode { get; init; }
    public int MaxParallel { get; init; }
    public IReadOnlyList<ReviewSwarmShadowReviewerPlan> Reviewers { get; init; } = Array.Empty<ReviewSwarmShadowReviewerPlan>();
    public ReviewSwarmShadowAggregatorPlan Aggregator { get; init; } = new();
}

internal static class ReviewSwarmShadowPlanner {
    public static ReviewSwarmShadowPlan Build(ReviewSettings settings) {
        var reviewerSettings = settings.Swarm.ReviewerSettings.Count > 0
            ? settings.Swarm.ReviewerSettings
            : ReviewSettings.BuildSwarmReviewerSettings(settings.Swarm.Reviewers);

        var reviewers = new List<ReviewSwarmShadowReviewerPlan>(reviewerSettings.Count);
        foreach (var reviewer in reviewerSettings) {
            if (string.IsNullOrWhiteSpace(reviewer.Id)) {
                continue;
            }

            var profile = string.IsNullOrWhiteSpace(reviewer.AgentProfile)
                ? null
                : settings.RequireAgentProfile(reviewer.AgentProfile,
                    $"review.swarm.reviewers[{reviewer.Id.Trim()}].agentProfile");
            var reviewerProvider = reviewer.Provider ?? profile?.ResolveProvider() ?? settings.Provider;
            var reviewerModel = !string.IsNullOrWhiteSpace(reviewer.Model)
                ? reviewer.Model!.Trim()
                : !string.IsNullOrWhiteSpace(profile?.Model)
                    ? profile!.Model!.Trim()
                    : settings.Model;

            reviewers.Add(new ReviewSwarmShadowReviewerPlan {
                Id = reviewer.Id.Trim().ToLowerInvariant(),
                AgentProfile = profile?.Id,
                ResolvedAgentProfile = profile,
                Provider = reviewerProvider,
                Model = reviewerModel,
                ReasoningEffort = reviewer.ReasoningEffort ?? profile?.ReasoningEffort ?? settings.ReasoningEffort
            });
        }

        var aggregatorProfile = string.IsNullOrWhiteSpace(settings.Swarm.Aggregator.AgentProfile)
            ? null
            : settings.RequireAgentProfile(settings.Swarm.Aggregator.AgentProfile,
                "review.swarm.aggregator.agentProfile");
        var aggregatorModel = !string.IsNullOrWhiteSpace(settings.Swarm.Aggregator.Model)
            ? settings.Swarm.Aggregator.Model!.Trim()
            : !string.IsNullOrWhiteSpace(aggregatorProfile?.Model)
                ? aggregatorProfile!.Model!.Trim()
            : !string.IsNullOrWhiteSpace(settings.Swarm.AggregatorModel)
                ? settings.Swarm.AggregatorModel!.Trim()
                : settings.Model;

        return new ReviewSwarmShadowPlan {
            Enabled = settings.Swarm.Enabled,
            ShadowMode = settings.Swarm.ShadowMode,
            MaxParallel = Math.Max(1, settings.Swarm.MaxParallel),
            Reviewers = reviewers,
            Aggregator = new ReviewSwarmShadowAggregatorPlan {
                AgentProfile = aggregatorProfile?.Id,
                ResolvedAgentProfile = aggregatorProfile,
                Provider = settings.Swarm.Aggregator.Provider ?? aggregatorProfile?.ResolveProvider() ?? settings.Provider,
                Model = aggregatorModel,
                ReasoningEffort = settings.Swarm.Aggregator.ReasoningEffort ?? aggregatorProfile?.ReasoningEffort ?? settings.ReasoningEffort
            }
        };
    }

    public static string Render(ReviewSwarmShadowPlan plan) {
        if (!plan.Enabled || !plan.ShadowMode) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Swarm shadow plan (public comment remains single-review path):");
        sb.AppendLine($"- reviewers: {plan.Reviewers.Count} | parallel cap: {plan.MaxParallel}");
        foreach (var reviewer in plan.Reviewers) {
            sb.Append("- ");
            sb.Append(reviewer.Id);
            sb.Append(" -> ");
            if (!string.IsNullOrWhiteSpace(reviewer.AgentProfile)) {
                sb.Append(reviewer.AgentProfile);
                sb.Append(" -> ");
            }
            sb.Append(reviewer.Provider.ToString().ToLowerInvariant());
            sb.Append(" / ");
            sb.Append(reviewer.Model);
            if (reviewer.ReasoningEffort.HasValue) {
                sb.Append(" / reasoning ");
                sb.Append(reviewer.ReasoningEffort.Value.ToString().ToLowerInvariant());
            }
            sb.AppendLine();
        }

        sb.Append("- aggregator -> ");
        if (!string.IsNullOrWhiteSpace(plan.Aggregator.AgentProfile)) {
            sb.Append(plan.Aggregator.AgentProfile);
            sb.Append(" -> ");
        }
        sb.Append(plan.Aggregator.Provider.ToString().ToLowerInvariant());
        sb.Append(" / ");
        sb.Append(plan.Aggregator.Model);
        if (plan.Aggregator.ReasoningEffort.HasValue) {
            sb.Append(" / reasoning ");
            sb.Append(plan.Aggregator.ReasoningEffort.Value.ToString().ToLowerInvariant());
        }
        sb.AppendLine();

        return sb.ToString().TrimEnd();
    }
}
