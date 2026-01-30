using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Host;

internal sealed class SetupHost {
    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    public async Task<int> ApplyAsync(SetupPlan plan) {
        var args = SetupArgsBuilder.FromPlan(plan);
        await ConsoleLock.WaitAsync().ConfigureAwait(false);
        try {
            return await SetupRunner.RunAsync(args).ConfigureAwait(false);
        } finally {
            ConsoleLock.Release();
        }
    }

    public async Task<SetupHostResult> ApplyWithOutputAsync(SetupPlan plan) {
        var args = SetupArgsBuilder.FromPlan(plan);
        await ConsoleLock.WaitAsync().ConfigureAwait(false);
        try {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            try {
                Console.SetOut(output);
                Console.SetError(error);
                var code = await SetupRunner.RunAsync(args).ConfigureAwait(false);
                return new SetupHostResult {
                    ExitCode = code,
                    Output = output.ToString(),
                    Error = error.ToString()
                };
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        } finally {
            ConsoleLock.Release();
        }
    }
}

internal sealed class SetupHostResult {
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
