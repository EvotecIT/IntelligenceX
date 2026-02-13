using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Small wrapper helpers for tool invocation to keep error handling consistent.
/// </summary>
public static class ToolInvoker {
    /// <summary>
    /// Runs a synchronous tool implementation with standard exception handling.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="func">Tool implementation.</param>
    public static string Run(CancellationToken cancellationToken, Func<string> func) {
        if (func is null) throw new ArgumentNullException(nameof(func));
        cancellationToken.ThrowIfCancellationRequested();

        try {
            return func();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("exception", ex.Message);
        }
    }

    /// <summary>
    /// Runs an asynchronous tool implementation with standard exception handling.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="func">Tool implementation.</param>
    public static async Task<string> RunAsync(CancellationToken cancellationToken, Func<Task<string>> func) {
        if (func is null) throw new ArgumentNullException(nameof(func));
        cancellationToken.ThrowIfCancellationRequested();

        try {
            return await func().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("exception", ex.Message);
        }
    }
}
