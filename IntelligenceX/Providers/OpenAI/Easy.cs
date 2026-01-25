using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

public static class Easy {
    public static async Task<EasyChatResult> ChatAsync(string text, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatAsync(ChatInput.FromText(text), chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    public static async Task<EasyChatResult> ChatWithImagePathAsync(string text, string imagePath, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImagePathAsync(text, imagePath, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    public static async Task<EasyChatResult> ChatWithImageUrlAsync(string text, string imageUrl, EasySessionOptions? sessionOptions = null,
        EasyChatOptions? chatOptions = null, CancellationToken cancellationToken = default) {
        await using var session = await EasySession.StartAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        var turn = await session.ChatWithImageUrlAsync(text, imageUrl, chatOptions, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }
}
