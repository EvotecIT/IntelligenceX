using System.Collections.Generic;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    internal static IReadOnlyList<string> GetDefaultIncludedSourceExtensionsForTests() {
        return (string[])DefaultIncludedSourceExtensions.Clone();
    }

    internal static IReadOnlyList<string> GetDuplicationCanonicalLanguagesForTests() {
        return (string[])DuplicationCanonicalLanguages.Clone();
    }

    internal static IReadOnlyList<string> GetDuplicationAliasLanguagesForTests() {
        return (string[])DuplicationAliasLanguages.Clone();
    }
}
