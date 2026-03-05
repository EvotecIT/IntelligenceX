using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Profiles;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ServiceProfilePresetsTests {
    [Fact]
    public void GetBuiltInPresetNames_ReturnsReadOnlyNonArrayView() {
        var names = ServiceProfilePresets.GetBuiltInPresetNames();

        Assert.Equal(new[] { ServiceProfilePresets.PluginOnly }, names);
        Assert.False(names is string[]);
        var list = Assert.IsAssignableFrom<IList<string>>(names);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = "mutated");
    }
}
