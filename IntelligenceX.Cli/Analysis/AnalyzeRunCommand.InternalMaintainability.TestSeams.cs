using System.Collections.Generic;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    internal static IReadOnlyList<string> GetDefaultIncludedSourceExtensionsForTests() {
        return DefaultIncludedSourceExtensions;
    }

    internal static IReadOnlyList<string> GetDuplicationCanonicalLanguagesForTests() {
        return DuplicationCanonicalLanguages;
    }

    internal static IReadOnlyList<string> GetDuplicationAliasLanguagesForTests() {
        return DuplicationAliasLanguages;
    }
}
