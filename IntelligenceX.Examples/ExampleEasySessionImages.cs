using System;
using System.IO;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Examples;

internal sealed class ExampleEasySessionImages : IExample {
    public string Name => "easy-session-images";
    public string Description => "EasySession with workspace + image input + output extraction.";

    public async Task RunAsync() {
        var options = new EasySessionOptions {
            Workspace = Environment.CurrentDirectory
        };

        await using var session = await EasySession.StartAsync(options).ConfigureAwait(false);
        using var sub = session.SubscribeDelta(Console.Write);

        var imagePath = Path.Combine(Environment.CurrentDirectory, "example.png");
        if (File.Exists(imagePath)) {
            var turn = await session.ChatWithImagePathAsync("Describe this image.", imagePath).ConfigureAwait(false);
            foreach (var image in turn.ImageOutputs) {
                Console.WriteLine(image.ImageUrl ?? image.ImagePath ?? image.Base64 ?? "<image>");
            }
        } else {
            Console.WriteLine($"Image example skipped; file not found: {imagePath}");
        }

        await session.ChatAsync("Write a file named report.txt with a short summary.").ConfigureAwait(false);
    }
}
