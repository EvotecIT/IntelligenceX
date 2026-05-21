using System;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Treatment;

/// <summary>
/// Builds provider prompts from generic treatment requests.
/// </summary>
public static class TreatmentPromptBuilder {
    /// <summary>
    /// Builds a single prompt from a treatment request.
    /// </summary>
    public static string Build(TreatmentRequest request) {
        return Build(request, null);
    }

    /// <summary>
    /// Builds a single prompt from a treatment request.
    /// </summary>
    public static string Build(TreatmentRequest request, TreatmentPromptBuildOptions? options) {
        Validate(request);
        options ??= CreateDefaultOptions(request);

        var sb = new StringBuilder();
        sb.AppendLine("# Treatment Request");
        AppendLine(sb, "id", request.Id);
        AppendLine(sb, "name", request.Name);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.Prompt)) {
            sb.AppendLine("## Brief");
            sb.AppendLine(request.Prompt!.Trim());
            sb.AppendLine();
        }

        if (request.Inputs.Count > 0) {
            sb.AppendLine("## Inputs");
            sb.AppendLine("Use the following artifacts as source material. Private artifacts are not publishable output by themselves.");
            for (var i = 0; i < request.Inputs.Count; i++) {
                AppendInput(sb, request.Inputs[i], i + 1, options);
            }
            sb.AppendLine();
        }

        if (request.Outputs.Count > 0) {
            sb.AppendLine("## Expected Outputs");
            for (var i = 0; i < request.Outputs.Count; i++) {
                AppendOutput(sb, request.Outputs[i], i + 1);
            }
            sb.AppendLine();
        }

        if (request.OutputSchema is not null) {
            sb.AppendLine("## Output Contract");
            AppendLine(sb, "contract", request.OutputSchema.Contract);
            sb.AppendLine("strict: " + request.OutputSchema.Strict.ToString().ToLowerInvariant());
            if (request.OutputSchema.JsonSchema is not null) {
                sb.AppendLine("jsonSchema:");
                sb.AppendLine(JsonLite.Serialize(JsonValue.From(request.OutputSchema.JsonSchema)));
            }
            if (request.OutputSchema.ExampleJson is not null) {
                sb.AppendLine("exampleJson:");
                sb.AppendLine(JsonLite.Serialize(JsonValue.From(request.OutputSchema.ExampleJson)));
            }
            sb.AppendLine();
        }

        if (request.ImageGeneration is { Enabled: true }) {
            sb.AppendLine("## Visual Assets");
            sb.AppendLine("Generate image assets only when requested by the expected outputs or brief.");
            AppendLine(sb, "quality", request.ImageGeneration.Quality);
            AppendLine(sb, "size", request.ImageGeneration.Size);
            AppendLine(sb, "format", request.ImageGeneration.OutputFormat);
            sb.AppendLine();
        }

        if (request.Metadata.Count > 0) {
            sb.AppendLine("## Metadata");
            foreach (var pair in request.Metadata) {
                AppendLine(sb, pair.Key, pair.Value);
            }
            sb.AppendLine();
        }

        sb.AppendLine("Return only the requested deliverable. When a JSON contract is supplied, return JSON that matches it.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Validates a treatment request before it is sent to a provider.
    /// </summary>
    public static void Validate(TreatmentRequest request) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Prompt) && (request.Inputs is null || request.Inputs.Count == 0)) {
            throw new ArgumentException("Treatment request must include a prompt or at least one input artifact.", nameof(request));
        }
        if (request.Inputs is null) {
            throw new ArgumentException("Treatment inputs cannot be null.", nameof(request));
        }
        if (request.Outputs is null) {
            throw new ArgumentException("Treatment outputs cannot be null.", nameof(request));
        }
        if (request.Metadata is null) {
            throw new ArgumentException("Treatment metadata cannot be null.", nameof(request));
        }
    }

    private static TreatmentPromptBuildOptions CreateDefaultOptions(TreatmentRequest request) {
        return new TreatmentPromptBuildOptions {
            BaseDirectory = request.WorkingDirectory ?? request.Workspace,
            InlineLocalFiles = request.InlineLocalInputFiles,
            MaxInlineFileCharacters = request.MaxInlineFileCharacters ?? 120000
        };
    }

    private static void AppendInput(StringBuilder sb, TreatmentInputArtifact input, int index, TreatmentPromptBuildOptions options) {
        if (input is null) {
            throw new ArgumentException("Treatment input cannot contain null entries.");
        }

        sb.AppendLine("### Input " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendLine(sb, "id", input.Id);
        AppendLine(sb, "role", input.Role);
        AppendLine(sb, "name", input.Name);
        AppendLine(sb, "mediaType", input.MediaType);
        sb.AppendLine("private: " + input.IsPrivate.ToString().ToLowerInvariant());
        AppendLine(sb, "path", input.Path);
        AppendLine(sb, "uri", input.Uri?.ToString());
        var metadata = input.Metadata ?? TreatmentRequest.EmptyDictionary;
        if (metadata.Count > 0) {
            sb.AppendLine("metadata:");
            foreach (var pair in metadata) {
                AppendLine(sb, "  " + pair.Key, pair.Value);
            }
        }
        if (input.Json is not null) {
            sb.AppendLine("json:");
            sb.AppendLine(JsonLite.Serialize(JsonValue.From(input.Json)));
        }
        if (!string.IsNullOrWhiteSpace(input.Text)) {
            sb.AppendLine("text:");
            sb.AppendLine(input.Text!.Trim());
        } else {
            var localContent = TreatmentLocalInputReader.TryRead(input, options);
            if (localContent is not null) {
                AppendLine(sb, "resolvedPath", FormatPromptPath(localContent.Path, options.BaseDirectory));
                if (!string.IsNullOrWhiteSpace(localContent.Warning)) {
                    AppendLine(sb, "warning", localContent.Warning);
                }
                if (localContent.Text is not null) {
                    sb.AppendLine(localContent.Truncated ? "textTruncated:" : "text:");
                    sb.AppendLine(localContent.Text.Trim());
                }
            }
        }
    }

    private static string FormatPromptPath(string path, string? baseDirectory) {
        try {
            if (!string.IsNullOrWhiteSpace(baseDirectory)) {
                var root = System.IO.Path.GetFullPath(baseDirectory!);
                var fullPath = System.IO.Path.GetFullPath(path);
                var relative = MakeRelativePath(root, fullPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative)) {
                    return relative.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                }
            }
        } catch (ArgumentException) {
        } catch (NotSupportedException) {
        }

        return System.IO.Path.GetFileName(path);
    }

    private static string MakeRelativePath(string root, string path) {
        var rootUri = new Uri(AppendDirectorySeparator(root));
        var pathUri = new Uri(path);
        if (!rootUri.IsBaseOf(pathUri)) {
            return path;
        }

        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path) {
        var separator = System.IO.Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
    }

    private static void AppendOutput(StringBuilder sb, TreatmentOutputSpec output, int index) {
        if (output is null) {
            throw new ArgumentException("Treatment output cannot contain null entries.");
        }

        sb.Append("- ");
        sb.Append(output.Id ?? ("output-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        sb.Append(" [");
        sb.Append(output.Modality);
        sb.Append("]");
        if (!output.Required) {
            sb.Append(" optional");
        }
        sb.AppendLine();
        AppendLine(sb, "  description", output.Description);
        AppendLine(sb, "  mediaType", output.MediaType);
        AppendLine(sb, "  path", output.Path);
    }

    private static void AppendLine(StringBuilder sb, string key, string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }
        sb.Append(key);
        sb.Append(": ");
        sb.AppendLine(value!.Trim());
    }
}
