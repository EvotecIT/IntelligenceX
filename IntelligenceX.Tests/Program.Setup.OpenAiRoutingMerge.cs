namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupBuildConfigJsonNormalizesOpenAiPrimaryInAccountIds() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--openai-account-id", "  acc-primary  ",
            "--openai-account-ids", " ACC-primary , acc-backup "
        });
        AssertNotNull(content, "config json openai primary normalization content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai primary normalization root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai primary normalization review");
        AssertEqual("acc-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai primary normalization trimmed primary");
        var ids = review!["openaiAccountIds"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(ids, "config json openai primary normalization account ids");
        AssertEqual(2, ids!.Count, "config json openai primary normalization account ids count");
        AssertEqual("acc-primary", ids![0]?.GetValue<string>(), "config json openai primary normalization first id");
        AssertEqual("acc-backup", ids[1]?.GetValue<string>(), "config json openai primary normalization second id");
    }

    private static void TestSetupBuildConfigJsonPersistsOpenAiRoutingWithPrimaryOnly() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--openai-account-id", "acc-primary",
            "--openai-account-rotation", "round-robin",
            "--openai-account-failover", "false"
        });
        AssertNotNull(content, "config json openai primary-only routing content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai primary-only routing root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai primary-only routing review");
        AssertEqual("acc-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai primary-only routing primary account");
        AssertEqual("round-robin", review["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai primary-only routing rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai primary-only routing failover");
        AssertEqual(null, review["openaiAccountIds"], "config json openai primary-only routing no account ids");
    }

    private static void TestSetupBuildConfigJsonMergePersistsOpenAiRoutingWithPrimaryOnly() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.4"
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] {
                "--openai-account-id", "acc-primary",
                "--openai-account-rotation", "round-robin",
                "--openai-account-failover", "false"
            },
            seed);
        AssertNotNull(content, "config json openai merge primary-only routing content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai merge primary-only routing root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai merge primary-only routing review");
        AssertEqual("acc-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai merge primary-only routing primary account");
        AssertEqual("round-robin", review["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai merge primary-only routing rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai merge primary-only routing failover");
        AssertEqual(null, review["openaiAccountIds"], "config json openai merge primary-only routing no account ids");
    }

    private static void TestSetupBuildConfigJsonMergePreservesOpenAiRoutingWhenAccountIdsAbsent() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiAccountId": "acc-primary",
    "openaiAccountRotation": "sticky",
    "openaiAccountFailover": false
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--with-config", "--analysis-enabled", "true" },
            seed);
        AssertNotNull(content, "config json openai merge preserve content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai merge preserve root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai merge preserve review");
        AssertEqual("sticky", review!["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai merge preserve rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai merge preserve failover");
        AssertEqual(null, review["openaiAccountIds"], "config json openai merge preserve no synthesized ids");
    }

    private static void TestSetupBuildConfigJsonMergeClearsOpenAiRoutingWhenAccountIdsExplicitlyEmpty() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiAccountIds": [
      "acc-primary",
      "acc-backup"
    ],
    "openaiAccountRotation": "sticky",
    "openaiAccountFailover": false
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--openai-account-ids", string.Empty },
            seed);
        AssertNotNull(content, "config json openai merge clear content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai merge clear root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai merge clear review");
        AssertEqual(null, review!["openaiAccountIds"], "config json openai merge clear ids");
        AssertEqual(null, review["openaiAccountRotation"], "config json openai merge clear rotation");
        AssertEqual(null, review["openaiAccountFailover"], "config json openai merge clear failover");
    }

    private static void TestSetupBuildConfigJsonMergeClearsOpenAiIdsButKeepsRoutingWithPrimary() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiAccountIds": [
      "acc-old-primary",
      "acc-old-backup"
    ],
    "openaiAccountRotation": "sticky",
    "openaiAccountFailover": false
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] {
                "--openai-account-id", "acc-primary",
                "--openai-account-ids", string.Empty
            },
            seed);
        AssertNotNull(content, "config json openai merge explicit-empty ids with primary content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai merge explicit-empty ids with primary root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai merge explicit-empty ids with primary review");
        AssertEqual("acc-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai merge explicit-empty ids with primary account");
        AssertEqual(null, review["openaiAccountIds"], "config json openai merge explicit-empty ids removes ids");
        AssertEqual("sticky", review["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai merge explicit-empty ids keeps rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai merge explicit-empty ids keeps failover");
    }

    private static void TestSetupBuildConfigJsonMergeClearsOpenAiIdsWhenSnapshotHasPrimary() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiAccountId": "acc-old-primary",
    "openaiAccountIds": [
      "acc-old-primary",
      "acc-old-backup"
    ],
    "openaiAccountRotation": "sticky",
    "openaiAccountFailover": false
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--openai-account-ids", string.Empty },
            seed);
        AssertNotNull(content, "config json openai merge explicit-empty ids snapshot primary content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai merge explicit-empty ids snapshot primary root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai merge explicit-empty ids snapshot primary review");
        AssertEqual("acc-old-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai merge explicit-empty ids snapshot primary account retained");
        AssertEqual(null, review["openaiAccountIds"],
            "config json openai merge explicit-empty ids snapshot primary removes ids");
        AssertEqual("sticky", review["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai merge explicit-empty ids snapshot primary keeps rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai merge explicit-empty ids snapshot primary keeps failover");
    }

    private static void TestSetupBuildConfigJsonMergeSwitchesOpenAiSeedToClaudeAndClearsOpenAiFields() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.4",
    "openaiTransport": "native",
    "openaiAccountId": "acc-primary",
    "openaiAccountIds": [
      "acc-primary",
      "acc-backup"
    ],
    "openaiAccountRotation": "round-robin",
    "openaiAccountFailover": false
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--provider", "claude" },
            seed);
        AssertNotNull(content, "config json claude merge switch content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json claude merge switch root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json claude merge switch review");
        var anthropic = review!["anthropic"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(anthropic, "config json claude merge switch anthropic");

        AssertEqual("claude", review["provider"]?.GetValue<string>(), "config json claude merge switch provider");
        AssertEqual("claude-opus-4-1", review["model"]?.GetValue<string>(), "config json claude merge switch model");
        AssertEqual("ANTHROPIC_API_KEY", anthropic!["apiKeyEnv"]?.GetValue<string>(),
            "config json claude merge switch api key env");
        AssertEqual(null, review["openaiTransport"], "config json claude merge switch clears openai transport");
        AssertEqual(null, review["openaiAccountId"], "config json claude merge switch clears openai account id");
        AssertEqual(null, review["openaiAccountIds"], "config json claude merge switch clears openai account ids");
        AssertEqual(null, review["openaiAccountRotation"], "config json claude merge switch clears openai rotation");
        AssertEqual(null, review["openaiAccountFailover"], "config json claude merge switch clears openai failover");
    }
#endif
}
