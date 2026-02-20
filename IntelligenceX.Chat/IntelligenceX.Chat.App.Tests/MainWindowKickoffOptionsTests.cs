using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Ensures onboarding kickoff requests use short, non-blocking defaults so user turns are not starved.
/// </summary>
public sealed class MainWindowKickoffOptionsTests {
    /// <summary>
    /// Verifies kickoff options force fast timeout + minimal execution settings regardless of base autonomy values.
    /// </summary>
    [Fact]
    public void BuildKickoffChatRequestOptions_ForcesShortNonBlockingDefaults() {
        var options = MainWindow.BuildKickoffChatRequestOptions(new ChatRequestOptions {
            MaxToolRounds = 24,
            ParallelTools = true,
            PlanExecuteReviewLoop = true,
            MaxReviewPasses = 2,
            TurnTimeoutSeconds = 180,
            ToolTimeoutSeconds = 60,
            ModelHeartbeatSeconds = 8
        });

        Assert.Equal(1, options.MaxToolRounds);
        Assert.False(options.ParallelTools);
        Assert.False(options.PlanExecuteReviewLoop);
        Assert.Equal(0, options.MaxReviewPasses);
        Assert.Equal(25, options.TurnTimeoutSeconds);
        Assert.Equal(20, options.ToolTimeoutSeconds);
        Assert.Equal(4, options.ModelHeartbeatSeconds);
    }
}
