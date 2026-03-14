using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceProfilePresetTests {
    [Fact]
    public async Task HandleListProfilesAsync_IncludesBuiltInPluginOnlyPreset() {
        var dbPath = CreateTempProfileDbPath();
        try {
            var options = new ServiceOptions {
                StateDbPath = dbPath
            };
            using var buffer = new MemoryStream();
            using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
            var session = new ChatServiceSession(options, Stream.Null);
            var request = new ListProfilesRequest {
                RequestId = "req_profiles"
            };

            await InvokeHandleListProfilesAsync(session, writer, request);
            writer.Flush();
            buffer.Position = 0;

            using var document = await JsonDocument.ParseAsync(buffer);
            var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var profileList = Assert.IsType<ProfileListMessage>(response);

            Assert.Contains("plugin-only", profileList.Profiles);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task HandleSetProfileAsync_AcceptsBuiltInPluginOnlyPresetWithoutStoredProfile() {
        var dbPath = CreateTempProfileDbPath();
        try {
            var options = new ServiceOptions {
                StateDbPath = dbPath
            };
            using var buffer = new MemoryStream();
            using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
            var session = new ChatServiceSession(options, Stream.Null);
            var request = new SetProfileRequest {
                RequestId = "req_profile_set",
                ProfileName = "plugin_only"
            };

            await InvokeHandleSetProfileAsync(session, writer, request);
            writer.Flush();
            buffer.Position = 0;

            using var document = await JsonDocument.ParseAsync(buffer);
            var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var ack = Assert.IsType<AckMessage>(response);

            Assert.True(ack.Ok);
            Assert.Equal("plugin-only", options.ProfileName);
            Assert.False(options.EnableBuiltInPackLoading);
            Assert.True(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task HandleSetProfileAsync_LoadsSavedProfileNamedPluginOnly_BeforeBuiltInPreset() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "plugin-only", "saved-plugin-model", enableBuiltInPackLoading: true, enableDefaultPluginPaths: false);

            var options = new ServiceOptions {
                StateDbPath = dbPath
            };
            using var buffer = new MemoryStream();
            using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
            var session = new ChatServiceSession(options, Stream.Null);
            var request = new SetProfileRequest {
                RequestId = "req_profile_set_saved_exact",
                ProfileName = "plugin-only"
            };

            await InvokeHandleSetProfileAsync(session, writer, request);
            writer.Flush();
            buffer.Position = 0;

            using var document = await JsonDocument.ParseAsync(buffer);
            var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var ack = Assert.IsType<AckMessage>(response);

            Assert.True(ack.Ok);
            Assert.Equal("plugin-only", options.ProfileName);
            Assert.Equal("saved-plugin-model", options.Model);
            Assert.True(options.EnableBuiltInPackLoading);
            Assert.False(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task HandleSetProfileAsync_LoadsSavedProfileNamedPluginOnlyAlias_BeforeBuiltInPresetAlias() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "plugin_only", "saved-plugin-alias-model", enableBuiltInPackLoading: true, enableDefaultPluginPaths: false);

            var options = new ServiceOptions {
                StateDbPath = dbPath
            };
            using var buffer = new MemoryStream();
            using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
            var session = new ChatServiceSession(options, Stream.Null);
            var request = new SetProfileRequest {
                RequestId = "req_profile_set_saved_alias",
                ProfileName = "plugin_only"
            };

            await InvokeHandleSetProfileAsync(session, writer, request);
            writer.Flush();
            buffer.Position = 0;

            using var document = await JsonDocument.ParseAsync(buffer);
            var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var ack = Assert.IsType<AckMessage>(response);

            Assert.True(ack.Ok);
            Assert.Equal("plugin_only", options.ProfileName);
            Assert.Equal("saved-plugin-alias-model", options.Model);
            Assert.True(options.EnableBuiltInPackLoading);
            Assert.False(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task HandleListProfilesAsync_WithNoStateDb_ReturnsBuiltInPluginOnlyPreset() {
        var options = new ServiceOptions {
            NoStateDb = true
        };
        using var buffer = new MemoryStream();
        using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
        var session = new ChatServiceSession(options, Stream.Null);
        var request = new ListProfilesRequest {
            RequestId = "req_profiles_nostate"
        };

        await InvokeHandleListProfilesAsync(session, writer, request);
        writer.Flush();
        buffer.Position = 0;

        using var document = await JsonDocument.ParseAsync(buffer);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var profileList = Assert.IsType<ProfileListMessage>(response);

        Assert.Equal(new[] { "plugin-only" }, profileList.Profiles);
    }

    [Fact]
    public async Task HandleSetProfileAsync_AcceptsBuiltInPluginOnlyPreset_WhenStateDbDisabled() {
        var options = new ServiceOptions {
            NoStateDb = true
        };
        using var buffer = new MemoryStream();
        using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
        var session = new ChatServiceSession(options, Stream.Null);
        var request = new SetProfileRequest {
            RequestId = "req_profile_set_nostate",
            ProfileName = "plugin-only"
        };

        await InvokeHandleSetProfileAsync(session, writer, request);
        writer.Flush();
        buffer.Position = 0;

        using var document = await JsonDocument.ParseAsync(buffer);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var ack = Assert.IsType<AckMessage>(response);

        Assert.True(ack.Ok);
        Assert.Equal("plugin-only", options.ProfileName);
        Assert.False(options.EnableBuiltInPackLoading);
    }

    [Fact]
    public async Task HandleSetProfileAsync_NormalizesBuiltInPluginOnlyPresetAlias_WhenStateDbDisabled() {
        var options = new ServiceOptions {
            NoStateDb = true
        };
        using var buffer = new MemoryStream();
        using var writer = new StreamWriter(buffer, new UTF8Encoding(false), 1024, leaveOpen: true);
        var session = new ChatServiceSession(options, Stream.Null);
        var request = new SetProfileRequest {
            RequestId = "req_profile_set_nostate_alias",
            ProfileName = "plugin_only"
        };

        await InvokeHandleSetProfileAsync(session, writer, request);
        writer.Flush();
        buffer.Position = 0;

        using var document = await JsonDocument.ParseAsync(buffer);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var ack = Assert.IsType<AckMessage>(response);

        Assert.True(ack.Ok);
        Assert.Equal("plugin-only", options.ProfileName);
        Assert.False(options.EnableBuiltInPackLoading);
        Assert.True(options.EnableDefaultPluginPaths);
    }

    private static async Task InvokeHandleListProfilesAsync(ChatServiceSession session, StreamWriter writer, ListProfilesRequest request) {
        var method = typeof(ChatServiceSession).GetMethod("HandleListProfilesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, CancellationToken.None }));
        await task;
    }

    private static async Task InvokeHandleSetProfileAsync(ChatServiceSession session, StreamWriter writer, SetProfileRequest request) {
        var method = typeof(ChatServiceSession).GetMethod("HandleSetProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, CancellationToken.None }));
        await task;
    }

    private static string CreateTempProfileDbPath() {
        return TempPathTestHelper.CreateTempFilePath("ix-chat-service-profile-preset-tests", ".db");
    }

    private static void SeedProfile(string dbPath, string profileName, string model, bool enableBuiltInPackLoading, bool enableDefaultPluginPaths) {
        using var store = new SqliteServiceProfileStore(dbPath);
        var profile = new ServiceProfile {
            Model = model,
            EnableBuiltInPackLoading = enableBuiltInPackLoading,
            EnableDefaultPluginPaths = enableDefaultPluginPaths
        };
        store.UpsertAsync(profileName, profile, CancellationToken.None).GetAwaiter().GetResult();
    }

}
