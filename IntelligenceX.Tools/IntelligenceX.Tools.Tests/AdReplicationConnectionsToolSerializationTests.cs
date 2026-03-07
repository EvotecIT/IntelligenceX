using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Text.Json;
using ADPlayground.Replication;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdReplicationConnectionsToolSerializationTests {
    [Fact]
    public void MapReplicationScheduleForResponse_FlattensScheduleToSerializableView() {
        var schedule = new ActiveDirectorySchedule();
        var raw = new bool[7, 24, 4];
        raw[0, 0, 0] = true;
        raw[0, 0, 1] = true;
        raw[2, 5, 3] = true;
        schedule.RawSchedule = raw;

        var view = AdReplicationConnectionsTool.MapReplicationScheduleForResponse(schedule);

        Assert.NotNull(view);
        Assert.Equal(7, view!.Days);
        Assert.Equal(24, view.HoursPerDay);
        Assert.Equal(4, view.SlotsPerHour);
        Assert.Equal(3, view.AllowedSlots);
        Assert.Equal(7 * 24 * 4, view.TotalSlots);
        Assert.Equal(2, view.AllowedSlotsByDay[0]);
        Assert.Equal(1, view.AllowedSlotsByDay[2]);
        Assert.True(view.AllowedHoursGrid[0][0]);
        Assert.True(view.AllowedHoursGrid[2][5]);
        Assert.False(view.AllowedHoursGrid[1][0]);

        var json = JsonSerializer.Serialize(view);
        Assert.Contains("AllowedHoursGrid", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MapReplicationScheduleForResponse_UsesReadOnlyCollections() {
        var schedule = new ActiveDirectorySchedule();
        var raw = new bool[7, 24, 4];
        raw[0, 0, 0] = true;
        schedule.RawSchedule = raw;

        var view = AdReplicationConnectionsTool.MapReplicationScheduleForResponse(schedule);
        Assert.NotNull(view);

        var slotsByDay = Assert.IsAssignableFrom<IList<int>>(view!.AllowedSlotsByDay);
        Assert.Throws<NotSupportedException>(() => slotsByDay.Add(1));

        var hoursGrid = Assert.IsAssignableFrom<IList<IReadOnlyList<bool>>>(view.AllowedHoursGrid);
        Assert.Throws<NotSupportedException>(() => hoursGrid.Add(Array.Empty<bool>()));

        var firstDay = Assert.IsAssignableFrom<IList<bool>>(view.AllowedHoursGrid[0]);
        Assert.Throws<NotSupportedException>(() => firstDay[0] = false);
    }

    [Fact]
    public void MapConnectionForResponse_ReplacesUnsupportedSchedulePayload() {
        var schedule = new ActiveDirectorySchedule();
        var raw = new bool[7, 24, 4];
        raw[1, 7, 2] = true;
        schedule.RawSchedule = raw;

        var connection = new SiteConnectionInfo(
            Name: "CN=Conn-01",
            Site: "Default-First-Site-Name",
            SourceServer: "DC01",
            SourceSite: "Branch-Site-Name",
            DestinationServer: "DC02",
            Transport: ActiveDirectoryTransportType.Rpc,
            Enabled: true,
            GeneratedByKcc: true,
            ReciprocalReplicationEnabled: false,
            ChangeNotificationStatus: NotificationStatus.IntraSiteOnly,
            DataCompressionEnabled: true,
            ReplicationScheduleOwnedByUser: false,
            ReplicationSpan: ReplicationSpan.InterSite,
            ReplicationSchedule: schedule);

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(connection));

        var row = AdReplicationConnectionsTool.MapConnectionForResponse(connection);
        var rowJson = JsonSerializer.Serialize(row);
        Assert.Contains("ReplicationSchedule", rowJson, StringComparison.Ordinal);
        Assert.DoesNotContain("RawSchedule", rowJson, StringComparison.Ordinal);
    }
}
