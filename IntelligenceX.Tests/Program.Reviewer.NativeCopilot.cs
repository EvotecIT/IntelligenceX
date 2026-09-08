namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestNativeCopilotSetupModelReachesReviewer() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"ix-copilot-setup-{Guid.NewGuid():N}.json");
        try {
            foreach (string model in new[] { "gpt-5.4", OpenAIModelCatalog.DefaultModel }) {
                var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForProviderModelTests("copilot", model);
                File.WriteAllText(path, IntelligenceX.Cli.Setup.SetupRunner.BuildReviewerConfigJson(args));
                Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
                var settings = new ReviewSettings();
                ReviewConfigLoader.Apply(settings);
                var options = new ReviewRunner(settings).BuildCopilotClientOptionsForTests();
                AssertEqual(OpenAITransportKind.CopilotNative, options.TransportKind, "generated config selects native transport");
                AssertEqual(model, options.DefaultModel, "generated config preserves selected model");
            }
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestNativeCopilotProfileCredentialsReplaceAndRestoreBaseline() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"ix-copilot-profile-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, """
                {"review":{"agentProfiles":{
                  "token":{"authenticator":"copilot-native","model":"model","copilot":{"token":"profile-token"}},
                  "env":{"authenticator":"copilot-native","model":"model","copilot":{"tokenEnv":"PROFILE_TOKEN"}},
                  "baseline":{"authenticator":"copilot-native","model":"model"}
                }}}
                """);
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            foreach (bool baselineToken in new[] { false, true }) {
                var settings = new ReviewSettings { CopilotToken = baselineToken ? "baseline-token" : null,
                    CopilotTokenEnvironmentVariable = baselineToken ? null : "BASELINE_TOKEN" };
                ReviewConfigLoader.Apply(settings);
                settings.ApplyAgentProfile("token");
                var configured = new ReviewRunner(settings).BuildCopilotClientOptionsForTests().CopilotOptions;
                AssertEqual("profile-token", configured.GitHubToken, "profile static token reaches actual client");
                AssertEqual(true, configured.TokenProvider is null, "static token clears inherited env callback");
                settings.ApplyAgentProfile("env");
                configured = new ReviewRunner(settings).BuildCopilotClientOptionsForTests().CopilotOptions;
                AssertEqual(true, configured.GitHubToken is null, "profile env clears inherited static token");
                AssertEqual(true, configured.TokenProvider is not null, "profile env reaches actual client");
                settings.ApplyAgentProfile("baseline");
                AssertEqual(baselineToken ? "baseline-token" : null, settings.CopilotToken, "static token baseline restored");
                AssertEqual(baselineToken ? null : "BASELINE_TOKEN", settings.CopilotTokenEnvironmentVariable, "env baseline restored");
            }
            File.WriteAllText(path, """
                {"review":{"agentProfiles":{"conflict":{"copilot":{"token":"one","tokenEnv":"TWO"}}}}}
                """);
            AssertThrows<InvalidOperationException>(() => ReviewConfigLoader.Apply(new ReviewSettings()), "ambiguous profile credentials rejected");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestNativeCopilotProfileSwitchRestoresBaseline() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAI, Model = "chatgpt-model", CopilotModel = "baseline-copilot",
            CopilotBaseUrl = "https://baseline.example/", CopilotTokenEnvironmentVariable = "BASELINE_TOKEN",
            CopilotRequestTimeoutSeconds = 45
        };
        var copilot = new ReviewAgentProfileSettings { Id = "copilot", Authenticator = "copilot-native", Model = "profile-model",
            CopilotBaseUrl = "https://profile.example/", CopilotTokenEnvironmentVariable = "PROFILE_TOKEN", CopilotRequestTimeoutSeconds = 120 };
        settings.ApplyAgentProfile(copilot);
        var client = new ReviewRunner(settings).BuildCopilotClientOptionsForTests();
        AssertEqual(OpenAITransportKind.CopilotNative, client.TransportKind, "native profile transport");
        AssertEqual("profile-model", client.DefaultModel, "native profile actual request model");
        AssertEqual("https://profile.example/", client.CopilotOptions.BaseUrl, "native profile endpoint");
        AssertEqual(TimeSpan.FromSeconds(120), client.CopilotOptions.RequestTimeout, "native profile timeout");
        settings.ApplyAgentProfile(new ReviewAgentProfileSettings { Id = "chatgpt", Authenticator = "chatgpt" });
        AssertEqual(ReviewProvider.OpenAI, settings.Provider, "switched provider");
        AssertEqual("chatgpt-model", settings.Model, "restored baseline model");
        AssertEqual("baseline-copilot", settings.CopilotModel, "restored baseline Copilot model");
        AssertEqual("https://baseline.example/", settings.CopilotBaseUrl, "restored baseline endpoint");
        AssertEqual("BASELINE_TOKEN", settings.CopilotTokenEnvironmentVariable, "restored baseline credential source");
        AssertEqual(45, settings.CopilotRequestTimeoutSeconds, "restored baseline timeout");
    }

    private static void TestNativeCopilotCredentialCallbackReadsOnlyItsConfiguredVariable() {
        const string variable = "IX_NATIVE_COPILOT_PROFILE_TEST_TOKEN";
        var previous = Environment.GetEnvironmentVariable(variable);
        try {
            Environment.SetEnvironmentVariable(variable, "first");
            var options = new ReviewRunner(new ReviewSettings { Provider = ReviewProvider.Copilot, CopilotModel = "selected-model",
                CopilotTokenEnvironmentVariable = variable }).BuildCopilotClientOptionsForTests();
            AssertEqual(false, options.CopilotOptions.UseEnvironmentCredentials, "explicit variable disables ambient fallback");
            AssertEqual("first", options.CopilotOptions.TokenProvider!(default).GetAwaiter().GetResult(), "first host credential");
            Environment.SetEnvironmentVariable(variable, "rotated");
            AssertEqual("rotated", options.CopilotOptions.TokenProvider!(default).GetAwaiter().GetResult(), "rotated host credential");
            Environment.SetEnvironmentVariable(variable, null);
            AssertThrows<InvalidOperationException>(() => options.CopilotOptions.TokenProvider!(default).GetAwaiter().GetResult(), "missing configured credential");
        } finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    private static void TestNativeCopilotSwarmProfilesPreserveProviderAndModelOverrides() {
        var profile = new ReviewAgentProfileSettings { Id = "copilot", Authenticator = "copilot-native", Model = "profile-model",
            CopilotBaseUrl = "https://profile.example/", CopilotTokenEnvironmentVariable = "PROFILE_TOKEN" };
        var settings = new ReviewSettings { Provider = ReviewProvider.OpenAI, Model = "primary-model",
            AgentProfiles = new Dictionary<string, ReviewAgentProfileSettings> { [profile.Id] = profile } };
        settings.Swarm.Enabled = true;
        settings.Swarm.ShadowMode = true;
        settings.Swarm.ReviewerSettings = new[] {
            new ReviewSwarmReviewerSettings { Id = "correctness", AgentProfile = profile.Id },
            new ReviewSwarmReviewerSettings { Id = "tests", AgentProfile = profile.Id, Model = "reviewer-model" }
        };
        settings.Swarm.Aggregator.AgentProfile = profile.Id;
        var plan = ReviewRunner.BuildSwarmShadowPlanForTests(settings);
        AssertEqual(ReviewProvider.Copilot, plan.Reviewers[0].Provider, "profile provider");
        AssertEqual("profile-model", plan.Reviewers[0].Model, "profile model");
        AssertEqual("reviewer-model", plan.Reviewers[1].Model, "reviewer override");
        AssertEqual("PROFILE_TOKEN", plan.Reviewers[0].ResolvedAgentProfile!.CopilotTokenEnvironmentVariable, "swarm credential source");
        AssertEqual(ReviewProvider.Copilot, plan.Aggregator.Provider, "aggregator provider");
        AssertEqual("profile-model", plan.Aggregator.Model, "aggregator model");
        AssertEqual(ReviewProvider.OpenAI, settings.Provider, "planner preserves primary provider");
        AssertEqual("primary-model", settings.Model, "planner preserves primary model");
    }
}
#endif
