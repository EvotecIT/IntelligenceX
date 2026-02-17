using System.Net;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests runtime detection probe classification and configured endpoint detection semantics.
/// </summary>
public sealed class MainWindowLocalRuntimeDetectionTests {
    /// <summary>
    /// Ensures success statuses are classified as model-list available.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public void ClassifyModelsProbeResponse_Marks2xxAsAvailable(HttpStatusCode statusCode) {
        var result = MainWindow.ClassifyModelsProbeResponse(statusCode);
        Assert.Equal(MainWindow.ModelsProbeAvailability.Available, result);
    }

    /// <summary>
    /// Ensures auth-gated statuses are classified as reachable but requiring auth.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ClassifyModelsProbeResponse_Marks401403AsReachableAuthRequired(HttpStatusCode statusCode) {
        var result = MainWindow.ClassifyModelsProbeResponse(statusCode);
        Assert.Equal(MainWindow.ModelsProbeAvailability.ReachableAuthRequired, result);
    }

    /// <summary>
    /// Ensures non-success/non-auth statuses are treated as unavailable.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ClassifyModelsProbeResponse_MarksNonReachableStatusesAsUnavailable(HttpStatusCode statusCode) {
        var result = MainWindow.ClassifyModelsProbeResponse(statusCode);
        Assert.Equal(MainWindow.ModelsProbeAvailability.Unavailable, result);
    }

    /// <summary>
    /// Ensures configured external compatible endpoints are considered detected
    /// when model listing works or auth is required.
    /// </summary>
    [Theory]
    [InlineData((int)MainWindow.ModelsProbeAvailability.Available, true)]
    [InlineData((int)MainWindow.ModelsProbeAvailability.ReachableAuthRequired, true)]
    [InlineData((int)MainWindow.ModelsProbeAvailability.Unavailable, false)]
    public void IsConfiguredCompatibleEndpointDetected_AcceptsAvailableAndAuthRequired(
        int availability,
        bool expected) {
        var result = MainWindow.IsConfiguredCompatibleEndpointDetected((MainWindow.ModelsProbeAvailability)availability);
        Assert.Equal(expected, result);
    }
}
