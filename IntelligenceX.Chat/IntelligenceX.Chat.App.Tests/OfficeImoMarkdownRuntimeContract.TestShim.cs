using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.App;

internal static class OfficeImoMarkdownRuntimeContract {
    public static MarkdownRendererOptions CreateTranscriptRendererOptions() {
        return MarkdownRendererPresets.CreateIntelligenceXTranscriptDesktopShell();
    }

    public static string ApplyTranscriptMarkdownPreProcessors(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return markdown;
        }

        return MarkdownRendererPreProcessorPipeline.Apply(
            markdown,
            MarkdownRendererPresets.CreateIntelligenceXTranscriptMinimal());
    }

    public static string DescribeMarkdownRendererContract() {
        return OfficeImoTestAssemblyContractDiagnostics.DescribeMarkdownRendererContract();
    }

    public static string DescribeMarkdownContract() {
        return OfficeImoTestAssemblyContractDiagnostics.DescribeMarkdownContract();
    }

    public static string DescribeWordMarkdownContract() {
        return OfficeImoTestAssemblyContractDiagnostics.DescribeWordMarkdownContract();
    }
}

internal static class OfficeImoMarkdownInputNormalizationRuntimeContract {
    public static string NormalizeForTranscriptCleanup(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        return MarkdownInputNormalizer.Normalize(
            text,
            MarkdownInputNormalizationPresets.CreateIntelligenceXTranscript());
    }
}

internal static class OfficeImoTestAssemblyContractDiagnostics {
    public static string DescribeMarkdownRendererContract() {
        return Describe(
            typeof(MarkdownRenderer).Assembly,
            "OfficeIMO.MarkdownRenderer",
            "0.2.0",
            "explicit transcript presets + preprocessor pipeline");
    }

    public static string DescribeMarkdownContract() {
        return Describe(
            typeof(MarkdownInputNormalizer).Assembly,
            "OfficeIMO.Markdown",
            "0.6.0",
            "transcript normalization + streaming preview");
    }

    public static string DescribeWordMarkdownContract() {
        return Describe(
            typeof(MarkdownToWordOptions).Assembly,
            "OfficeIMO.Word.Markdown",
            "1.0.7",
            "transcript markdown-to-word conversion");
    }

    private static string Describe(System.Reflection.Assembly assembly, string componentName, string minimumVersion, string feature) {
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var path = string.IsNullOrWhiteSpace(assembly.Location) ? "(dynamic)" : System.IO.Path.GetFullPath(assembly.Location);
        return $"{componentName} expected>={minimumVersion} feature={feature} loaded={version} status=ok path={path}";
    }
}
