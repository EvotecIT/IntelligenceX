using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;

namespace IntelligenceX.Copilot.Native;

/// <summary>Copilot authentication and model routing over the shared HTTP conversation engine.</summary>
internal sealed class CopilotNativeTransport : OpenAICompatibleHttpTransport {
    private readonly CopilotNativeOptions _copilot;
    private readonly CopilotNativeAuthentication _auth;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private IReadOnlyDictionary<string, bool>? _protocols;
    private DateTimeOffset _catalogExpires;

    internal CopilotNativeTransport(CopilotNativeOptions options) : this(Snapshot(options), CreateHttp(options)) { }

    private CopilotNativeTransport(CopilotNativeOptions options, HttpClient http)
        : base(new OpenAICompatibleHttpOptions { BaseUrl = options.BaseUrl, AuthMode = OpenAICompatibleHttpAuthMode.None,
            Streaming = options.Streaming, AllowAutoRedirect = false }, http, new Uri(options.BaseUrl.TrimEnd('/') + "/")) {
        _copilot = options;
        _auth = new CopilotNativeAuthentication(options, http);
    }

    public override OpenAITransportKind Kind => OpenAITransportKind.CopilotNative;

    protected override async Task PrepareRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
        request.Headers.UserAgent.ParseAdd("IntelligenceX/0.1.1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        request.Headers.TryAddWithoutValidation("X-Initiator", "user");
        request.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());
    }

    protected override async Task<bool> UseResponsesAsync(string model, CancellationToken cancellationToken) {
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_protocols is null || DateTimeOffset.UtcNow >= _catalogExpires) {
                var models = await base.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                UpdateCatalog(models);
            }
            if (!_protocols!.TryGetValue(model, out bool responses))
                throw new NotSupportedException("The selected Copilot model is unavailable or does not advertise a supported inference protocol. Refresh the model list and select an available model.");
            return responses;
        } finally { _catalogGate.Release(); }
    }

    public override async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        using var deadline = CreateDeadline(cancellationToken);
        try {
            await _catalogGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try {
                var models = await base.ListModelsAsync(deadline.Token).ConfigureAwait(false);
                UpdateCatalog(models);
                return models;
            } finally { _catalogGate.Release(); }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("The native Copilot model request exceeded its configured timeout.");
        }
    }

    private void UpdateCatalog(ModelListResult models) {
        var protocols = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var model in models.Models) {
            var endpoints = model.Raw.GetArray("supported_endpoints");
            bool? responses = null;
            if (endpoints is null) {
                if (model.Raw.GetObject("capabilities")?.GetString("type") != "embeddings") responses = false;
            } else if (endpoints.Any(value => value.AsString() == "/responses")) responses = true;
            else if (endpoints.Any(value => value.AsString() == "/chat/completions")) responses = false;
            if (!responses.HasValue) continue;
            if (!string.IsNullOrWhiteSpace(model.Id)) protocols[model.Id] = responses.Value;
            if (!string.IsNullOrWhiteSpace(model.Model)) protocols[model.Model] = responses.Value;
        }
        _protocols = protocols; _catalogExpires = DateTimeOffset.UtcNow.AddMinutes(5);
    }

    public override async Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options,
        string? currentDirectory, string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        options = options?.Clone() ?? new ChatOptions();
        if (options.ImageGeneration?.Enabled == true) throw new NotSupportedException("Native Copilot does not expose image generation.");
        if (!string.IsNullOrWhiteSpace(options.PreviousResponseId))
            throw new NotSupportedException("Native Copilot keeps conversation history locally; resume the client thread instead of supplying a previous response ID.");
        options.MaxResponseBytes ??= 16_777_216;
        using var deadline = CreateDeadline(cancellationToken);
        try {
            return await base.StartTurnAsync(threadId, input, options, currentDirectory, approvalPolicy, sandboxPolicy, deadline.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("The native Copilot request exceeded its configured timeout.");
        }
    }

    public override async Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        using var deadline = CreateDeadline(cancellationToken);
        try {
            return await _auth.GetAccountAsync(deadline.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("The native Copilot account request exceeded its configured timeout.");
        }
    }

    public override Task LogoutAsync(CancellationToken cancellationToken) => _auth.LogoutAsync(cancellationToken);

    internal CopilotNativeAuthentication Authentication => _auth;

    private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken) {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_copilot.RequestTimeout);
        return deadline;
    }

    private static CopilotNativeOptions Snapshot(CopilotNativeOptions options) {
        if (options is null) throw new ArgumentNullException(nameof(options));
        options.Validate(); return options.Snapshot();
    }

    private static HttpClient CreateHttp(CopilotNativeOptions options) => new(options.HttpMessageHandler
        ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
}
