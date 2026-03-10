using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void WorkingMemoryCheckpoint_AugmentsCompactFollowUpAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-working-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-working-memory";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId: threadId,
                intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
                domainIntentFamily: "ad_domain",
                recentToolNames: new[] { "ad_replication_summary", "eventlog_live_query" },
                recentEvidenceSnippets: new[] { "ad_replication_summary: replication failures were concentrated on DC02." },
                priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
                priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
                priorAnswerPlanRequiresLiveExecution: true,
                priorAnswerPlanMissingLiveEvidence: "cert status and memory usage",
                priorAnswerPlanPreferredPackIds: new[] { "active_directory", "system" },
                priorAnswerPlanPreferredToolNames: new[] { "ad_ldap_diagnostics", "system_hardware_summary" },
                priorAnswerPlanPrimaryArtifact: "table",
                enabledPackIds: new[] { "adplayground", "eventlog" },
                routingFamilies: new[] { "ad_domain", "public_domain" },
                skills: new[] { "ad_domain.scope_hosts", "public_domain.query_whois" },
                healthyToolNames: new[] { "ad_replication_summary", "eventlog_live_query" });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var augmented = session2.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
                threadId,
                userRequest: "run now",
                routedUserRequest: "run now",
                out var routedFromCheckpoint);

            Assert.True(augmented);
            Assert.Contains("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_user_goal:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_unresolved_ask:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_requires_live_execution: true", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_missing_live_evidence: cert status and memory usage", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_preferred_pack_ids: active_directory, system", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_preferred_tool_names: ad_ldap_diagnostics, system_hardware_summary", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_primary_artifact: table", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ix:working-memory:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intent_anchor:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("domain_scope_family: ad_domain", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("recent_tools:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_user_goal:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_unresolved_now:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_requires_live_execution: true", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_missing_live_evidence: cert status and memory usage", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_preferred_pack_ids: active_directory, system", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_preferred_tool_names: ad_ldap_diagnostics, system_hardware_summary", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prior_answer_plan_primary_artifact: table", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ix:capability-snapshot:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("enabled_packs:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("routing_families:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void WorkingMemoryCheckpoint_RoutingPreludeAfterRestart_CarriesLongQuestionIntoFocusedContinuationContext() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-working-memory-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-working-memory-routing";
        const string followUp = "Where is ADRODC in the full forest replication table above, and why are those rows still missing from it?";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId: threadId,
                intentAnchor: "Run forest-wide replication and LDAP diagnostics.",
                domainIntentFamily: "ad_domain",
                recentToolNames: new[] { "ad_replication_summary" },
                recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
                priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
                priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
                priorAnswerPlanPrimaryArtifact: "table",
                enabledPackIds: new[] { "active_directory" },
                routingFamilies: new[] { "ad_domain" },
                healthyToolNames: new[] { "ad_replication_summary" });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var prelude = session2.ResolveRoutingPreludeForTesting(threadId, followUp);

            Assert.Equal(followUp, prelude.UserRequest);
            Assert.True(prelude.ContinuationExpandedFromContext);
            Assert.True(prelude.HasStructuredContinuationContext);
            Assert.False(prelude.ContinuationFollowUpTurn);
            Assert.False(prelude.CompactFollowUpTurn);
            Assert.Contains("ix:continuation-focus:v1", prelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.", prelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("follow_up: " + followUp, prelude.RoutedUserRequest, StringComparison.Ordinal);
            Assert.DoesNotContain("ix:continuation:v1", prelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Follow-up: " + followUp, prelude.RoutedUserRequest, StringComparison.Ordinal);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotEmitContinuationFocusBlockWithoutPriorAnswerPlan() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-no-focus",
            intentAnchor: "Analyze DNS + AD context and compare anomalies.",
            domainIntentFamily: "public_domain",
            recentToolNames: new[] { "domaindetective_domain_summary" },
            recentEvidenceSnippets: new[] { "domaindetective_domain_summary: SPF and DMARC are valid." });

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-no-focus",
            userRequest: "run now",
            routedUserRequest: "run now",
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.DoesNotContain("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_unresolved_ask:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:working-memory:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_EmitsContinuationFocusBlockForCachedEvidenceReusePreferenceWithoutUnresolvedAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-cache-reuse-focus";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue from the same forest replication evidence.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest replication is healthy for AD0, AD1, and AD2." },
            priorAnswerPlanUserGoal: "Continue from the same forest replication evidence.",
            priorAnswerPlanUnresolvedNow: string.Empty,
            priorAnswerPlanPreferCachedEvidenceReuse: true,
            priorAnswerPlanCachedEvidenceReuseReason: "reuse the latest forest replication snapshot",
            priorAnswerPlanPrimaryArtifact: "prose");

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            userRequest: "continue replication AD2",
            routedUserRequest: "continue replication AD2",
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.Contains("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_unresolved_ask:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_prefer_cached_evidence_reuse: true", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "last_cached_evidence_reuse_reason: reuse the latest forest replication snapshot",
            routedFromCheckpoint,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prior_answer_plan_prefer_cached_evidence_reuse: true", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "prior_answer_plan_cached_evidence_reuse_reason: reuse the latest forest replication snapshot",
            routedFromCheckpoint,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_AugmentsLongQuestionFollowUpWhenItOverlapsPriorAnswerPlanFocus() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-long-question";
        const string followUp = "Where is ADRODC in the full forest replication table above, and why are those rows still missing from it?";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table");

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            userRequest: followUp,
            routedUserRequest: followUp,
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.Contains("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow_up: " + followUp, routedFromCheckpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotAugmentLongQuestionFollowUpWithoutPriorAnswerPlanOverlap() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-unrelated-question";
        const string followUp = "Which firewall ports should I open for LDAP and Kerberos troubleshooting in another environment?";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table");

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            userRequest: followUp,
            routedUserRequest: followUp,
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal(followUp, routedFromCheckpoint);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_ResolveIntentAnchorForTesting_PrefersContinuationFocusUnresolvedAsk() {
        var routedRequest = """
            [Continuation focus]
            ix:continuation-focus:v1
            last_user_goal: Summarize the forest replication state in a table.
            last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.
            last_primary_artifact: table

            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Run AD replication + failed-logon diagnostics across DCs and summarize top risks.
            follow_up: run now
            """;

        var resolved = ChatServiceSession.ResolveWorkingMemoryIntentAnchorForTesting(
            userIntent: string.Empty,
            routedUserRequest: routedRequest,
            fallbackIntentAnchor: "fallback anchor");

        Assert.Equal("Explain why ADRODC is absent from the forest replication rows.", resolved);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_ResolveIntentAnchorForTesting_FallsBackToWorkingMemoryIntentAnchorWithoutContinuationFocus() {
        var routedRequest = """
            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Run AD replication + failed-logon diagnostics across DCs and summarize top risks.
            follow_up: run now
            """;

        var resolved = ChatServiceSession.ResolveWorkingMemoryIntentAnchorForTesting(
            userIntent: string.Empty,
            routedUserRequest: routedRequest,
            fallbackIntentAnchor: "fallback anchor");

        Assert.Equal("Run AD replication + failed-logon diagnostics across DCs and summarize top risks.", resolved);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_AnswerPlanCarryForwardFalse_ClearsPriorUnresolvedFocus() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-answer-plan-clear";
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run forest-wide replication diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table");

        var reviewedDraft = ChatServiceSession.ResolveReviewedAssistantDraft("""
            [Answer progression plan]
            ix:answer-plan:v1
            user_goal: explain why ADRODC was missing from the table above
            resolved_so_far: clarified that the upstream collector returned only domain-scoped rows
            unresolved_now: explain why ADRODC is absent from the forest replication rows
            carry_forward_unresolved_focus: false
            carry_forward_reason: the prior follow-up gap is fully resolved in this turn
            primary_artifact: prose
            requested_artifact_already_visible_above: true
            requested_artifact_visibility_reason: the table above is still the relevant artifact
            repeats_prior_visible_content: false
            prior_visible_delta_reason: none
            reuse_prior_visuals: false
            reuse_reason: none
            repeat_adds_new_information: true
            repeat_novelty_reason: none
            advances_current_ask: true
            advance_reason: resolves the missing-row explanation without redrawing the table

            ADRODC was missing because the upstream collector returned only domain-scoped rows in that run.
            """);

        session.RememberWorkingMemoryCheckpointFromAnswerPlanForTesting(
            threadId: threadId,
            userIntent: "Explain why ADRODC was missing from the table above.",
            routedUserRequest: "Explain why ADRODC was missing from the table above.",
            answerPlan: reviewedDraft.AnswerPlan);

        var found = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            threadId,
            out var userGoal,
            out var unresolvedNow,
            out var primaryArtifact);

        Assert.True(found);
        Assert.Equal("explain why ADRODC was missing from the table above", userGoal);
        Assert.Equal(string.Empty, unresolvedNow);
        Assert.Equal("prose", primaryArtifact);

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId,
            userRequest: "run now",
            routedUserRequest: "run now",
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.DoesNotContain("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_unresolved_ask:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_AnswerPlanCarryForwardTrue_ReplacesPriorUnresolvedFocusWithNarrowerGap() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-answer-plan-narrow";
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run forest-wide replication diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table");

        var reviewedDraft = ChatServiceSession.ResolveReviewedAssistantDraft("""
            [Answer progression plan]
            ix:answer-plan:v1
            user_goal: explain the remaining forest replication discrepancy
            resolved_so_far: confirmed that the run was forest-scoped and the visible table is still valid
            unresolved_now: confirm whether ADRODC rows were omitted by the upstream collector or filtered client-side
            carry_forward_unresolved_focus: true
            carry_forward_reason: one narrower evidence gap still remains after this explanation
            primary_artifact: prose
            requested_artifact_already_visible_above: true
            requested_artifact_visibility_reason: the table above is still the relevant artifact
            repeats_prior_visible_content: false
            prior_visible_delta_reason: none
            reuse_prior_visuals: false
            reuse_reason: none
            repeat_adds_new_information: true
            repeat_novelty_reason: none
            advances_current_ask: true
            advance_reason: narrows the remaining investigation target to the collector path

            The remaining question is whether the ADRODC rows were omitted by the upstream collector or filtered client-side.
            """);

        session.RememberWorkingMemoryCheckpointFromAnswerPlanForTesting(
            threadId: threadId,
            userIntent: "Explain the remaining forest replication discrepancy.",
            routedUserRequest: "Explain the remaining forest replication discrepancy.",
            answerPlan: reviewedDraft.AnswerPlan);

        var found = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            threadId,
            out _,
            out var unresolvedNow,
            out var primaryArtifact);

        Assert.True(found);
        Assert.Equal(
            "confirm whether ADRODC rows were omitted by the upstream collector or filtered client-side",
            unresolvedNow);
        Assert.Equal("prose", primaryArtifact);

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId,
            userRequest: "run now",
            routedUserRequest: "run now",
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.Contains("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "last_unresolved_ask: confirm whether ADRODC rows were omitted by the upstream collector or filtered client-side",
            routedFromCheckpoint,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotAugmentWhenRoutedRequestAlreadyExpanded() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-expanded",
            intentAnchor: "Analyze DNS + AD context and compare anomalies.",
            domainIntentFamily: "public_domain",
            recentToolNames: new[] { "domaindetective_domain_summary" },
            recentEvidenceSnippets: new[] { "domaindetective_domain_summary: SPF and DMARC are valid." });

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-expanded",
            userRequest: "run now",
            routedUserRequest: "Analyze DNS + AD context and compare anomalies.\nFollow-up: run now",
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal("Analyze DNS + AD context and compare anomalies.\nFollow-up: run now", routedFromCheckpoint);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_OverridesExpandedRoutedRequestWhenAnswerPlanRequestsCachedEvidenceReuse() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-expanded-cache-reuse";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "go ahead and check full ad replication forest",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest replication is healthy for AD0, AD1, and AD2." },
            priorAnswerPlanUserGoal: "Continue from the same forest replication evidence.",
            priorAnswerPlanUnresolvedNow: string.Empty,
            priorAnswerPlanPreferCachedEvidenceReuse: true,
            priorAnswerPlanCachedEvidenceReuseReason: "compact continuation should reuse the latest forest replication evidence snapshot",
            priorAnswerPlanPrimaryArtifact: "prose");

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            userRequest: "continue replication AD1",
            routedUserRequest: "go ahead and check full ad replication forest\nFollow-up: continue replication AD1",
            out var routedFromCheckpoint);

        Assert.True(augmented);
        Assert.Contains("ix:continuation-focus:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_prefer_cached_evidence_reuse: true", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow_up: continue replication AD1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotAugmentPassiveCompactAckWithSymbolCue() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-passive-ack",
            intentAnchor: "Analyze DNS + AD context and compare anomalies.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: replication is healthy." });

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-passive-ack",
            userRequest: "ok to dziala ;)",
            routedUserRequest: "ok to dziala ;)",
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal("ok to dziala ;)", routedFromCheckpoint);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotAugmentStructuredActionSelectionPayload() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-structured-action";
        const string payload = "{\"ix_action_selection\":{\"id\":\"act_domain_scope_public\",\"request\":{\"ix_domain_scope\":{\"family\":\"public_domain\"}},\"mutating\":false}}";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary", "eventlog_live_query" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: replication failures were concentrated on DC02." });

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId,
            userRequest: payload,
            routedUserRequest: payload,
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal(payload, routedFromCheckpoint);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DoesNotAugmentPendingDomainSelectionReply() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-domain-selection";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary", "eventlog_live_query" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: replication failures were concentrated on DC02." });
        session.RememberPendingDomainIntentClarificationRequestForTesting(threadId);

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId,
            userRequest: "２",
            routedUserRequest: "２",
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal("２", routedFromCheckpoint);
    }

    [Fact]
    public void WorkingMemoryCheckpoint_DropsStructuredIntentAnchorOnCheckpointWrite() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-working-memory-structured-anchor";
        const string structuredAnchor =
            "{\"ix_action_selection\":{\"id\":\"act_domain_scope_public\",\"request\":{\"ix_domain_scope\":{\"family\":\"public_domain\"}},\"mutating\":false}}";

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: structuredAnchor,
            domainIntentFamily: "public_domain",
            recentToolNames: new[] { "domaindetective_domain_summary" },
            recentEvidenceSnippets: new[] { "domaindetective_domain_summary: SPF and DMARC are valid." });

        var found = session.TryGetWorkingMemoryCheckpointForTesting(
            threadId,
            out var intentAnchor,
            out _,
            out _,
            out _);

        Assert.True(found);
        Assert.Equal(string.Empty, intentAnchor);
    }
}
