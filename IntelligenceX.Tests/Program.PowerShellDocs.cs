namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static partial class Program {
    private static readonly Regex PowerShellFenceRegex = new(
        "```(?:powershell|pwsh)\\s*\\r?\\n(.*?)\\r?\\n```",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex IntelligenceXCmdletRegex = new(
        "\\b[A-Za-z]+-IntelligenceX[A-Za-z0-9]+\\b",
        RegexOptions.Compiled);

    private static readonly XNamespace CommandNs = "http://schemas.microsoft.com/maml/dev/command/2004/10";
    private static readonly XNamespace MamlNs = "http://schemas.microsoft.com/maml/2004/10";

    private static readonly string[] OpenAiApiDocFiles = new[] {
        Path.Combine("IntelligenceX", "Providers", "OpenAI", "IntelligenceXClient.cs"),
        Path.Combine("IntelligenceX", "Providers", "OpenAI", "AppServer", "AppServerClient.cs"),
        Path.Combine("IntelligenceX", "Providers", "OpenAI", "AppServer", "AppServerClient.AccountAndThreads.cs"),
        Path.Combine("IntelligenceX", "Providers", "OpenAI", "AppServer", "AppServerClient.ConfigAndRuntime.cs")
    };

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

    private static void TestPowerShellCmdletSourceXmlDocsAreRich() {
        var workspace = ResolveWorkspaceRoot();
        var cmdletDirectory = Path.Combine(workspace, "IntelligenceX.PowerShell");
        AssertEqual(true, Directory.Exists(cmdletDirectory), "powershell cmdlet source directory exists");

        var cmdletFiles = Directory.GetFiles(cmdletDirectory, "Cmdlet*.cs", SearchOption.TopDirectoryOnly);
        AssertEqual(true, cmdletFiles.Length > 0, "powershell cmdlet files discovered");

        foreach (var file in cmdletFiles) {
            var text = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            AssertEqual(true, text.Contains("<para type=\"synopsis\">", StringComparison.Ordinal),
                $"cmdlet synopsis exists in {fileName}");
            AssertEqual(true, text.Contains("<para type=\"description\">", StringComparison.Ordinal),
                $"cmdlet description exists in {fileName}");

            var examples = Regex.Matches(text, "<example>", RegexOptions.Compiled).Count;
            AssertEqual(true, examples >= 3, $"cmdlet examples >= 3 in {fileName}");

            ValidateCmdletParameterXmlDocs(fileName, text);
        }
    }

    private static void TestPowerShellHelpXmlCoversAllCmdletsWithRichDocs() {
        var workspace = ResolveWorkspaceRoot();
        var exportedCmdlets = LoadExportedCmdletSet(workspace);
        var helpPath = Path.Combine(workspace, "Website", "data", "apidocs", "powershell", "IntelligenceX.PowerShell.dll-Help.xml");
        AssertEqual(true, File.Exists(helpPath), "powershell help xml exists");

        var document = XDocument.Load(helpPath);
        var commands = document.Root?.Elements(CommandNs + "command").ToArray() ?? Array.Empty<XElement>();
        AssertEqual(true, commands.Length > 0, "powershell help xml commands discovered");

        var commandByName = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands) {
            var name = command.Element(CommandNs + "details")?.Element(CommandNs + "name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }
            commandByName[name] = command;
        }

        foreach (var cmdlet in exportedCmdlets.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)) {
            AssertEqual(true, commandByName.TryGetValue(cmdlet, out var command),
                $"help xml entry exists for {cmdlet}");

            var detailParas = command!
                .Element(CommandNs + "details")?
                .Element(MamlNs + "description")?
                .Elements(MamlNs + "para")
                .Where(static p => !string.IsNullOrWhiteSpace(p.Value))
                .ToArray() ?? Array.Empty<XElement>();
            AssertEqual(true, detailParas.Length >= 2, $"help xml detail description has at least 2 paragraphs for {cmdlet}");

            var examples = command
                .Element(CommandNs + "examples")?
                .Elements(CommandNs + "example")
                .ToArray() ?? Array.Empty<XElement>();
            AssertEqual(true, examples.Length >= 3, $"help xml has at least 3 examples for {cmdlet}");
            foreach (var example in examples) {
                var code = example.Element("code")?.Value
                    ?? example.Element(XName.Get("code", "http://schemas.microsoft.com/maml/dev/2004/10"))?.Value
                    ?? string.Empty;
                AssertEqual(true, !string.IsNullOrWhiteSpace(code), $"help xml example code is non-empty for {cmdlet}");
            }

            var parameters = command
                .Element(CommandNs + "parameters")?
                .Elements(CommandNs + "parameter")
                .ToArray() ?? Array.Empty<XElement>();
            foreach (var parameter in parameters) {
                var parameterName = parameter.Element(MamlNs + "name")?.Value ?? "<unknown>";
                var paraCount = parameter
                    .Element(MamlNs + "description")?
                    .Elements(MamlNs + "para")
                    .Count(static p => !string.IsNullOrWhiteSpace(p.Value)) ?? 0;
                AssertEqual(true, paraCount > 0,
                    $"help xml parameter description exists for {cmdlet}:{parameterName}");
            }
        }
    }

    private static void TestOpenAiClientCSharpXmlDocsAreComplete() {
        var workspace = ResolveWorkspaceRoot();
        foreach (var relativePath in OpenAiApiDocFiles) {
            var filePath = Path.Combine(workspace, relativePath);
            AssertEqual(true, File.Exists(filePath), $"csharp api doc file exists: {relativePath}");

            var lines = File.ReadAllLines(filePath);
            for (var i = 0; i < lines.Length; i++) {
                if (!Regex.IsMatch(lines[i], "^\\s{4}public\\s+", RegexOptions.Compiled)) {
                    continue;
                }
                if (Regex.IsMatch(lines[i], "^\\s{4}public\\s+(?:event|sealed\\s+class|interface|enum)\\s+", RegexOptions.Compiled)) {
                    continue;
                }
                if (!lines[i].Contains('(')) {
                    continue;
                }

                var signature = lines[i].Trim();
                var j = i;
                while (!Regex.IsMatch(signature, "\\)\\s*(\\{|=>)\\s*$", RegexOptions.Compiled) && j + 1 < lines.Length) {
                    j++;
                    signature = $"{signature} {lines[j].Trim()}";
                }

                var methodMatch = Regex.Match(
                    signature,
                    "^public\\s+(?:static\\s+)?(?:async\\s+)?(?<return>[A-Za-z0-9_<>\\[\\],?.]+)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\((?<params>.*)\\)\\s*(?:\\{|=>)\\s*$",
                    RegexOptions.Compiled);
                if (!methodMatch.Success) {
                    continue;
                }

                var returnType = methodMatch.Groups["return"].Value;
                var methodName = methodMatch.Groups["name"].Value;
                var parameters = methodMatch.Groups["params"].Value;
                var docBlock = ReadXmlDocBlock(lines, i);

                AssertEqual(true, !string.IsNullOrWhiteSpace(docBlock),
                    $"xml doc block exists for {relativePath}:{methodName}");
                AssertEqual(true, docBlock.Contains("<summary>", StringComparison.Ordinal) &&
                    docBlock.Contains("</summary>", StringComparison.Ordinal),
                    $"xml summary exists for {relativePath}:{methodName}");

                foreach (var parameterName in ExtractParameterNames(parameters)) {
                    var token = $"<param name=\"{parameterName}\">";
                    AssertEqual(true, docBlock.Contains(token, StringComparison.Ordinal),
                        $"xml param doc exists for {relativePath}:{methodName}:{parameterName}");
                }

                if (!string.Equals(returnType, "void", StringComparison.Ordinal)) {
                    AssertEqual(true, docBlock.Contains("<returns>", StringComparison.Ordinal),
                        $"xml returns doc exists for {relativePath}:{methodName}");
                }
            }
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

    private static void ValidateCmdletParameterXmlDocs(string fileName, string text) {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            if (!Regex.IsMatch(lines[i], "^\\s*public\\s+[^\\(]+\\s+(?<name>\\w+)\\s*\\{", RegexOptions.Compiled)) {
                continue;
            }

            var propertyMatch = Regex.Match(lines[i], "^\\s*public\\s+[^\\(]+\\s+(?<name>\\w+)\\s*\\{", RegexOptions.Compiled);
            var propertyName = propertyMatch.Groups["name"].Value;

            var j = i - 1;
            while (j >= 0 && string.IsNullOrWhiteSpace(lines[j])) {
                j--;
            }
            if (j < 0 || !Regex.IsMatch(lines[j], "^\\s*\\]", RegexOptions.Compiled)) {
                continue;
            }

            var attrStart = j;
            while (attrStart >= 0) {
                if (Regex.IsMatch(lines[attrStart], "^\\s*\\[[^\\]]+\\]\\s*$", RegexOptions.Compiled) ||
                    string.IsNullOrWhiteSpace(lines[attrStart])) {
                    attrStart--;
                    continue;
                }
                break;
            }
            attrStart++;
            if (attrStart < 0 || attrStart >= lines.Length) {
                continue;
            }

            var hasParameterAttribute = false;
            for (var k = attrStart; k < i; k++) {
                if (Regex.IsMatch(lines[k], "^\\s*\\[Parameter\\(", RegexOptions.Compiled)) {
                    hasParameterAttribute = true;
                    break;
                }
            }
            if (!hasParameterAttribute) {
                continue;
            }

            var docStart = attrStart - 1;
            while (docStart >= 0 && string.IsNullOrWhiteSpace(lines[docStart])) {
                docStart--;
            }
            if (docStart < 0 || !Regex.IsMatch(lines[docStart], "^\\s*///", RegexOptions.Compiled)) {
                throw new InvalidOperationException($"Missing XML doc block for parameter {propertyName} in {fileName}.");
            }
            while (docStart >= 0 && Regex.IsMatch(lines[docStart], "^\\s*///", RegexOptions.Compiled)) {
                docStart--;
            }
            docStart++;

            var block = string.Join("\n", lines.Skip(docStart).Take(attrStart - docStart));
            if (!block.Contains("<summary>", StringComparison.Ordinal) ||
                !block.Contains("</summary>", StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Missing XML summary for parameter {propertyName} in {fileName}.");
            }
            if (!block.Contains("<para type=\"description\">", StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Missing XML description paragraph for parameter {propertyName} in {fileName}.");
            }
        }
    }

    private static string ReadXmlDocBlock(IReadOnlyList<string> lines, int declarationLine) {
        var i = declarationLine - 1;
        while (i >= 0 && string.IsNullOrWhiteSpace(lines[i])) {
            i--;
        }
        if (i < 0 || !Regex.IsMatch(lines[i], "^\\s*///", RegexOptions.Compiled)) {
            return string.Empty;
        }

        var docLines = new List<string>();
        while (i >= 0 && Regex.IsMatch(lines[i], "^\\s*///", RegexOptions.Compiled)) {
            docLines.Add(lines[i].Trim());
            i--;
        }
        docLines.Reverse();
        return string.Join("\n", docLines);
    }

    private static IReadOnlyList<string> ExtractParameterNames(string rawParameters) {
        if (string.IsNullOrWhiteSpace(rawParameters)) {
            return Array.Empty<string>();
        }

        var chunks = new List<string>();
        var current = new List<char>();
        var angleDepth = 0;
        var parenDepth = 0;
        for (var i = 0; i < rawParameters.Length; i++) {
            var ch = rawParameters[i];
            switch (ch) {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0) {
                        angleDepth--;
                    }
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) {
                        parenDepth--;
                    }
                    break;
                case ',':
                    if (angleDepth == 0 && parenDepth == 0) {
                        chunks.Add(new string(current.ToArray()));
                        current.Clear();
                        continue;
                    }
                    break;
            }
            current.Add(ch);
        }
        if (current.Count > 0) {
            chunks.Add(new string(current.ToArray()));
        }

        var parameterNames = new List<string>();
        foreach (var chunk in chunks) {
            var part = chunk.Trim();
            if (part.Length == 0) {
                continue;
            }

            var equalsIndex = part.IndexOf('=');
            if (equalsIndex >= 0) {
                part = part.Substring(0, equalsIndex).Trim();
            }

            part = Regex.Replace(part, "^\\s*(?:this|params|ref|out|in)\\s+", string.Empty, RegexOptions.Compiled);
            var tokens = Regex.Split(part, "\\s+")
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .ToArray();
            if (tokens.Length == 0) {
                continue;
            }

            var name = tokens[^1]
                .Trim()
                .TrimEnd('?');
            name = Regex.Replace(name, "[^A-Za-z0-9_]", string.Empty, RegexOptions.Compiled);
            if (name.Length == 0) {
                continue;
            }
            parameterNames.Add(name);
        }

        return parameterNames;
    }
}
#endif
