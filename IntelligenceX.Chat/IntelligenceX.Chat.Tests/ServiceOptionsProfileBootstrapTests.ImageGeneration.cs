using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers explicit image-generation launch override and clear semantics.
/// </summary>
public sealed partial class ServiceOptionsProfileBootstrapTests {
    /// <summary>
    /// Ensures clear switches remove previously supplied values without invalid empty CLI arguments.
    /// </summary>
    [Fact]
    public void Parse_ImageGenerationClearSwitchesRemoveOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--image-generation-quality", "high",
            "--clear-image-generation-quality",
            "--image-generation-size", "1024x1024",
            "--clear-image-generation-size",
            "--image-generation-output-format", "png",
            "--clear-image-generation-output-format",
            "--image-generation-output-compression", "80",
            "--clear-image-generation-output-compression",
            "--image-generation-background", "transparent",
            "--clear-image-generation-background",
            "--image-generation-output-directory", "images",
            "--clear-image-generation-output-directory"
        }, out var error);

        Assert.Null(error);
        Assert.Null(options.ImageGenerationQuality);
        Assert.Null(options.ImageGenerationSize);
        Assert.Null(options.ImageGenerationOutputFormat);
        Assert.Null(options.ImageGenerationOutputCompression);
        Assert.Null(options.ImageGenerationBackground);
        Assert.Null(options.ImageGenerationOutputDirectory);
    }
}
