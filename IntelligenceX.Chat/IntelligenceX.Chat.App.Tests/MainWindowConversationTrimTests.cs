using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards conversation-cap enforcement for system+user thread retention.
/// </summary>
public sealed class MainWindowConversationTrimTests {
    private const string SystemConversationId = "chat-system";

    private static readonly Type ConversationRuntimeType = typeof(MainWindow).GetNestedType("ConversationRuntime", BindingFlags.NonPublic)
                                                           ?? throw new InvalidOperationException("ConversationRuntime type not found.");

    private static readonly MethodInfo TrimConversationsToLimitMethod = typeof(MainWindow).GetMethod(
                                                                            "TrimConversationsToLimit",
                                                                            BindingFlags.NonPublic | BindingFlags.Instance)
                                                                        ?? throw new InvalidOperationException("TrimConversationsToLimit not found.");

    private static readonly FieldInfo ConversationsField = typeof(MainWindow).GetField(
                                                               "_conversations",
                                                               BindingFlags.NonPublic | BindingFlags.Instance)
                                                           ?? throw new InvalidOperationException("_conversations field not found.");

    private static readonly FieldInfo ActiveConversationIdField = typeof(MainWindow).GetField(
                                                                      "_activeConversationId",
                                                                      BindingFlags.NonPublic | BindingFlags.Instance)
                                                                  ?? throw new InvalidOperationException("_activeConversationId field not found.");

    private static readonly FieldInfo ActiveRequestConversationIdField = typeof(MainWindow).GetField(
                                                                             "_activeRequestConversationId",
                                                                             BindingFlags.NonPublic | BindingFlags.Instance)
                                                                         ?? throw new InvalidOperationException("_activeRequestConversationId field not found.");

    private static readonly PropertyInfo ConversationIdProperty = ConversationRuntimeType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                                                              ?? throw new InvalidOperationException("ConversationRuntime.Id property not found.");

    private static readonly PropertyInfo ConversationUpdatedUtcProperty = ConversationRuntimeType.GetProperty("UpdatedUtc", BindingFlags.Public | BindingFlags.Instance)
                                                                      ?? throw new InvalidOperationException("ConversationRuntime.UpdatedUtc property not found.");

    /// <summary>
    /// Ensures user-conversation cap is enforced even when many conversations share the active conversation id.
    /// </summary>
    [Fact]
    public void TrimConversationsToLimit_EnforcesCapWhenUserConversationsShareActiveId() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var conversations = CreateConversationList();

        // Build 41 user conversations with duplicated active id + one reserved system conversation.
        var nowUtc = new DateTime(2026, 2, 16, 16, 45, 0, DateTimeKind.Utc);
        for (var i = 0; i < 41; i++) {
            conversations.Add(CreateConversation("chat-dup-active", nowUtc.AddMinutes(-i)));
        }
        conversations.Add(CreateConversation(SystemConversationId, nowUtc.AddHours(-2)));

        ConversationsField.SetValue(window, conversations);
        ActiveConversationIdField.SetValue(window, "chat-dup-active");
        ActiveRequestConversationIdField.SetValue(window, null);

        InvokeTrim(window);

        var remaining = Assert.IsAssignableFrom<IList>(ConversationsField.GetValue(window));
        var userCount = 0;
        var systemCount = 0;
        for (var i = 0; i < remaining.Count; i++) {
            var id = Assert.IsType<string>(ConversationIdProperty.GetValue(remaining[i]));
            if (string.Equals(id, SystemConversationId, StringComparison.OrdinalIgnoreCase)) {
                systemCount++;
            } else {
                userCount++;
            }
        }

        Assert.Equal(39, userCount);
        Assert.Equal(1, systemCount);
        Assert.Equal(40, remaining.Count);
        Assert.Equal("chat-dup-active", Assert.IsType<string>(ActiveConversationIdField.GetValue(window)));
    }

    private static IList CreateConversationList() {
        var listType = typeof(List<>).MakeGenericType(ConversationRuntimeType);
        return Assert.IsAssignableFrom<IList>(Activator.CreateInstance(listType));
    }

    private static object CreateConversation(string id, DateTime updatedUtc) {
        var conversation = Activator.CreateInstance(ConversationRuntimeType, nonPublic: true)
                           ?? throw new InvalidOperationException("Failed to create ConversationRuntime instance.");
        ConversationIdProperty.SetValue(conversation, id);
        ConversationUpdatedUtcProperty.SetValue(conversation, updatedUtc);
        return conversation;
    }

    private static void InvokeTrim(MainWindow window) {
        try {
            TrimConversationsToLimitMethod.Invoke(window, null);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }
}
