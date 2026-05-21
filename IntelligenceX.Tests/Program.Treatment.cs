using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Treatment;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestTreatmentPromptBuilderIncludesArtifactsAndContract() {
        var request = new TreatmentRequest {
            Id = "job-1",
            Name = "generic article treatment",
            Prompt = "Write a Polish article from the supplied sources.",
            Inputs = new[] {
                new TreatmentInputArtifact {
                    Id = "source-1",
                    Role = "source",
                    Name = "Example source",
                    MediaType = "text/markdown",
                    Text = "# Matter lock\nAqara announced details.",
                    Uri = new Uri("https://example.test/article"),
                    IsPrivate = true,
                    Metadata = new Dictionary<string, string> {
                        ["language"] = "en"
                    }
                }
            },
            Outputs = new[] {
                new TreatmentOutputSpec {
                    Id = "article",
                    Modality = TreatmentOutputModality.Json,
                    Description = "Publisher-ready article JSON"
                }
            },
            OutputSchema = new TreatmentOutputSchema {
                Contract = "publisher.article.v1",
                Strict = true,
                ExampleJson = new JsonObject().Add("title", "Example")
            }
        };

        var prompt = TreatmentPromptBuilder.Build(request);

        AssertContainsText(prompt, "# Treatment Request", "treatment prompt heading");
        AssertContainsText(prompt, "private: true", "treatment prompt private flag");
        AssertContainsText(prompt, "publisher.article.v1", "treatment prompt contract");
        AssertContainsText(prompt, "Write a Polish article", "treatment prompt brief");
        AssertContainsText(prompt, "https://example.test/article", "treatment prompt uri");
    }

    private static void TestTreatmentPromptBuilderRejectsEmptyRequest() {
        AssertThrows<ArgumentException>(() => TreatmentPromptBuilder.Build(new TreatmentRequest()), "empty treatment request");
    }

    private static void TestTreatmentPromptBuilderInlinesLocalTextArtifacts() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "evidence.json"), "{\"items\":[{\"title\":\"Matter lock\"}]}");
            var request = new TreatmentRequest {
                Prompt = "Read the evidence.",
                WorkingDirectory = directory,
                Inputs = new[] {
                    new TreatmentInputArtifact {
                        Id = "evidence",
                        MediaType = "application/json",
                        Path = "evidence.json"
                    }
                }
            };

            var prompt = TreatmentPromptBuilder.Build(request);

            AssertContainsText(prompt, "resolvedPath: evidence.json", "treatment prompt relative resolved path");
            AssertContainsText(prompt, "Matter lock", "treatment prompt inlined file content");
            AssertDoesNotContainText(prompt, directory, "treatment prompt omits absolute local path");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestTreatmentPromptBuilderHonorsInlineLocalFileLimit() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "content.md"), "abcdefghij");
            var request = new TreatmentRequest {
                Prompt = "Read the evidence.",
                WorkingDirectory = directory,
                MaxInlineFileCharacters = 4,
                Inputs = new[] {
                    new TreatmentInputArtifact {
                        Id = "content",
                        MediaType = "text/markdown",
                        Path = "content.md"
                    }
                }
            };

            var prompt = TreatmentPromptBuilder.Build(request);

            AssertContainsText(prompt, "textTruncated:", "treatment prompt truncated marker");
            AssertContainsText(prompt, "abcd", "treatment prompt truncated content");
            AssertDoesNotContainText(prompt, "abcde", "treatment prompt excludes beyond limit");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestTreatmentPromptBuilderHandlesNullMetadataAndParameterizedMediaType() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "evidence"), "{\"items\":[{\"title\":\"Parameterized media type\"}]}");
            var request = new TreatmentRequest {
                Prompt = "Read the evidence.",
                WorkingDirectory = directory,
                Inputs = new[] {
                    new TreatmentInputArtifact {
                        Id = "evidence",
                        MediaType = "application/json; charset=utf-8",
                        Metadata = null!,
                        Path = "evidence"
                    }
                }
            };

            var prompt = TreatmentPromptBuilder.Build(request);

            AssertContainsText(prompt, "Parameterized media type", "parameterized media type input content");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestTreatmentPromptBuilderReportsTraversalAsWarning() {
        var root = Path.Combine(Path.GetTempPath(), "ix-treatment-root-" + Guid.NewGuid().ToString("N"));
        var sibling = Path.Combine(Path.GetTempPath(), "ix-treatment-sibling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        try {
            File.WriteAllText(Path.Combine(sibling, "secret.md"), "do not inline");
            var request = new TreatmentRequest {
                Prompt = "Read the evidence.",
                WorkingDirectory = root,
                Inputs = new[] {
                    new TreatmentInputArtifact {
                        Id = "secret",
                        MediaType = "text/markdown",
                        Path = Path.Combine("..", Path.GetFileName(sibling), "secret.md")
                    }
                }
            };

            var prompt = TreatmentPromptBuilder.Build(request);

            AssertContainsText(prompt, "warning:", "traversal prompt warning");
            AssertContainsText(prompt, "local artifact path rejected", "traversal warning is sanitized");
            AssertDoesNotContainText(prompt, "do not inline", "traversal content not inlined");
            AssertDoesNotContainText(prompt, root, "traversal warning omits root path");
            AssertDoesNotContainText(prompt, sibling, "traversal warning omits escaped path");
        } finally {
            Directory.Delete(root, recursive: true);
            Directory.Delete(sibling, recursive: true);
        }
    }

    private static void TestTreatmentPromptBuilderClampsInlineLocalFileLimit() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "content.md"), "small evidence");
            var request = new TreatmentRequest {
                Prompt = "Read the evidence.",
                WorkingDirectory = directory,
                MaxInlineFileCharacters = int.MaxValue,
                Inputs = new[] {
                    new TreatmentInputArtifact {
                        Id = "content",
                        MediaType = "text/markdown",
                        Path = "content.md"
                    }
                }
            };

            var prompt = TreatmentPromptBuilder.Build(request);

            AssertContainsText(prompt, "inline character limit clamped", "inline limit clamp warning");
            AssertContainsText(prompt, "small evidence", "clamped inline content");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestOpenAIChatTreatmentProviderMapsTextJsonAndOptions() {
        var client = new FakeTreatmentChatClient(new TreatmentChatResponse(
            "turn-1",
            "completed",
            new[] {
                new TreatmentChatOutput("out-1", "text", "{\"title\":\"Aqara update\"}")
            }));
        var provider = new OpenAIChatTreatmentProvider(client);
        var request = new TreatmentRequest {
            Id = "job-2",
            Prompt = "Produce JSON.",
            Instructions = "Use neutral editorial tone.",
            Model = "gpt-5.4",
            ReasoningEffort = ReasoningEffort.Medium,
            TextVerbosity = TextVerbosity.Low,
            Temperature = 0.2,
            Workspace = @"C:\work",
            ImageGeneration = new TreatmentImageOptions {
                Enabled = true,
                OutputDirectory = @"C:\work\assets",
                Size = "1024x1024",
                OutputFormat = "png"
            }
        };

        var result = provider.RunAsync(request).GetAwaiter().GetResult();

        AssertEqual("job-2", result.Id, "treatment result id");
        AssertEqual("completed", result.Status, "treatment result status");
        AssertEqual("Aqara update", result.JsonObject?.GetString("title"), "treatment result json title");
        AssertEqual("gpt-5.4", client.Options?.Model, "treatment provider model");
        AssertEqual("Use neutral editorial tone.", client.Options?.Instructions, "treatment provider instructions");
        AssertEqual(true, client.Options?.ImageGeneration?.Enabled ?? false, "treatment provider image generation enabled");
        AssertEqual(@"C:\work\assets", client.Options?.ImageGeneration?.OutputDirectory, "treatment provider image output directory");
        AssertEqual(true, client.Options?.RequireWorkspaceForFileAccess ?? false, "treatment provider requires workspace file access");
    }

    private static void TestOpenAIChatTreatmentProviderParsesArrayJson() {
        var client = new FakeTreatmentChatClient(new TreatmentChatResponse(
            "turn-json-array",
            "completed",
            new[] {
                new TreatmentChatOutput("out-1", "text", "[{\"title\":\"Aqara update\"}]")
            }));
        var provider = new OpenAIChatTreatmentProvider(client);
        var request = new TreatmentRequest {
            Id = "job-array",
            Prompt = "Produce JSON array."
        };

        var result = provider.RunAsync(request).GetAwaiter().GetResult();

        AssertEqual(JsonValueKind.Array, result.Json?.Kind, "treatment result json array kind");
        AssertEqual(1, result.Json?.AsArray()?.Count ?? 0, "treatment result json array count");
    }

    private static void TestOpenAIChatTreatmentProviderMapsImageAssets() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var client = new FakeTreatmentChatClient(new TreatmentChatResponse(
            "turn-2",
            "completed",
            new[] {
                new TreatmentChatOutput(
                    id: "img-1",
                    type: "image",
                    imagePath: @"C:\work\assets\image.png",
                    mimeType: "image/png",
                    revisedPrompt: "Clean product photo",
                    raw: new JsonObject().Add("id", "img-1"))
            }));
        var provider = new OpenAIChatTreatmentProvider(client);
        var request = new TreatmentRequest {
            Prompt = "Create a hero image.",
            Model = "gpt-image-2",
            Inputs = new[] {
                new TreatmentInputArtifact {
                    Id = "source-image",
                    Path = "source.png"
                }
            },
            WorkingDirectory = directory
        };

        try {
            var result = provider.RunAsync(request).GetAwaiter().GetResult();
            var expectedImagePath = Path.GetFullPath(Path.Combine(directory, "source.png"));

            AssertEqual(1, result.Assets.Count, "treatment result asset count");
            AssertEqual("img-1", result.Assets[0].Id, "treatment image asset id");
            AssertEqual(@"C:\work\assets\image.png", result.Assets[0].Path, "treatment image asset path");
            AssertEqual("source-image", result.Assets[0].SourceInputIds[0], "treatment image source id");
            AssertEqual("Clean product photo", result.Assets[0].RevisedPrompt, "treatment image revised prompt");
            AssertEqual(expectedImagePath, client.Input?.GetImagePaths()[0], "treatment image input path resolved under working directory");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestOpenAIChatTreatmentProviderSkipsUnsupportedImplicitImages() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-treatment-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var client = new FakeTreatmentChatClient(new TreatmentChatResponse(
            "turn-unsupported-image",
            "completed",
            new[] {
                new TreatmentChatOutput("out-1", "text", "ok")
            }));
        var provider = new OpenAIChatTreatmentProvider(client);
        var request = new TreatmentRequest {
            Prompt = "Use supported images only.",
            WorkingDirectory = directory,
            Inputs = new[] {
                new TreatmentInputArtifact {
                    Id = "svg",
                    Path = "diagram.svg"
                },
                new TreatmentInputArtifact {
                    Id = "avif",
                    Path = "photo.avif"
                }
            }
        };

        try {
            provider.RunAsync(request).GetAwaiter().GetResult();

            AssertEqual(0, client.Input?.GetImagePaths().Length ?? -1, "unsupported implicit image inputs skipped");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeTreatmentChatClient : ITreatmentChatClient {
        private readonly TreatmentChatResponse _response;

        public FakeTreatmentChatClient(TreatmentChatResponse response) {
            _response = response;
        }

        public ChatInput? Input { get; private set; }
        public ChatOptions? Options { get; private set; }

        public Task<TreatmentChatResponse> SendAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken = default) {
            Input = input;
            Options = options;
            return Task.FromResult(_response);
        }
    }
}
