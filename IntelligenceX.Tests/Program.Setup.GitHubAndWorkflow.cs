namespace IntelligenceX.Tests;

internal static partial class Program {
    #if !NET472
    private static void TestSetupBuildConfigJsonMergeRefreshesManagedReviewerDefaultsWhenEnablingAnalysis() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5-mini",
    "profile": "security",
    "mode": "summary",
    "commentMode": "sticky",
    "reviewDiffRange": "first-review",
    "includeReviewThreads": false,
    "reviewThreadsAutoResolveAIReply": false,
    "reviewUsageSummary": false,
    "includeIssueComments": false,
    "includeReviewComments": true,
    "includeRelatedPullRequests": false,
    "progressUpdates": false,
    "diagnostics": false,
    "preflight": true,
    "preflightTimeoutSeconds": 30,
    "customReviewFlag": "keep-me"
  }
}
""";

        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--with-config", "--analysis-enabled", "true", "--analysis-gate", "true" },
            seed);
        AssertNotNull(content, "config json merge content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json merge root");

        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json merge review");
        AssertEqual("keep-me", review!["customReviewFlag"]?.GetValue<string>(), "config json merge keeps custom review key");
        AssertEqual("security", review["profile"]?.GetValue<string>(), "config json merge keeps existing profile");
        AssertEqual(true, review["summaryStability"]?.GetValue<bool>(), "config json merge refreshes summary stability");
        AssertEqual("pr-base", review["reviewDiffRange"]?.GetValue<string>(), "config json merge refreshes diff range");
        AssertEqual(true, review["includeReviewThreads"]?.GetValue<bool>(), "config json merge refreshes include review threads");
        AssertEqual(true, review["reviewThreadsAutoResolveAIReply"]?.GetValue<bool>(),
            "config json merge refreshes auto-resolve ai reply");
        AssertEqual(true, review["reviewUsageSummary"]?.GetValue<bool>(), "config json merge refreshes usage summary");

        var analysis = root["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "config json merge analysis object");
        AssertEqual(true, analysis!["enabled"]?.GetValue<bool>(), "config json merge analysis.enabled");

        var gate = analysis["gate"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(gate, "config json merge analysis.gate");
        AssertEqual(true, gate!["enabled"]?.GetValue<bool>(), "config json merge analysis.gate.enabled");

        var packsNode = analysis["packs"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(packsNode, "config json merge analysis.packs");
        AssertEqual(true, packsNode!.Count > 0, "config json merge analysis.packs has values");
    }

    private static void TestSetupWorkflowUpgradePreservesCustomSectionsOutsideManagedBlock() {
        const string beginMarker = "# INTELLIGENCEX:BEGIN";
        const string endMarker = "# INTELLIGENCEX:END";
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  custom_pre:
    runs-on: ubuntu-latest
    steps:
      - run: echo pre
  __IX_BEGIN__
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-core.yml@master
    with:
      provider: openai
      model: gpt-5.4
  __IX_END__
  custom_post:
    runs-on: ubuntu-latest
    steps:
      - run: echo post
""";
        seed = seed.Replace("__IX_BEGIN__", beginMarker).Replace("__IX_END__", endMarker);

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            seed);

        AssertContainsText(content, "custom_pre:", "workflow upgrade keeps custom_pre");
        AssertContainsText(content, "custom_post:", "workflow upgrade keeps custom_post");
        AssertContainsText(content, "provider: copilot", "workflow upgrade updates managed provider");
        AssertContainsText(content, "needs-ai-review", "workflow upgrade keeps safety gate");
        AssertContainsText(content, beginMarker, "workflow upgrade keeps managed begin marker");
        AssertContainsText(content, endMarker, "workflow upgrade keeps managed end marker");
        AssertEqual(1, CountOccurrences(content, beginMarker),
            "workflow upgrade has single managed begin marker");
        AssertEqual(1, CountOccurrences(content, endMarker),
            "workflow upgrade has single managed end marker");
        AssertEqual(1, CountOccurrences(content, "provider: copilot"),
            "workflow upgrade has single provider override");

        var secondPass = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            content);
        AssertEqual(content, secondPass, "workflow upgrade idempotent on second pass");
    }

    private static void TestSetupWorkflowTemplateIncludesOpenAiAccountRoutingPassThrough() {
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-core.yml@master
    with:
      provider: openai
      model: gpt-5.4
""";

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(Array.Empty<string>(), seed);

        AssertContainsText(content, "needs-ai-review", "workflow template includes safety gate");
        AssertContainsText(content, "openai_account_id:", "workflow template openai account id input");
        AssertContainsText(content, "openai_account_ids:", "workflow template openai account ids input");
        AssertContainsText(content, "openai_account_rotation:", "workflow template openai account rotation input");
        AssertContainsText(content, "openai_account_failover:", "workflow template openai account failover input");
        AssertContainsText(content, "usage_budget_guard:", "workflow template usage budget guard input");
        AssertContainsText(content, "usage_budget_allow_credits:", "workflow template usage budget credits input");
        AssertContainsText(content, "usage_budget_allow_weekly_limit:",
            "workflow template usage budget weekly input");
        AssertContainsText(content, "copilot_model:", "workflow template copilot model input");
        AssertContainsText(content, "copilot_launcher:", "workflow template copilot launcher input");
        AssertContainsText(content, "profile:", "workflow template prompt profile input");
        AssertContainsText(content, "ci_context_enabled:", "workflow template CI context input");
        AssertContainsText(content, "history_enabled:", "workflow template history enabled input");
        AssertContainsText(content, "history_include_external_bot_summaries:",
            "workflow template external bot history input");
        AssertContainsText(content, "history_external_bot_logins:", "workflow template external bot login input");
        AssertContainsText(content, "swarm_enabled:", "workflow template swarm enabled input");
        AssertContainsText(content, "swarm_max_parallel:", "workflow template swarm max parallel input");
        AssertEqual(1, CountWorkflowOnEvent(content, "workflow_call"),
            "workflow template defines workflow_call exactly once");
        AssertContainsText(content, "provider:",
            "workflow template reusable-call provider input");
        AssertContainsText(content, "history_include_ix_summary_history:",
            "workflow template reusable-call IX summary history input");
        AssertContainsText(content, "swarm_publish_subreviews:",
            "workflow template reusable-call swarm subreview publishing input");
        AssertContainsText(content, "review_config_path:",
            "workflow template reusable-call review config path input");
        AssertContainsText(content, "openai_account_id: ${{ inputs.openai_account_id }}",
            "workflow template openai account id pass-through");
        AssertContainsText(content, "openai_account_ids: ${{ inputs.openai_account_ids }}",
            "workflow template openai account ids pass-through");
        AssertContainsText(content, "openai_account_rotation: ${{ inputs.openai_account_rotation }}",
            "workflow template openai account rotation pass-through");
        AssertContainsText(content, "openai_account_failover: ${{ inputs.openai_account_failover }}",
            "workflow template openai account failover pass-through");
        AssertContainsText(content, "usage_budget_guard: ${{ inputs.usage_budget_guard }}",
            "workflow template usage budget guard pass-through");
        AssertContainsText(content, "usage_budget_allow_credits: ${{ inputs.usage_budget_allow_credits }}",
            "workflow template usage budget credits pass-through");
        AssertContainsText(content, "usage_budget_allow_weekly_limit: ${{ inputs.usage_budget_allow_weekly_limit }}",
            "workflow template usage budget weekly pass-through");
        AssertContainsText(content, "agent_profile: ${{ inputs.agent_profile || vars.IX_REVIEW_AGENT_PROFILE }}",
            "workflow template agent profile input overrides repo variable");
        AssertContainsText(content, "copilot_model: ${{ inputs.copilot_model || vars.IX_REVIEW_COPILOT_MODEL }}",
            "workflow template copilot model input overrides repo variable");
        AssertContainsText(content, "copilot_launcher: ${{ inputs.copilot_launcher || vars.IX_REVIEW_COPILOT_LAUNCHER }}",
            "workflow template copilot launcher pass-through");
        AssertContainsText(content, "profile: ${{ inputs.profile || 'balanced' }}",
            "workflow template prompt profile pass-through");
        AssertContainsText(content, "mode: ${{ inputs.mode || 'hybrid' }}",
            "workflow template review mode pass-through");
        AssertContainsText(content, "length: ${{ inputs.length || 'medium' }}",
            "workflow template review length pass-through");
        AssertContainsText(content, "style: ${{ inputs.style || 'direct' }}",
            "workflow template review style pass-through");
        AssertContainsText(content, "review_config_path: ${{ inputs.review_config_path || '.intelligencex/reviewer.json' }}",
            "workflow template review config path pass-through");
        AssertContainsText(content, "max_files: ${{ fromJSON(inputs.max_files || '30') }}",
            "workflow template max files pass-through");
        AssertContainsText(content, "diagnostics: ${{ fromJSON(inputs.diagnostics || 'false') }}",
            "workflow template diagnostics pass-through");
        AssertContainsText(content,
            "(inputs.copilot_launcher || vars.IX_REVIEW_COPILOT_LAUNCHER) == 'auto' || vars.IX_REVIEW_COPILOT_AUTO_INSTALL == 'true'",
            "workflow template copilot auto-install respects launcher repo variable");
        AssertContainsText(content, "history_enabled: ${{ inputs.history_enabled }}",
            "workflow template history enabled pass-through");
        AssertContainsText(content,
            "history_include_external_bot_summaries: ${{ inputs.history_include_external_bot_summaries }}",
            "workflow template external bot history pass-through");
        AssertContainsText(content, "history_external_bot_logins: ${{ inputs.history_external_bot_logins }}",
            "workflow template external bot login pass-through");
        AssertContainsText(content, "swarm_enabled: ${{ inputs.swarm_enabled }}",
            "workflow template swarm enabled pass-through");
        AssertContainsText(content, "swarm_max_parallel: ${{ inputs.swarm_max_parallel }}",
            "workflow template swarm max parallel pass-through");
    }

    private static void TestSetupWorkflowTemplateIncludesOpenAiModelPassThrough() {
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-core.yml@master
    with:
      provider: openai
      model: gpt-5.4
""";

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(Array.Empty<string>(), seed);

        AssertContainsText(content, "needs-ai-review", "workflow template openai model includes safety gate");
        AssertContainsText(content, "openai_model:", "workflow template openai model input");
        AssertContainsText(content, "openai_model: ${{ inputs.openai_model }}",
            "workflow template openai model pass-through");
        AssertContainsText(content, "model: gpt-5.4", "workflow template default model updated");
    }

    private static void TestReviewReusableWorkflowDispatchIncludesOpenAiModelInput() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "review-intelligencex-core.yml");
        var content = NormalizeWorkflowText(File.ReadAllText(workflowPath));
        var wrapperWorkflowPath = ResolveRepoFilePath(".github", "workflows", "review-intelligencex.yml");
        var wrapperContent = NormalizeWorkflowText(File.ReadAllText(wrapperWorkflowPath));

        AssertContainsText(wrapperContent, "workflow_dispatch:", "wrapper workflow defines workflow_dispatch");
        AssertEqual(true, CountWorkflowDispatchInputs(wrapperContent) <= 25,
            "wrapper workflow stays within GitHub workflow_dispatch input limit");
        AssertContainsText(wrapperContent, "reviewer_source: source",
            "wrapper workflow keeps PR reviews on repo source to avoid release drift");
        AssertContainsText(wrapperContent, "workflow_call:",
            "wrapper workflow exposes reusable call overrides outside the dispatch input budget");
        AssertEqual(1, CountWorkflowOnEvent(wrapperContent, "workflow_call"),
            "wrapper workflow defines workflow_call exactly once");
        AssertContainsText(wrapperContent, "provider:",
            "wrapper workflow exposes provider reusable-call override");
        AssertContainsText(wrapperContent, "agent_profile:",
            "wrapper workflow exposes agent profile reusable-call override");
        AssertContainsText(wrapperContent, "copilot_model:",
            "wrapper workflow exposes copilot model reusable-call override");
        AssertContainsText(wrapperContent, "profile:",
            "wrapper workflow exposes prompt profile reusable-call override");
        AssertContainsText(wrapperContent, "ci_context_enabled:",
            "wrapper workflow exposes CI context reusable-call override");
        AssertContainsText(wrapperContent, "history_include_ix_summary_history:",
            "wrapper workflow exposes IX summary history reusable-call override");
        AssertContainsText(wrapperContent, "swarm_publish_subreviews:",
            "wrapper workflow exposes swarm subreview publishing reusable-call override");
        AssertContainsText(wrapperContent, "review_config_path:",
            "wrapper workflow exposes review config path reusable-call override");
        AssertContainsText(wrapperContent, "agent_profile: ${{ inputs.agent_profile || vars.IX_REVIEW_AGENT_PROFILE }}",
            "wrapper workflow lets reusable agent profile input override repo variable");
        AssertContainsText(wrapperContent, "swarm_metrics:",
            "wrapper workflow preserves swarm metrics manual override");
        AssertContainsText(wrapperContent, "copilot_model: ${{ inputs.copilot_model || vars.IX_REVIEW_COPILOT_MODEL }}",
            "wrapper workflow lets reusable copilot model input override repo variable");
        AssertContainsText(wrapperContent, "copilot_launcher: ${{ inputs.copilot_launcher || vars.IX_REVIEW_COPILOT_LAUNCHER }}",
            "wrapper workflow passes copilot launcher through to reusable workflow");
        AssertContainsText(wrapperContent,
            "(inputs.copilot_launcher || vars.IX_REVIEW_COPILOT_LAUNCHER) == 'auto' || vars.IX_REVIEW_COPILOT_AUTO_INSTALL == 'true'",
            "wrapper workflow copilot auto-install respects launcher repo variable");
        AssertContainsText(wrapperContent, "history_enabled: ${{ inputs.history_enabled }}",
            "wrapper workflow passes history awareness through to reusable workflow");
        AssertContainsText(wrapperContent,
            "history_include_external_bot_summaries: ${{ inputs.history_include_external_bot_summaries }}",
            "wrapper workflow passes external bot history through to reusable workflow");
        AssertContainsText(wrapperContent, "swarm_enabled: ${{ inputs.swarm_enabled }}",
            "wrapper workflow passes swarm mode through to reusable workflow");
        AssertContainsText(wrapperContent, "swarm_max_parallel: ${{ inputs.swarm_max_parallel }}",
            "wrapper workflow passes swarm max parallel through to reusable workflow");
        AssertContainsText(wrapperContent, "profile: ${{ inputs.profile || 'balanced' }}",
            "wrapper workflow passes prompt profile through with default");
        AssertContainsText(wrapperContent, "mode: ${{ inputs.mode || 'hybrid' }}",
            "wrapper workflow passes review mode through with default");
        AssertContainsText(wrapperContent, "length: ${{ inputs.length || 'medium' }}",
            "wrapper workflow passes review length through with default");
        AssertContainsText(wrapperContent, "style: ${{ inputs.style || 'direct' }}",
            "wrapper workflow passes review style through with default");
        AssertContainsText(wrapperContent, "review_config_path: ${{ inputs.review_config_path || '.intelligencex/reviewer.json' }}",
            "wrapper workflow passes review config path through with default");
        AssertContainsText(wrapperContent, "max_files: ${{ fromJSON(inputs.max_files || '30') }}",
            "wrapper workflow passes max files through with default");
        AssertContainsText(wrapperContent, "diagnostics: ${{ fromJSON(inputs.diagnostics || 'false') }}",
            "wrapper workflow passes diagnostics through with default");
        AssertEqual(false, content.Contains("workflow_dispatch:", StringComparison.Ordinal),
            "reusable workflow should keep manual dispatch on the wrapper workflow");
        var jobEnvIndex = content.IndexOf("    env:\n", StringComparison.Ordinal);
        var permissionsIndex = content.IndexOf("    permissions:\n", StringComparison.Ordinal);
        AssertEqual(true, jobEnvIndex > 0, "reusable workflow contains job env section");
        AssertEqual(true, permissionsIndex > jobEnvIndex, "reusable workflow contains permissions after job env");
        var jobEnvContent = content[jobEnvIndex..permissionsIndex];
        var sourceStep = ExtractWorkflowStepBlock(content, "Run IntelligenceX.Reviewer (source)");
        var releaseUnixStep = ExtractWorkflowStepBlock(content, "Run IntelligenceX.Reviewer (release, unix)");
        var releaseWindowsStep = ExtractWorkflowStepBlock(content, "Run IntelligenceX.Reviewer (release, windows)");
        AssertContainsText(content, "workflow_call:", "reusable workflow defines workflow_call");
        AssertEqual(1, CountOccurrences(content, "openai_model:"),
            "reusable workflow defines openai_model once for workflow_call");
        AssertEqual(1, CountOccurrences(content, "copilot_launcher:"),
            "reusable workflow defines copilot_launcher once for workflow_call");
        AssertEqual(1, CountOccurrences(content, "history_enabled:"),
            "reusable workflow defines history_enabled once for workflow_call");
        AssertEqual(1, CountOccurrences(content, "history_include_external_bot_summaries:"),
            "reusable workflow defines external bot history once for workflow_call");
        AssertEqual(1, CountOccurrences(content, "swarm_max_parallel:"),
            "reusable workflow defines swarm max parallel once for workflow_call");
        AssertContainsText(content, "default: 180",
            "reusable workflow gives PR-sized reviewer prompts a longer default wait window");
        AssertContainsText(content, "dotnet-version: '8.0.x'",
            "reusable workflow provisions the .NET 8 SDK for source reviewer runs");
        AssertContainsText(content, "dotnet build IntelligenceX.Reviewer/IntelligenceX.Reviewer.csproj -c Release -f net8.0",
            "reusable workflow pins the source reviewer build to net8.0 on the provisioned SDK");
        AssertContainsText(content, "log_path=\"artifacts/reviewer-run-source.log\"",
            "reusable workflow captures source reviewer build output in the shared source log");
        AssertEqual(1, CountOccurrences(content, "REVIEW_FAIL_OPEN: true"),
            "reusable workflow exports fail-open default once at the job level");
        AssertEqual(1, CountOccurrences(content, "REVIEW_FAIL_OPEN_TRANSIENT_ONLY: false"),
            "reusable workflow exports non-transient fail-open default once at the job level");
        AssertEqual(false, jobEnvContent.Contains("INTELLIGENCEX_AUTH_B64:", StringComparison.Ordinal),
            "reusable workflow does not expose auth bundle at job scope");
        AssertEqual(false, jobEnvContent.Contains("ANTHROPIC_API_KEY:", StringComparison.Ordinal),
            "reusable workflow does not expose provider api key at job scope");
        AssertContainsText(jobEnvContent, "COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}",
            "reusable workflow exposes Copilot CLI token at job scope for child prompt process auth");
        AssertContainsText(sourceStep, "INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}",
            "source reviewer step receives auth bundle");
        AssertContainsText(sourceStep, "ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}",
            "source reviewer step receives provider api key");
        AssertContainsText(releaseUnixStep, "INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}",
            "release unix reviewer step receives auth bundle");
        AssertContainsText(releaseUnixStep, "ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}",
            "release unix reviewer step receives provider api key");
        AssertContainsText(releaseWindowsStep, "INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}",
            "release windows reviewer step receives auth bundle");
        AssertContainsText(releaseWindowsStep, "ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}",
            "release windows reviewer step receives provider api key");
        AssertEqual(1, CountOccurrences(content, "INPUT_PROVIDER: ${{ inputs.provider }}"),
            "reusable workflow defines shared reviewer env once instead of repeating it per step");
        AssertEqual(1, CountOccurrences(content, "INPUT_COPILOT_LAUNCHER: ${{ inputs.copilot_launcher }}"),
            "reusable workflow exports copilot launcher env once");
        AssertEqual(1, CountOccurrences(content, "INPUT_HISTORY_ENABLED: ${{ inputs.history_enabled }}"),
            "reusable workflow exports history enabled env once");
        AssertEqual(1, CountOccurrences(content,
                "INPUT_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES: ${{ inputs.history_include_external_bot_summaries }}"),
            "reusable workflow exports external bot history env once");
        AssertContainsText(content, "inputs.reviewer_source == 'source' && steps.reviewer_build.outcome == 'success'",
            "reusable workflow gates source reviewer execution on a successful source build");
        AssertContainsText(content, "git diff --name-only HEAD^1 HEAD^2 > artifacts/changed-files.txt",
            "reusable workflow falls back to merge-parent changed-files diff");
        AssertContainsText(content, "-p:EnableWindowsTargeting=true -- analyze run",
            "reusable workflow enables windows targeting for analysis pre-run");
        AssertContainsText(content, "-p:EnableWindowsTargeting=true -- analyze gate",
            "reusable workflow enables windows targeting for analysis gate");
        AssertEqual(false, content.Contains("""
      - name: Prepare static analysis context
        if: ${{ inputs.reviewer_source == 'source' }}
""", StringComparison.Ordinal),
            "reusable workflow keeps static analysis context independent from reviewer_source");
        AssertEqual(false, content.Contains("""
      - name: Run IntelligenceX analysis (pre-review, best-effort)
        if: ${{ inputs.reviewer_source == 'source' }}
""", StringComparison.Ordinal),
            "reusable workflow keeps static analysis pre-run independent from reviewer_source");
        AssertEqual(false, content.Contains("""
      - name: Evaluate IntelligenceX analysis gate (pre-review, enforcing)
        if: ${{ inputs.reviewer_source == 'source' }}
""", StringComparison.Ordinal),
            "reusable workflow keeps analysis gate independent from reviewer_source");
        AssertContainsText(content, "Finalize fail-open reviewer summary",
            "reusable workflow finalizes fail-open reviewer runs with a summary update");
        AssertContainsText(content, "continue-on-error: true",
            "reusable workflow keeps fail-open finalization best-effort");
        AssertContainsText(content, "steps.reviewer_build.outcome == 'failure'",
            "reusable workflow finalizes fail-open summaries when the source reviewer build fails");
        AssertContainsText(content, "INTELLIGENCEX_GITHUB_TOKEN: ${{ steps.app_token.outputs.token || secrets.GITHUB_TOKEN }}",
            "reusable workflow passes the app token to fail-open summary finalization");
        AssertEqual(false, content.Contains("github.event_name == 'pull_request'", StringComparison.Ordinal),
            "reusable workflow allows fail-open finalization for workflow_dispatch PR reviews too");
        AssertContainsText(content, "ci review-fail-open-summary",
            "reusable workflow delegates fail-open summary handling to the CLI helper");
        AssertContainsText(content, "--source-log artifacts/reviewer-run-source.log",
            "reusable workflow passes source reviewer log path to CLI helper");
        AssertContainsText(content, "--release-unix-log artifacts/reviewer-run-release-unix.log",
            "reusable workflow passes release unix log path to CLI helper");
        AssertContainsText(content, "--release-windows-log artifacts/reviewer-run-release-windows.log",
            "reusable workflow passes release windows log path to CLI helper");
        AssertEqual(false, content.Contains("&review_inputs", StringComparison.Ordinal),
            "reusable workflow should avoid YAML anchors in workflow schema");
        AssertEqual(false, content.Contains("*review_inputs", StringComparison.Ordinal),
            "reusable workflow should avoid YAML aliases in workflow schema");
    }

    private static string NormalizeWorkflowText(string content) {
        return content.Replace("\r\n", "\n");
    }

    private static int CountWorkflowDispatchInputs(string content) {
        var normalized = NormalizeWorkflowText(content);
        const string marker = "  workflow_dispatch:\n    inputs:\n";
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        AssertEqual(true, start >= 0, "workflow_dispatch inputs block exists");
        start += marker.Length;
        var end = normalized.IndexOf("\n  workflow_call:", start, StringComparison.Ordinal);
        if (end < 0) {
            end = normalized.IndexOf("\njobs:", start, StringComparison.Ordinal);
        }
        if (end < 0) {
            end = normalized.Length;
        }

        var count = 0;
        foreach (var line in normalized[start..end].Split('\n')) {
            if (line.StartsWith("      ", StringComparison.Ordinal) &&
                !line.StartsWith("        ", StringComparison.Ordinal) &&
                line.TrimEnd().EndsWith(":", StringComparison.Ordinal)) {
                count++;
            }
        }
        return count;
    }

    private static int CountWorkflowOnEvent(string content, string eventName) {
        var normalized = NormalizeWorkflowText(content);
        var startsAtTop = normalized.StartsWith("on:\n", StringComparison.Ordinal);
        var onIndex = normalized.IndexOf("\non:\n", StringComparison.Ordinal);
        AssertEqual(true, startsAtTop || onIndex >= 0, "workflow on block exists");
        var start = startsAtTop ? "on:\n".Length : onIndex + "\non:\n".Length;
        var end = normalized.IndexOf("\njobs:", start, StringComparison.Ordinal);
        if (end < 0) {
            end = normalized.Length;
        }

        var count = 0;
        var expected = $"  {eventName}:";
        foreach (var line in normalized[start..end].Split('\n')) {
            if (string.Equals(line.TrimEnd(), expected, StringComparison.Ordinal)) {
                count++;
            }
        }

        return count;
    }

    private static string ExtractWorkflowStepBlock(string content, string stepName) {
        var normalized = NormalizeWorkflowText(content);
        var startMarker = $"      - name: {stepName}\n";
        var start = normalized.IndexOf(startMarker, StringComparison.Ordinal);
        AssertEqual(true, start >= 0, $"workflow contains step '{stepName}'");
        var next = normalized.IndexOf("\n      - name: ", start + startMarker.Length, StringComparison.Ordinal);
        if (next < 0) {
            next = normalized.Length;
        }
        return normalized[start..next];
    }

    private static void TestSetupWorkflowTemplateExplicitSecretsIncludesDiagnosticsAndPreflightPassThrough() {
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-core.yml@master
    with:
      provider: openai
      model: gpt-5.4
""";

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(new[] {
            "--explicit-secrets", "true"
        }, seed);

        AssertContainsText(content, "diagnostics:", "workflow explicit-secrets diagnostics input");
        AssertContainsText(content, "preflight:", "workflow explicit-secrets preflight input");
        AssertContainsText(content, "preflight_timeout_seconds:", "workflow explicit-secrets preflight timeout input");
        AssertContainsText(content, "copilot_auto_install:", "workflow explicit-secrets Copilot auto-install input");
        AssertContainsText(content, "(inputs.copilot_launcher || vars.IX_REVIEW_COPILOT_LAUNCHER) == 'auto'",
            "workflow explicit-secrets maps Copilot launcher auto to auto-install");
        AssertContainsText(content, "copilot_auto_install_method:",
            "workflow explicit-secrets Copilot auto-install method input");
        AssertContainsText(content, "copilot_auto_install_prerelease:",
            "workflow explicit-secrets Copilot prerelease input");
        AssertContainsText(content, "INTELLIGENCEX_AUTH_B64:", "workflow explicit-secrets includes auth bundle mapping");
        AssertContainsText(content, "COPILOT_GITHUB_TOKEN:",
            "workflow explicit-secrets includes Copilot CLI token mapping");
        AssertEqual(false, content.Contains("INTELLIGENCEX_AUTH_KEY:", StringComparison.Ordinal),
            "workflow explicit-secrets does not pass undeclared auth key");
    }

    private static void TestSetupWorkflowTemplateNonExplicitSecretsUsesInheritMode() {
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-core.yml@master
    with:
      provider: openai
      model: gpt-5.4
""";

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(new[] {
            "--explicit-secrets", "false"
        }, seed);

        AssertContainsText(content, "secrets: inherit", "workflow non-explicit secrets inherit");
        AssertEqual(false, content.Contains("INTELLIGENCEX_AUTH_B64:", StringComparison.Ordinal),
            "workflow non-explicit secrets no explicit auth mapping");
        AssertEqual(false, content.Contains("INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY:", StringComparison.Ordinal),
            "workflow non-explicit secrets no explicit app key mapping");
    }

    private static void TestGitHubRepoDetectorParsesRemoteUrls() {
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo.git"), "https git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo"), "https no git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("git@github.com:owner/repo.git"), "ssh scp");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.com/owner/repo.git"), "ssh url");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.mycorp.local/owner/repo.git"), "ssh ghe");
        AssertEqual(null, GitHubRepoDetector.ParseRepoFromRemoteUrl("not a url"), "invalid url");
    }

    private static void TestGitHubRepoDetectorParsesGitConfigRemoteSection() {
        var config = """
[core]
    repositoryformatversion = 0
    url = SHOULD_NOT_MATCH
[remote "origin"]
    fetch = +refs/heads/*:refs/remotes/origin/*
    url = git@github.com:EvotecIT/IntelligenceX.git
[branch "main"]
    remote = origin
    merge = refs/heads/main
    url = ALSO_SHOULD_NOT_MATCH
[remote "upstream"]
    url = https://github.com/other/repo.git
""";

        AssertEqual("git@github.com:EvotecIT/IntelligenceX.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "origin"),
            "origin url");
        AssertEqual("https://github.com/other/repo.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "upstream"),
            "upstream url");
        AssertEqual(null, GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "missing"), "missing remote");
    }

    private static void TestGitHubRepoClientSecretLookupMapsStatusCodes() {
        static IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult RunLookup(
            System.Net.HttpStatusCode statusCode,
            string? reasonPhrase = null) {
            using var client = CreateGitHubRepoClientForTests((_, _) => {
                var response = new System.Net.Http.HttpResponseMessage(statusCode);
                if (reasonPhrase is not null) {
                    response.ReasonPhrase = reasonPhrase;
                }
                return Task.FromResult(response);
            });
            return client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
        }

        var present = RunLookup(System.Net.HttpStatusCode.OK);
        AssertEqual("present", present.Status, "repo client secret status present");
        AssertEqual(true, present.Exists, "repo client secret exists true");
        AssertEqual(null, present.Note, "repo client secret present note");

        var missing = RunLookup(System.Net.HttpStatusCode.NotFound);
        AssertEqual("missing", missing.Status, "repo client secret status missing");
        AssertEqual(false, missing.Exists, "repo client secret exists false");
        AssertEqual(null, missing.Note, "repo client secret missing note");

        var unauthorized = RunLookup(System.Net.HttpStatusCode.Unauthorized);
        AssertEqual("unauthorized", unauthorized.Status, "repo client secret status unauthorized");
        AssertEqual(null, unauthorized.Exists, "repo client secret unauthorized exists unknown");
        AssertContainsText(unauthorized.Note ?? string.Empty, "401 Unauthorized", "repo client secret unauthorized note");

        var forbidden = RunLookup(System.Net.HttpStatusCode.Forbidden);
        AssertEqual("forbidden", forbidden.Status, "repo client secret status forbidden");
        AssertEqual(null, forbidden.Exists, "repo client secret forbidden exists unknown");
        AssertContainsText(forbidden.Note ?? string.Empty, "403 Forbidden", "repo client secret forbidden note");

        var rateLimited = RunLookup((System.Net.HttpStatusCode)429);
        AssertEqual("rate_limited", rateLimited.Status, "repo client secret status rate limited");
        AssertEqual(null, rateLimited.Exists, "repo client secret rate limited exists unknown");
        AssertContainsText(rateLimited.Note ?? string.Empty, "429 Too Many Requests", "repo client secret rate limited note");

        var unknown = RunLookup(System.Net.HttpStatusCode.InternalServerError, "Boom");
        AssertEqual("unknown", unknown.Status, "repo client secret status unknown");
        AssertEqual(null, unknown.Exists, "repo client secret unknown exists unknown");
        AssertContainsText(unknown.Note ?? string.Empty, "500 Boom", "repo client secret unknown note");
    }

    private static void TestGitHubRepoClientSecretLookupMapsClientExceptions() {
        using (var httpFailureClient = CreateGitHubRepoClientForTests((_, _) =>
                   throw new HttpRequestException("socket failed"))) {
            var httpFailure = httpFailureClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64")
                .GetAwaiter().GetResult();
            AssertEqual("unknown", httpFailure.Status, "repo client secret http failure status");
            AssertEqual(null, httpFailure.Exists, "repo client secret http failure exists");
            AssertContainsText(httpFailure.Note ?? string.Empty, "HTTP client error", "repo client secret http failure note");
        }

        using (var invalidOperationClient = CreateGitHubRepoClientForTests((_, _) =>
                   throw new InvalidOperationException("invalid request uri"))) {
            var invalidOperation = invalidOperationClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64")
                .GetAwaiter().GetResult();
            AssertEqual("unknown", invalidOperation.Status, "repo client secret invalid operation status");
            AssertEqual(null, invalidOperation.Exists, "repo client secret invalid operation exists");
            AssertContainsText(invalidOperation.Note ?? string.Empty, "configuration error", "repo client secret invalid operation note");
        }
    }

    private static void TestGitHubRepoClientSecretLookupCancellationPropagates() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new OperationCanceledException("cancelled"));
        AssertThrows<OperationCanceledException>(() =>
                client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult(),
            "repo client secret cancellation");
    }

    private static void TestGitHubRepoClientListWorkflowRunsParsesLatestRun() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "workflow_runs": [
    {
      "id": 42,
      "html_url": "https://github.com/owner/repo/actions/runs/42",
      "status": "completed",
      "conclusion": "success",
      "head_branch": "main",
      "event": "pull_request",
      "created_at": "2026-02-11T20:00:00Z"
    }
  ]
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(true, lookup.Success, "repo client workflow runs lookup success");
        AssertEqual("ok", lookup.Status, "repo client workflow runs lookup status");
        AssertEqual(1, lookup.Runs.Count, "repo client workflow runs count");
        AssertEqual(42L, lookup.Runs[0].Id, "repo client workflow run id");
        AssertEqual("completed", lookup.Runs[0].Status, "repo client workflow run status");
        AssertEqual("success", lookup.Runs[0].Conclusion, "repo client workflow run conclusion");
        AssertContainsText(lookup.Runs[0].Url ?? string.Empty, "actions/runs/42", "repo client workflow run url");
    }

    private static void TestGitHubRepoClientWorkflowRunLookupResultUsesDefensiveCopy() {
        var sourceRuns = new List<IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo> {
            new(
                id: 7,
                url: "https://github.com/owner/repo/actions/runs/7",
                status: "completed",
                conclusion: "success",
                headBranch: "main",
                @event: "pull_request",
                createdAt: DateTimeOffset.Parse("2026-02-11T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        };
        var lookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunLookupResult.Ok(sourceRuns);
        sourceRuns.Clear();

        AssertEqual("ok", lookup.Status, "repo client workflow runs defensive copy status");
        AssertEqual(true, lookup.Success, "repo client workflow runs defensive copy success");
        AssertEqual(1, lookup.Runs.Count, "repo client workflow runs defensive copy count");
        AssertEqual(false, lookup.Runs is List<IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo>,
            "repo client workflow runs defensive copy list exposure");
    }

    private static void TestGitHubRepoClientListWorkflowRunsInvalidPayloadReturnsEmpty() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "workflow_runs": "invalid"
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(false, lookup.Success, "repo client workflow runs invalid payload lookup failure");
        AssertEqual("parse_error", lookup.Status, "repo client workflow runs invalid payload status");
        AssertEqual(0, lookup.Runs.Count, "repo client workflow runs invalid payload returns empty");
    }

    private static void TestGitHubRepoClientListWorkflowRunsEncodesPathSegments() {
        string? absolutePath = null;
        using var client = CreateGitHubRepoClientForTests((request, _) => {
            absolutePath = request.RequestUri?.AbsolutePath;
            var payload = """
{
  "workflow_runs": []
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner+team", "repo name", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(true, lookup.Success, "repo client workflow runs path encoding lookup success");
        AssertContainsText(absolutePath ?? string.Empty, "/repos/owner%2Bteam/repo%20name/actions/workflows/",
            "repo client workflow runs owner/repo segments encoded");
        AssertContainsText(absolutePath ?? string.Empty, ".github%2Fworkflows%2Freview-intelligencex.yml",
            "repo client workflow runs workflow path encoded");
    }

    private static void TestGitHubRepoClientListWorkflowRunsMapsUnauthorized() {
        using var client = CreateGitHubRepoClientForTests((_, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)));

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(false, lookup.Success, "repo client workflow runs unauthorized lookup failure");
        AssertEqual("unauthorized", lookup.Status, "repo client workflow runs unauthorized status");
        AssertEqual(0, lookup.Runs.Count, "repo client workflow runs unauthorized runs empty");
        AssertContainsText(lookup.Note ?? string.Empty, "401", "repo client workflow runs unauthorized note");
    }

    private static void TestGitHubRepoClientFileFetchCancellationPropagates() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new OperationCanceledException("cancelled"));
        AssertThrows<OperationCanceledException>(() =>
                client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult(),
            "repo client file fetch cancellation");
    }

    private static void TestGitHubRepoClientFileFetchInvalidBase64ReturnsNull() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "sha": "abc123",
  "content": "@@@not-base64@@@"
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var file = client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult();
        AssertEqual(null, file, "repo client file fetch invalid base64");
    }

    private static void TestGitHubRepoClientFileFetchMissingShaReturnsNull() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "content": "e30="
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var file = client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult();
        AssertEqual(null, file, "repo client file fetch missing sha");
    }

    private static void TestGitHubRepoClientInjectedHttpClientAppliesDefaultHeaders() {
        System.Net.Http.HttpRequestMessage? capturedRequest = null;
        using var client = CreateGitHubRepoClientForTests((request, _) => {
            capturedRequest = request;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }, token: "injected-token");

        var result = client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
        AssertEqual("missing", result.Status, "repo client injected headers lookup status");
        AssertNotNull(capturedRequest, "repo client injected headers captured request");
        AssertEqual("Bearer", capturedRequest!.Headers.Authorization?.Scheme, "repo client injected headers auth scheme");
        AssertEqual("injected-token", capturedRequest.Headers.Authorization?.Parameter, "repo client injected headers auth token");
        AssertEqual(true, capturedRequest.Headers.UserAgent.ToString().Contains("IntelligenceX.Cli"), "repo client injected headers user agent");
        AssertEqual(true, capturedRequest.Headers.Accept.ToString().Contains("application/vnd.github+json"), "repo client injected headers accept");
        AssertEqual(true,
            capturedRequest.Headers.TryGetValues("X-GitHub-Api-Version", out var values)
            && values.Contains("2022-11-28"),
            "repo client injected headers api version");
    }

    private static void TestGitHubRepoClientReusedInjectedHttpClientRemainsIdempotent() {
        var requests = new List<System.Net.Http.HttpRequestMessage>();
        using var http = new System.Net.Http.HttpClient(new DelegateHttpMessageHandler((request, _) => {
            requests.Add(request);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        })) {
            BaseAddress = new Uri("https://api.github.com")
        };

        using (var first = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token: "token-one")) {
            var firstResult = first.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual("missing", firstResult.Status, "repo client reused injected first status");
        }

        using (var second = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token: "token-two")) {
            var secondResult = second.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual("missing", secondResult.Status, "repo client reused injected second status");
        }

        AssertEqual(true, requests.Count >= 2, "repo client reused injected requests captured");
        var lastRequest = requests[requests.Count - 1];
        AssertEqual("token-two", lastRequest.Headers.Authorization?.Parameter, "repo client reused injected latest auth token");

        var userAgentCount = 0;
        foreach (var _ in lastRequest.Headers.UserAgent) {
            userAgentCount++;
        }
        AssertEqual(1, userAgentCount, "repo client reused injected user-agent count");

        var acceptCount = 0;
        foreach (var _ in lastRequest.Headers.Accept) {
            acceptCount++;
        }
        AssertEqual(1, acceptCount, "repo client reused injected accept count");

        var versionCount = 0;
        if (lastRequest.Headers.TryGetValues("X-GitHub-Api-Version", out var apiVersions)) {
            foreach (var _ in apiVersions) {
                versionCount++;
            }
        }
        AssertEqual(1, versionCount, "repo client reused injected api version count");
    }

    private static IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient CreateGitHubRepoClientForTests(
        Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> sendAsync,
        string token = "test-token") {
        var http = new System.Net.Http.HttpClient(new DelegateHttpMessageHandler(sendAsync)) {
            BaseAddress = new Uri("https://api.github.com")
        };
        return new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token);
    }

    private static (int ExitCode, string Output) RunSetupAutodetectAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectCliRunner.RunAsync(args)
                .GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed class DelegateHttpMessageHandler : System.Net.Http.HttpMessageHandler {
        private readonly Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> _sendAsync;

        public DelegateHttpMessageHandler(
            Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> sendAsync) {
            _sendAsync = sendAsync;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return _sendAsync(request, cancellationToken);
        }
    }

    private static void TestGitHubSecretsRejectEmptyValue() {
        using var client = new GitHubSecretsClient("token");
        AssertThrows<InvalidOperationException>(() =>
            client.SetRepoSecretAsync("owner", "repo", "SECRET_NAME", "").GetAwaiter().GetResult(),
            "repo secret empty");
        AssertThrows<InvalidOperationException>(() =>
            client.SetOrgSecretAsync("org", "SECRET_NAME", " ").GetAwaiter().GetResult(),
            "org secret empty");
    }

    private static void TestReleaseReviewerEnvToken() {
        var previous = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", "token-value");
            var options = new ReleaseReviewerOptions();
            ReleaseReviewerOptions.ApplyEnvDefaults(options);
            AssertEqual("token-value", options.Token, "reviewer token");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", previous);
        }
    }
    #endif
}
