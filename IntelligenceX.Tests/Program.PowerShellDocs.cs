namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
using System.Text.RegularExpressions;

internal static partial class Program {
    private static readonly Regex PowerShellFenceRegex = new(
        "```(?:powershell|pwsh)\\s*\\r?\\n(.*?)\\r?\\n```",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex IntelligenceXCmdletRegex = new(
        "\\b[A-Za-z]+-IntelligenceX[A-Za-z0-9]+\\b",
        RegexOptions.Compiled);

    private static void TestPowerShellDocsSnippetsUseExportedCmdlets() {
        var workspace = ResolveWorkspaceRoot();
        var docsDirectory = Path.Combine(workspace, "Docs", "powershell");
        AssertEqual(true, Directory.Exists(docsDirectory), "powershell docs directory exists");

        var exportedCmdlets = LoadExportedCmdletSet(workspace);
        var markdownFiles = Directory.GetFiles(docsDirectory, "*.md", SearchOption.TopDirectoryOnly);
        AssertEqual(true, markdownFiles.Length > 0, "powershell docs markdown files discovered");

        var snippetsChecked = 0;
        foreach (var file in markdownFiles) {
            var markdown = File.ReadAllText(file);
            var matches = PowerShellFenceRegex.Matches(markdown);
            foreach (Match match in matches) {
                var code = match.Groups[1].Value;
                ValidateIntelligenceXCmdletReferences(code, file, exportedCmdlets);
                snippetsChecked++;
            }
        }

        AssertEqual(true, snippetsChecked > 0, "powershell docs snippets discovered");
    }

    private static void TestPowerShellExampleScriptsUseExportedCmdlets() {
        var workspace = ResolveWorkspaceRoot();
        var examplesDirectory = Path.Combine(workspace, "Website", "data", "apidocs", "powershell", "examples");
        AssertEqual(true, Directory.Exists(examplesDirectory), "powershell examples directory exists");

        var exportedCmdlets = LoadExportedCmdletSet(workspace);
        var scripts = Directory.GetFiles(examplesDirectory, "*.ps1", SearchOption.TopDirectoryOnly);
        AssertEqual(true, scripts.Length > 0, "powershell example scripts discovered");

        foreach (var script in scripts) {
            var content = File.ReadAllText(script);
            ValidateIntelligenceXCmdletReferences(content, script, exportedCmdlets);
        }
    }

    private static HashSet<string> LoadExportedCmdletSet(string workspace) {
        var manifestPath = Path.Combine(workspace, "Module", "IntelligenceX.psd1");
        AssertEqual(true, File.Exists(manifestPath), "powershell module manifest exists");

        var manifest = File.ReadAllText(manifestPath);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = IntelligenceXCmdletRegex.Matches(manifest);
        foreach (Match match in matches) {
            set.Add(match.Value);
        }

        AssertEqual(true, set.Count > 0, "exported cmdlets discovered from module manifest");
        return set;
    }

    private static void ValidateIntelligenceXCmdletReferences(string content, string source,
        IReadOnlySet<string> exportedCmdlets) {
        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = IntelligenceXCmdletRegex.Matches(content);
        foreach (Match match in matches) {
            var cmdlet = match.Value;
            if (!seen.Add(cmdlet)) {
                continue;
            }
            if (!exportedCmdlets.Contains(cmdlet)) {
                unknown.Add(cmdlet);
            }
        }

        if (unknown.Count > 0) {
            throw new InvalidOperationException(
                $"Unknown IntelligenceX cmdlet reference(s) in '{source}': {string.Join(", ", unknown)}");
        }
    }
}
#endif

