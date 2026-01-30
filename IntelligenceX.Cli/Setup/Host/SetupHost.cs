using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Host;

internal sealed class SetupHost {
    public Task<int> ApplyAsync(SetupPlan plan) {
        var args = SetupArgsBuilder.FromPlan(plan);
        return SetupRunner.RunAsync(args);
    }
}
