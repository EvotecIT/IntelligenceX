using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Utils;

internal static class RpcRetryHelper {
    public static Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, RpcRetryOptions? options,
        bool allowRetry, CancellationToken cancellationToken) {
        if (!allowRetry || options is null || !options.Enabled) {
            return action(cancellationToken);
        }
        return ExecuteCoreAsync(action, options, cancellationToken);
    }

    private static async Task<T> ExecuteCoreAsync<T>(Func<CancellationToken, Task<T>> action, RpcRetryOptions options,
        CancellationToken cancellationToken) {
        var delay = options.InitialDelay;
        for (var attempt = 0; attempt <= options.RetryCount; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                return await action(cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (attempt < options.RetryCount && options.IsRetryable(ex)) {
                if (delay > TimeSpan.Zero) {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = NextDelay(delay, options.MaxDelay);
                }
            }
        }
        return await action(cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan NextDelay(TimeSpan current, TimeSpan max) {
        if (current <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return next > max ? max : next;
    }
}
