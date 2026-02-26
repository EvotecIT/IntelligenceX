namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunPacksOverrideSkipsConfiguredCsharpFailure() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: true, packsOverride: "internal-default");
        AssertEqual(0, exit, "analyze run pack override skips configured csharp runner failure");
    }

    private static void TestAnalyzeRunStrictSkipsCsharpRunnerWithoutCsharpSources() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: true, includeCsharpSource: false);
        AssertEqual(0, exit, "analyze run strict skips csharp runner when no csharp sources are present");
    }

    private static void TestAnalyzeRunInvalidPackOverrideFails() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: false, packsOverride: "all-50,--force");
        AssertEqual(1, exit, "analyze run invalid pack override fails");
    }
}
#endif
