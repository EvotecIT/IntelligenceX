using System;
using System.IO;

namespace IntelligenceX.Visualization.Heatmaps;

/// <summary>
/// Public facade for writing telemetry overview report bundles.
/// </summary>
public static class UsageTelemetryOverviewReportExporter {
    /// <summary>
    /// Writes a telemetry overview bundle and returns the main HTML report path.
    /// </summary>
    public static string WriteBundle(UsageTelemetryOverviewDocument overview, string outputDirectory) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }
        if (string.IsNullOrWhiteSpace(outputDirectory)) {
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        UsageTelemetryReportBundleWriter.WriteOverviewBundle(overview, outputDirectory);
        return Path.Combine(outputDirectory, "index.html");
    }
}
