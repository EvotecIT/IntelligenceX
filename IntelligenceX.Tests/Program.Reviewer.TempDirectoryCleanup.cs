namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void DeleteDirectoryIfExistsWithRetries(string? path, int maxAttempts = 5, int delayMilliseconds = 50) {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
            return;
        }

        if (maxAttempts < 1) {
            maxAttempts = 1;
        }

        if (delayMilliseconds < 0) {
            delayMilliseconds = 0;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                Directory.Delete(path, true);
                return;
            } catch (IOException) when (attempt < maxAttempts) {
                if (delayMilliseconds > 0) {
                    System.Threading.Thread.Sleep(delayMilliseconds);
                }
            } catch (UnauthorizedAccessException) when (attempt < maxAttempts) {
                if (delayMilliseconds > 0) {
                    System.Threading.Thread.Sleep(delayMilliseconds);
                }
            }
        }
    }
}
#endif
