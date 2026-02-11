namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestGitHubRepoClientInjectedCtorNullGuard() {
        AssertThrows<ArgumentNullException>(() => {
            var _ = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient((System.Net.Http.HttpClient)null!, "token");
        }, "repo client injected ctor null guard");
    }

    private static void TestGitHubRepoClientDisposeOwnershipSemantics() {
        var nonOwningHandler = new TrackingHttpMessageHandler((_, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        using (var sharedHttp = new System.Net.Http.HttpClient(nonOwningHandler) {
                   BaseAddress = new Uri("https://api.github.com")
               }) {
            using (var nonOwningClient = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(
                       sharedHttp,
                       token: "token-a",
                       ownsHttpClient: false)) {
                var status = nonOwningClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
                AssertEqual("missing", status.Status, "repo client non-owning setup status");
            }

            AssertEqual(false, nonOwningHandler.IsDisposed, "repo client non-owning handler still active");
            var probe = sharedHttp.GetAsync("/repos/owner/repo/actions/secrets/INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual(System.Net.HttpStatusCode.NotFound, probe.StatusCode, "repo client non-owning shared http still usable");
        }

        var owningHandler = new TrackingHttpMessageHandler((_, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        var ownedHttp = new System.Net.Http.HttpClient(owningHandler) {
            BaseAddress = new Uri("https://api.github.com")
        };
        using (var owningClient = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(
                   ownedHttp,
                   token: "token-b",
                   ownsHttpClient: true)) {
            var status = owningClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual("missing", status.Status, "repo client owning setup status");
        }

        AssertEqual(true, owningHandler.IsDisposed, "repo client owning handler disposed");
        AssertThrows<ObjectDisposedException>(() =>
            ownedHttp.GetAsync("/repos/owner/repo/actions/secrets/INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult(),
            "repo client owning shared http disposed");
    }

    private static void TestGitHubRepoClientRejectsUseAfterDispose() {
        using var client = CreateGitHubRepoClientForTests((_, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        client.Dispose();

        AssertThrows<ObjectDisposedException>(() =>
            client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult(),
            "repo client use after dispose secret");
        AssertThrows<ObjectDisposedException>(() =>
            client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult(),
            "repo client use after dispose file");
    }

    private sealed class TrackingHttpMessageHandler : System.Net.Http.HttpMessageHandler {
        private readonly Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> _sendAsync;
        public bool IsDisposed { get; private set; }

        public TrackingHttpMessageHandler(
            Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> sendAsync) {
            _sendAsync = sendAsync;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return _sendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing) {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
#endif
}
