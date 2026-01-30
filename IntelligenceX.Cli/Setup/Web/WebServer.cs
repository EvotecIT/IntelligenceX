using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed class WebServer : IDisposable {
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private Task? _loop;
    private readonly WebApi _api = new();

    public WebServer(string prefix) {
        _prefix = prefix.EndsWith("/") ? prefix : prefix + "/";
        _listener.Prefixes.Add(_prefix);
    }

    public void Start() {
        _listener.Start();
        _loop = Task.Run(ListenAsync);
    }

    public async Task WaitForShutdownAsync(CancellationToken cancellationToken) {
        if (_loop is null) {
            return;
        }
        using var registration = cancellationToken.Register(() => _listener.Close());
        await _loop.ConfigureAwait(false);
    }

    public void Dispose() {
        if (_listener.IsListening) {
            _listener.Stop();
        }
        _listener.Close();
    }

    private async Task ListenAsync() {
        while (_listener.IsListening) {
            HttpListenerContext context;
            try {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            } catch {
                break;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context) {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (path == "/") {
            path = "/index.html";
        }

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
            await _api.HandleAsync(context).ConfigureAwait(false);
            return;
        }

        var content = WebStaticAssets.TryGet(path, out var contentType);
        if (content is null) {
            context.Response.StatusCode = 404;
            await WriteTextAsync(context.Response, "Not found.").ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = content.Length;
        await context.Response.OutputStream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
        context.Response.OutputStream.Close();
    }

    private static Task WriteTextAsync(HttpListenerResponse response, string text) {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        return response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }
}
