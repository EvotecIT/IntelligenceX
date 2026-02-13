using System.Text.Json.Serialization;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Abstractions.Serialization;

/// <summary>
/// Source-generated System.Text.Json context for the chat service protocol.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatServiceRequest))]
[JsonSerializable(typeof(ChatServiceMessage))]
[JsonSerializable(typeof(HelloRequest))]
[JsonSerializable(typeof(EnsureLoginRequest))]
[JsonSerializable(typeof(StartChatGptLoginRequest))]
[JsonSerializable(typeof(ChatGptLoginPromptResponseRequest))]
[JsonSerializable(typeof(CancelChatGptLoginRequest))]
[JsonSerializable(typeof(ListToolsRequest))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatRequestOptions))]
[JsonSerializable(typeof(ErrorMessage))]
[JsonSerializable(typeof(AckMessage))]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(LoginStatusMessage))]
[JsonSerializable(typeof(ChatGptLoginStartedMessage))]
[JsonSerializable(typeof(ChatGptLoginUrlMessage))]
[JsonSerializable(typeof(ChatGptLoginPromptMessage))]
[JsonSerializable(typeof(ChatGptLoginCompletedMessage))]
[JsonSerializable(typeof(ToolListMessage))]
[JsonSerializable(typeof(ChatStatusMessage))]
[JsonSerializable(typeof(ChatDeltaMessage))]
[JsonSerializable(typeof(ChatResultMessage))]
[JsonSerializable(typeof(ToolDefinitionDto))]
[JsonSerializable(typeof(ToolCallDto))]
[JsonSerializable(typeof(ToolOutputDto))]
[JsonSerializable(typeof(ToolRunDto))]
[JsonSerializable(typeof(SessionPolicyDto))]
[JsonSerializable(typeof(ToolPackInfoDto))]
public sealed partial class ChatServiceJsonContext : JsonSerializerContext;
