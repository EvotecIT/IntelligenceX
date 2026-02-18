using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class SkillScriptParityTests {
    [Fact]
    public void EachSkillShellScript_HasPowerShellPeer() {
        var repoRoot = FindRepoRoot();
        var skillsRoot = Path.Combine(repoRoot, ".agents", "skills");
        Assert.True(Directory.Exists(skillsRoot), "Skills directory was not found: " + skillsRoot);

        var shellScripts = Directory
            .EnumerateFiles(skillsRoot, "*.sh", SearchOption.AllDirectories)
            .Where(path => path.Contains(Path.DirectorySeparatorChar + "scripts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(shellScripts);

        var missingPowerShellPeers = new List<string>();
        for (var i = 0; i < shellScripts.Length; i++) {
            var shellPath = shellScripts[i];
            var powerShellPath = Path.ChangeExtension(shellPath, ".ps1");
            if (!File.Exists(powerShellPath)) {
                missingPowerShellPeers.Add(Path.GetRelativePath(repoRoot, shellPath));
            }
        }

        Assert.True(
            missingPowerShellPeers.Count == 0,
            "Missing PowerShell script peers for: " + string.Join(", ", missingPowerShellPeers));
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "IntelligenceX.sln"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
