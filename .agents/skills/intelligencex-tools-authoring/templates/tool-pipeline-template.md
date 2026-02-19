# Pipeline Tool Template

Use this template when adding a new tool that should follow the shared bind -> middleware -> execute flow.

```csharp
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

public sealed class ExampleTool : ToolBase, ITool {
    private sealed record RequestModel(string Name, bool IncludeDetails);

    private static readonly ToolDefinition DefinitionValue = new(
        "example_tool",
        "Describe what the tool does.",
        ToolSchema.Object(
                ("name", ToolSchema.String("Target name.")),
                ("include_details", ToolSchema.Boolean("Include detail rows.")))
            .Required("name")
            .NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteRequestAsync,
            middleware: Array.Empty<ToolPipelineMiddleware<RequestModel>>());
    }

    private static ToolRequestBindingResult<RequestModel> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("name", out var name, out var error)) {
                return ToolRequestBindingResult<RequestModel>.Failure(error);
            }

            return ToolRequestBindingResult<RequestModel>.Success(new RequestModel(
                Name: name,
                IncludeDetails: reader.Boolean("include_details")));
        });
    }

    private static Task<string> ExecuteRequestAsync(
        ToolPipelineContext<RequestModel> context,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var model = new {
            Name = context.Request.Name,
            IncludeDetails = context.Request.IncludeDetails
        };
        return Task.FromResult(ToolResultV2.OkModel(model));
    }
}
```

## Notes
- Use `ToolRequestBinder` for argument parsing and validation.
- Keep middleware small and composable; prefer reusable middleware in package base classes.
- Use `ToolResultV2` for envelope generation and immutable metadata handling.
