# IntelligenceX

`IntelligenceX` is the reusable .NET SDK behind IntelligenceX applications and integrations. It includes:

- Codex app-server and provider-neutral AI clients
- GitHub notification and repository monitoring APIs
- AI usage, model, cost, and provider-limit telemetry
- tool identity, domain presentation, selection metadata, and reporting models

The package does not install the IntelligenceX desktop applications and does not bring in SQLite or Windows UI dependencies.
It provides assets for .NET Standard 2.0, .NET Framework 4.7.2, .NET 8, and .NET 10.

```powershell
dotnet add package IntelligenceX
```

## Native Copilot

Copilot uses the same `IntelligenceXClient` as ChatGPT, with direct HTTPS and no Copilot CLI, native runtime, or extra provider package:

```csharp
using IntelligenceX.OpenAI;

using var client = await IntelligenceXClient.ConnectAsync(new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.CopilotNative,
    DefaultModel = "gpt-5.4"
});
var models = await client.ListModelsAsync();
var turn = await client.ChatAsync("Summarize the supplied text.");
Console.WriteLine(EasyChatResult.FromTurn(turn).Text);
```

Set `COPILOT_GITHUB_TOKEN` to a GitHub credential authorized for Copilot, or supply `CopilotOptions.GitHubToken`, `TokenProvider`, or an explicit `AuthStore`. Select a model returned by the account's model catalog. The SDK routes Responses and Chat Completions models through the shared streaming, tool-call, image, schema, usage, and local-history contracts.

For interactive applications, configure the host's registered `GitHubClientId` and call `LoginCopilotAsync` with a callback that displays the user code and verification URL. Storage is opt-in; expiring stored credentials renew through that registered app. Authentication and inference entitlement are separate checks.

The transport implements the Copilot client protocol, which is not a versioned public GitHub REST inference API. Model availability and protocol support can change. It rejects unsupported requests and does not install or fall back to a CLI. See the [provider guide and migration](https://github.com/EvotecIT/IntelligenceX/blob/master/Docs/library/providers.md#copilot) for options and replacements for the retired Copilot clients.
## GitHub notification example

```csharp
using IntelligenceX.Telemetry.GitHub;

using var inbox = new GitHubNotificationService(githubToken);
GitHubNotificationSnapshot snapshot = await inbox.FetchAsync(
    new GitHubNotificationQuery { Limit = 20 });

foreach (GitHubNotificationThread thread in snapshot.Threads) {
    Console.WriteLine($"{thread.RepositoryNameWithOwner}: {thread.Title}");
}
```

For persistent SQLite-backed telemetry and GitHub monitoring stores on .NET 8 or .NET 10, add [`IntelligenceX.Storage.SQLite`](https://www.nuget.org/packages/IntelligenceX.Storage.SQLite).

Documentation and source are available in the [IntelligenceX repository](https://github.com/EvotecIT/IntelligenceX).

## Bounded image and structured-output treatment

`TreatmentRequest` can carry text/JSON and inline image bytes without giving the provider a local file path. For an already connected `IntelligenceXClient` and a caller-authorized PNG payload:

```csharp
using IntelligenceX.Json;
using IntelligenceX.Treatment;

static Task<TreatmentResult> ReadImageAsync(
    IntelligenceX.OpenAI.IntelligenceXClient client, byte[] png,
    CancellationToken cancellationToken = default) {
    return new OpenAIChatTreatmentProvider(client).RunAsync(new TreatmentRequest {
        Prompt = "Read the reference printed in this image. Use null when unreadable.",
        Inputs = new[] {
            new TreatmentInputArtifact { Id = "source", MediaType = "image/png", ImageBytes = png }
        },
        OutputSchema = new TreatmentOutputSchema {
            JsonSchema = JsonLite.Parse("""
                {"type":"object","properties":{"reference":{"type":["string","null"]}},"required":["reference"],"additionalProperties":false}
                """).AsObject(),
            Strict = true
        },
        EnforceOutputSchema = true,
        MaxInlineImageBytes = 4 * 1024 * 1024,
        MaxResponseBytes = 256 * 1024,
        Ephemeral = true,
        InlineLocalInputFiles = false,
        ImageGeneration = new TreatmentImageOptions { Enabled = false }
    }, cancellationToken);
}
```

The Native and OpenAI-compatible HTTP transports map enforced schemas to their respective response formats. Other transports must explicitly support the contract; unsupported ephemeral execution is rejected. `EnforceOutputSchema = false` preserves prompted JSON behavior. Always validate the returned structure and source support in the consumer: generation constraints do not establish factual correctness.

`MaxResponseBytes` bounds the response wire stream, including SSE framing, before full buffering. `JsonLite.Parse` rejects nesting beyond 128 containers across all target frameworks, before recursive parsing of provider envelopes or candidate content. `Ephemeral` starts a fresh conversation and removes local SDK thread state after the request settles; it does not establish remote retention policy. `MaxInlineImageBytes` applies to aggregate encoded image bytes, while the caller remains responsible for decoding and pixel limits.

For sensitive native requests, configure `OpenAINativeOptions.AllowSensitiveDiagnostics = false`, disable telemetry as appropriate, and use `EnableModelFallback = false` when switching models would violate the chosen profile. Compatible HTTP clients can set `AllowAutoRedirect = false` and `UseProxy = false` for an explicitly local loopback route. These options preserve existing defaults for other consumers. `AllowNetwork` controls provider-side tool policy, not whether hosted inference sends supplied content over the network.
