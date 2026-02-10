using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task<string> ReadBodyAsync(System.Net.HttpListenerContext context) {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task WriteJsonAsync(System.Net.HttpListenerContext context, object payload) {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    private async Task<string?> ReadJsonBodyAsync(System.Net.HttpListenerContext context) {
        if (!await RequirePostJsonAsync(context).ConfigureAwait(false)) {
            return null;
        }
        var body = await ReadBodyAsync(context).ConfigureAwait(false);
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
