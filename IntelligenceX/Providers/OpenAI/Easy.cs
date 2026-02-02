using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Convenience helpers for one-off chat requests.
/// </summary>
public static class Easy {
    /// <summary>Runs a single text chat request using a temporary session.</summary>
    /// <param name="text">The user input.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<EasyChatResult> ChatAsync(string text, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatAsync(ChatInput.FromText(text), chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    /// <summary>Runs a single chat request with a local image path.</summary>
    /// <param name="text">The user input.</param>
    /// <param name="imagePath">The image path to attach.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<EasyChatResult> ChatWithImagePathAsync(string text, string imagePath, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImagePathAsync(text, imagePath, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    /// <summary>Runs a single chat request with an image URL.</summary>
    /// <param name="text">The user input.</param>
    /// <param name="imageUrl">The image URL to attach.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<EasyChatResult> ChatWithImageUrlAsync(string text, string imageUrl, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImageUrlAsync(text, imageUrl, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }
}
