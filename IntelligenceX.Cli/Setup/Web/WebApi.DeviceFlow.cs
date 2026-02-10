using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleDeviceCodeAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<DeviceCodeRequest>(body, _jsonOptions) ?? new DeviceCodeRequest();
        var effectiveClientId = request.GetEffectiveClientId();
        if (string.IsNullOrWhiteSpace(effectiveClientId)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetAuthBaseUrl(request.AuthBaseUrl, out var authBaseUrl, out var authError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = authError }).ConfigureAwait(false);
            return;
        }

        try {
            var result = await GitHubDeviceFlowClient.RequestCodeAsync(effectiveClientId, authBaseUrl, request.Scopes)
                .ConfigureAwait(false);
            await WriteJsonAsync(context, result).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleDeviceCodeAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

    private async Task HandleDevicePollAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<DevicePollRequest>(body, _jsonOptions) ?? new DevicePollRequest();
        var effectiveClientId = request.GetEffectiveClientId();
        if (string.IsNullOrWhiteSpace(effectiveClientId) || string.IsNullOrWhiteSpace(request.DeviceCode)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId or deviceCode" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetAuthBaseUrl(request.AuthBaseUrl, out var authBaseUrl, out var authError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = authError }).ConfigureAwait(false);
            return;
        }

        try {
            var token = await GitHubDeviceFlowClient.PollTokenAsync(effectiveClientId, request.DeviceCode!, authBaseUrl, request.IntervalSeconds, request.ExpiresIn)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token)) {
                context.Response.StatusCode = 408;
                await WriteJsonAsync(context, new { error = "Device flow expired. Please start again." }).ConfigureAwait(false);
                return;
            }
            await WriteJsonAsync(context, new { token }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleDevicePollAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

}
