using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;

namespace IntelligenceX.Treatment;

/// <summary>Inline text treatment through an authenticated Copilot CLI, with a fresh restricted process for each call.</summary>
/// <remarks>Uses prompted JSON only. Tools, ambient configuration, skills, memory and session persistence are disabled.
/// The provider does not claim that hosted inference retains no data. The caller owns authorization to send source content.</remarks>
public sealed class CopilotTreatmentProvider : ITreatmentProvider {
    private readonly string? _cliPath;
    private readonly string? _githubToken;

    /// <summary>Creates a restricted provider using an installed CLI and an optional GitHub token passed only in its environment.</summary>
    public CopilotTreatmentProvider(string? cliPath = null, string? githubToken = null) {
        _cliPath = cliPath;
        _githubToken = githubToken;
    }

    /// <inheritdoc />
    public async Task<TreatmentResult> RunAsync(TreatmentRequest request, CancellationToken cancellationToken = default) {
        TreatmentPromptBuilder.Validate(request);
#if !NET5_0_OR_GREATER
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            throw new PlatformNotSupportedException("Restricted Copilot treatment requires a modern .NET runtime on non-Windows platforms.");
#endif
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        CancellationToken token = deadline.Token;
        token.ThrowIfCancellationRequested();
        if (request.EnforceOutputSchema || request.AllowNetwork || request.Workspace is not null || request.WorkingDirectory is not null
            || request.ImageGeneration?.Enabled == true || !request.NewThread || !request.Ephemeral
            || request.Inputs.Any(input => input.ImageBytes is not null || input.Path is not null || input.Uri is not null))
            throw new NotSupportedException("Copilot treatment requires ephemeral inline text, prompted output, and no tool or file access.");
        if (string.IsNullOrWhiteSpace(request.Model)) throw new ArgumentException("Copilot treatment requires an explicit model.", nameof(request));
        long maximum = request.MaxResponseBytes ?? 4_194_304;
        if (maximum < 1 || maximum > 268_435_456) throw new ArgumentOutOfRangeException(nameof(request.MaxResponseBytes));
        // Build before opening a connection: this owner never reads input files or resolves input URLs.
        string prompt = TreatmentPromptBuilder.Build(request);
        string runtime = Path.Combine(Path.GetTempPath(), "intelligencex-treatment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);
        try {
            var options = new CopilotClientOptions {
                CliPath = _cliPath ?? "copilot", AutoInstallCli = false, WorkingDirectory = runtime,
                LogLevel = "none", MaxReceivedBytes = maximum, ConnectRetryCount = 0
            };
            options.Environment["COPILOT_HOME"] = runtime;
            if (!string.IsNullOrWhiteSpace(_githubToken)) options.Environment["COPILOT_GITHUB_TOKEN"] = _githubToken!;
            options.CliArgs.AddRange(new[] { "--no-auto-update", "--disable-builtin-mcps", "--no-custom-instructions",
                "--no-remote-export", "--no-ask-user" });
            using var client = await CopilotClient.StartAsync(options, token).ConfigureAwait(false);
            var auth = await client.GetAuthStatusAsync(token).ConfigureAwait(false);
            if (!auth.IsAuthenticated) throw new InvalidOperationException("Copilot treatment authentication is unavailable.");
            using var session = await client.CreateSessionAsync(new CopilotSessionOptions {
                Model = request.Model, Restricted = true, Streaming = false, SystemMessage = request.Instructions
            }, token).ConfigureAwait(false);
            string? response = await session.SendAndWaitAsync(new CopilotMessageOptions {
                Prompt = prompt, MaxResponseCharacters = (int)Math.Min(maximum, 16_000_000)
            }, TimeSpan.FromMinutes(10), token).ConfigureAwait(false);
            // Closing the dedicated CLI below also terminates outstanding work on cancellation or invalid output.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.DeleteSessionAsync(session.SessionId, cleanup.Token).ConfigureAwait(false);
            return new TreatmentResult(request.Id ?? Guid.NewGuid().ToString("N"), "completed", response, null, null, null, request.Metadata);
        } finally {
            // This exact random directory was created above; never recurse into a caller-selected directory.
            Directory.Delete(runtime, recursive: true);
        }
    }
}
