using System.Collections.Generic;
using IntelligenceX.Chat.App.Native;
using IntelligenceX.Chat.App.Native.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests native table artifact title inference from projected Markdown table schemas.
/// </summary>
public sealed class NativeTableArtifactTitleResolverTests {
    /// <summary>
    /// Ensures known sample table schemas resolve to operator-facing artifact titles.
    /// </summary>
    [Theory]
    [InlineData("Account|Risk|Last Sign-in", "Privileged Accounts")]
    [InlineData("Workload|Finding|Severity|Owner", "Tenant Findings")]
    [InlineData("Domain|SPF|DKIM|DMARC|Risk", "Mail Authentication")]
    [InlineData("Time|Actor|Action|Evidence", "Incident Timeline")]
    [InlineData("Object|Kind|Owner|Finding", "Directory Objects")]
    [InlineData("Group|Removed Members|Remaining Risk", "Group Cleanup")]
    [InlineData("Account|Exception|Expires|Risk", "MFA Exceptions")]
    public void Resolve_KnownSchemas_ReturnsSpecificTitle(string headerSpec, string expectedTitle) {
        var table = new NativeTranscriptTable(headerSpec.Split('|'), new IReadOnlyList<string>[] { });

        var title = NativeTableArtifactTitleResolver.Resolve(table);

        Assert.Equal(expectedTitle, title);
    }

    /// <summary>
    /// Ensures unknown table schemas fall back to a neutral artifact title.
    /// </summary>
    [Fact]
    public void Resolve_UnknownSchema_ReturnsNeutralTitle() {
        var table = new NativeTranscriptTable(new[] { "Name", "Value" }, new IReadOnlyList<string>[] { });

        var title = NativeTableArtifactTitleResolver.Resolve(table);

        Assert.Equal("Table Evidence", title);
    }
}
