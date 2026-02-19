using System;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolingBootstrapTests {
    [Fact]
    public void RebuildToolingFromOptions_RefreshesPackAvailabilitySnapshot() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var packAvailabilityField = typeof(ChatServiceSession).GetField("_packAvailability", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(packAvailabilityField);

        var options = new ServiceOptions {
            EnableOfficeImoPack = true
        };
        var session = new ChatServiceSession(options, Stream.Null);

        var initialAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField!.GetValue(session));

        options.EnableOfficeImoPack = false;
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var rebuiltAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField.GetValue(session));
        Assert.NotSame(initialAvailability, rebuiltAvailability);

        var officeImo = Assert.Single(rebuiltAvailability, static item =>
            string.Equals(item.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.False(officeImo.Enabled);
    }
}
