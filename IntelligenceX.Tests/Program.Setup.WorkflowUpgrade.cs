namespace IntelligenceX.Tests;

#if !NET472
internal static partial class Program {
    private static void TestSetupWorkflowUpgradePreservesOutsideManagedBlockVerbatim() {
        const string beginMarker = "# INTELLIGENCEX:BEGIN";
        const string endMarker = "# INTELLIGENCEX:END";
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]
  workflow_dispatch:
    inputs:
      target_ref:
        description: "Optional override"
        required: false
        default: ""

concurrency:
  group: custom-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read
  pull-requests: write

jobs:
  custom_pre:
    runs-on: ubuntu-latest
    steps:
      - run: echo pre
  __IX_BEGIN__
  review:
    uses: evotecit/intelligencex/.github/workflows/review-intelligencex-reusable.yml@master
    with:
      provider: openai
      model: gpt-5.4
      preflight_timeout_seconds: 15
  __IX_END__
  custom_post:
    runs-on: ubuntu-latest
    steps:
      - run: echo post
""";
        seed = seed.Replace("__IX_BEGIN__", beginMarker).Replace("__IX_END__", endMarker);

        var upgraded = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            seed);

        AssertContainsText(upgraded, "provider: copilot", "workflow upgrade updates provider");
        AssertEqual(1, CountOccurrencesInText(upgraded, beginMarker),
            "workflow upgrade has one begin marker");
        AssertEqual(1, CountOccurrencesInText(upgraded, endMarker),
            "workflow upgrade has one end marker");

        var seedOutside = NormalizeLineEndingsForWorkflowAssert(StripManagedBlockForWorkflowAssert(seed)).Trim();
        var upgradedOutside = NormalizeLineEndingsForWorkflowAssert(StripManagedBlockForWorkflowAssert(upgraded)).Trim();
        AssertEqual(seedOutside, upgradedOutside,
            "workflow upgrade preserves content outside managed block");

        var secondPass = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            upgraded);
        AssertEqual(upgraded, secondPass, "workflow upgrade remains idempotent after outside-block preserve");
    }

    private static int CountOccurrencesInText(string value, string marker) {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(marker)) {
            return 0;
        }
        var count = 0;
        var start = 0;
        while (true) {
            var index = value.IndexOf(marker, start, StringComparison.Ordinal);
            if (index < 0) {
                break;
            }
            count++;
            start = index + marker.Length;
        }
        return count;
    }

    private static string StripManagedBlockForWorkflowAssert(string content) {
        var pattern = @"^[ \t]*# INTELLIGENCEX:BEGIN[\s\S]*?^[ \t]*# INTELLIGENCEX:END[ \t]*\r?$";
        return System.Text.RegularExpressions.Regex.Replace(content, pattern, string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    private static string NormalizeLineEndingsForWorkflowAssert(string content) {
        return (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
#endif
