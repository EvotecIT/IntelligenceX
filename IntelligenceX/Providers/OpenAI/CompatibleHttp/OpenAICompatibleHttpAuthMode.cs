namespace IntelligenceX.OpenAI.CompatibleHttp;

/// <summary>
/// Authentication mode for OpenAI-compatible HTTP transports.
/// </summary>
public enum OpenAICompatibleHttpAuthMode {
    /// <summary>
    /// No Authorization header is sent.
    /// </summary>
    None = 0,
    /// <summary>
    /// Sends <c>Authorization: Bearer ...</c> using <see cref="OpenAICompatibleHttpOptions.ApiKey"/>.
    /// </summary>
    Bearer = 1,
    /// <summary>
    /// Sends <c>Authorization: Basic ...</c> using <see cref="OpenAICompatibleHttpOptions.BasicUsername"/>
    /// and <see cref="OpenAICompatibleHttpOptions.BasicPassword"/>.
    /// </summary>
    Basic = 2
}
