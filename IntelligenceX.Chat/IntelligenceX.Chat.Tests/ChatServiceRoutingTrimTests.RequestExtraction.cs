using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ExtractPrimaryUserRequest_StripsCodeFencesAndInlineCode() {
        var input = """
            Please check this:
            ```powershell
            Get-EventLog -LogName System
            ```
            and also `C:\Temp\ADO-System.evtx`
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.DoesNotContain("Get-EventLog", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C:\\Temp\\ADO-System.evtx", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Please check this:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and also", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_DoesNotDropIntentWhenFenceUnclosedAfterIntent() {
        var input = """
            Please run the checks first.
            ```powershell
            Get-EventLog -LogName System
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Please run the checks first.", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-EventLog", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_PreservesTrailingPlainTextAfterUnclosedFence() {
        var input = """
            Please run the checks first.
            ```powershell
            Get-EventLog -LogName System
            then run now
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Please run the checks first.", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-EventLog", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("then run now", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_ReturnsEmptyWhenFenceUnclosedAtStart() {
        var input = """
            ```powershell
            Get-EventLog -LogName System
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_DoesNotConcatenateTokensWhenBackticksAreOdd() {
        var input = "please `run now";

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("run now", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_StopsAtNextStructuredSectionWhenUserRequestAppearsFirst() {
        var input = """
            User request:
            Check replication health and show a table.

            [Session profile context]
            - Assistant persona: sharp operator
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Equal("Check replication health and show a table.", text);
    }

    [Fact]
    public void ExtractIntentUserText_StopsAtNextStructuredSectionWhenUserRequestAppearsFirst() {
        var input = """
            User request:
            Pokaz tabele i diagram replikacji.

            [Persistent memory]
            - Prefers concise answers.
            """;

        var result = ExtractIntentUserTextMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Equal("Pokaz tabele i diagram replikacji.", text);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_DoesNotTruncateStandaloneBracketedContentInsideUserRequestBody() {
        var input = """
            User request:
            Show the replication matrix exactly like this:
            [Replication topology]
            AD0 -> AD1

            [Session profile context]
            - Assistant persona: sharp operator
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Show the replication matrix exactly like this:", text, StringComparison.Ordinal);
        Assert.Contains("[Replication topology]", text, StringComparison.Ordinal);
        Assert.Contains("AD0 -> AD1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Session profile context]", text, StringComparison.Ordinal);
    }

}
