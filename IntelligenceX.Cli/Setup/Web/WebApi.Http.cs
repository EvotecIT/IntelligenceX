using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private const long MaxRequestBodyBytes = 1024 * 1024; // 1 MiB

    private sealed class RequestBodyTooLargeException : Exception {
    }

    private async Task<string> ReadBodyAsync(System.Net.HttpListenerContext context) {
        var contentLength = context.Request.ContentLength64;
        if (contentLength > MaxRequestBodyBytes) {
            throw new RequestBodyTooLargeException();
        }

        var encoding = context.Request.ContentEncoding ?? Encoding.UTF8;
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = await context.Request.InputStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) {
            total += read;
            if (total > MaxRequestBodyBytes) {
                throw new RequestBodyTooLargeException();
            }
            ms.Write(buffer, 0, read);
        }
        if (ms.TryGetBuffer(out var segment) && segment.Array is not null) {
            return encoding.GetString(segment.Array, segment.Offset, segment.Count);
        }
        return encoding.GetString(ms.ToArray());
    }

    private async Task WriteJsonAsync(System.Net.HttpListenerContext context, object payload) {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        try {
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
        } finally {
            try {
                context.Response.OutputStream.Close();
            } catch {
                // Best effort close.
            }
            try {
                context.Response.Close();
            } catch {
                // Best effort close.
            }
        }
    }

    private async Task<string?> ReadJsonBodyAsync(System.Net.HttpListenerContext context) {
        if (!await RequirePostJsonAsync(context).ConfigureAwait(false)) {
            return null;
        }
        string body;
        try {
            body = await ReadBodyAsync(context).ConfigureAwait(false);
        } catch (RequestBodyTooLargeException) {
            context.Response.StatusCode = 413;
            await WriteJsonAsync(context, new { error = "Request body too large." }).ConfigureAwait(false);
            return null;
        }
        if (string.IsNullOrWhiteSpace(body)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Request body required." }).ConfigureAwait(false);
            return null;
        }
        return body;
    }

    private async Task<bool> RequirePostJsonAsync(System.Net.HttpListenerContext context) {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return false;
        }
        if (!IsJsonContentType(context.Request.ContentType)) {
            context.Response.StatusCode = 415;
            await WriteJsonAsync(context, new { error = "Content-Type must be application/json." }).ConfigureAwait(false);
            return false;
        }
        if (!context.Request.HasEntityBody) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Request body required." }).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private static bool IsJsonContentType(string? contentType) {
        if (string.IsNullOrWhiteSpace(contentType)) {
            return false;
        }
        var type = contentType.Split(';', 2)[0].Trim();
        if (type.Equals("application/json", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
