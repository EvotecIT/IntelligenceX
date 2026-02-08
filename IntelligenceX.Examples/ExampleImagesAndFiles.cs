using System;
using System.IO;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Examples;

internal sealed class ExampleImagesAndFiles : IExample {
    public string Name => "images-files";
    public string Description => "Send an image input and enable workspace file writes.";

    public async Task RunAsync() {
        await using var ix = await IntelligenceXClient.ConnectAsync().ConfigureAwait(false);
        await ix.EnsureChatGptLoginAsync(onUrl: url => Console.WriteLine($"Open: {url}")).ConfigureAwait(false);
        using var sub = ix.SubscribeDelta(Console.Write);

        var imagePath = Path.Combine(Environment.CurrentDirectory, "example.png");
        if (File.Exists(imagePath)) {
            var turn = await ix.ChatWithImagePathAsync("Describe this image.", imagePath).ConfigureAwait(false);
            if (turn.ImageOutputs.Count > 0) {
                foreach (var image in turn.ImageOutputs) {
                    Console.WriteLine(image.ImageUrl ?? image.ImagePath ?? image.Base64 ?? "<image>");
                }
            }
        } else {
            Console.WriteLine($"Image example skipped; file not found: {imagePath}");
        }

        if (ix.TransportKind == OpenAITransportKind.AppServer) {
            var options = new ChatOptions { Workspace = Environment.CurrentDirectory };
            await ix.ChatAsync(ChatInput.FromText("Create a report.txt file with a short summary of this run."), options)
                .ConfigureAwait(false);
        } else {
            Console.WriteLine("Workspace file writes require the app-server transport.");
        }
    }
}
