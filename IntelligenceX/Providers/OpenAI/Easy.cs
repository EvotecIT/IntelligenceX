using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Convenience helpers for one-shot chat calls.
/// </summary>
public static class Easy {
    /// <summary>
    /// Sends a text-only chat request using an ephemeral session.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<EasyChatResult> ChatAsync(string text, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatAsync(ChatInput.FromText(text), chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    /// <summary>
    /// Sends a chat request with an image loaded from a local path.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="imagePath">Local image path.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<EasyChatResult> ChatWithImagePathAsync(string text, string imagePath, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImagePathAsync(text, imagePath, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    /// <summary>
    /// Sends a chat request with an image loaded from a URL.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="imageUrl">Image URL.</param>
    /// <param name="sessionOptions">Optional session options.</param>
    /// <param name="chatOptions">Optional chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<EasyChatResult> ChatWithImageUrlAsync(string text, string imageUrl, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImageUrlAsync(text, imageUrl, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }
}
