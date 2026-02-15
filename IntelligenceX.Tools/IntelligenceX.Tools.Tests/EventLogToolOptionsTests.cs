using System;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogToolOptionsTests {
    [Fact]
    public void Validate_Defaults_DoNotThrow() {
        var options = new EventLogToolOptions();
        options.Validate();
    }

    [Fact]
    public void Validate_WhenEvtxFindMaxDepthTooHigh_Throws() {
        var options = new EventLogToolOptions {
            EvtxFindMaxDepth = 33
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("EvtxFindMaxDepth", ex.ParamName);
    }

    [Fact]
    public void Validate_WhenEvtxFindMaxDirsScannedTooHigh_Throws() {
        var options = new EventLogToolOptions {
            EvtxFindMaxDirsScanned = 50_001
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("EvtxFindMaxDirsScanned", ex.ParamName);
    }

    [Fact]
    public void Validate_WhenEvtxFindMaxFilesScannedTooHigh_Throws() {
        var options = new EventLogToolOptions {
            EvtxFindMaxFilesScanned = 200_001
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("EvtxFindMaxFilesScanned", ex.ParamName);
    }
}

