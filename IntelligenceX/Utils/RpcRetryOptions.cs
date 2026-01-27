using System;
using System.IO;

namespace IntelligenceX.Utils;

public sealed class RpcRetryOptions {
    public int RetryCount { get; set; }
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
    public Func<Exception, bool>? ShouldRetry { get; set; }

    public bool Enabled => RetryCount > 0;

    public bool IsRetryable(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (ShouldRetry is not null) {
            return ShouldRetry(ex);
        }
        return ex is IOException || ex is TimeoutException;
    }
}
