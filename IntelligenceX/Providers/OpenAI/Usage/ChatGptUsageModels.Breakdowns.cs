using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Represents daily token usage breakdown grouped by product surface.
/// </summary>
public sealed class ChatGptDailyTokenUsageBreakdown {
    /// <summary>
    /// Initializes a new daily token usage breakdown payload.
    /// </summary>
    public ChatGptDailyTokenUsageBreakdown(IReadOnlyList<ChatGptDailyTokenUsageDay> data, string? units, JsonObject raw,
        JsonObject? additional) {
        Data = data ?? Array.Empty<ChatGptDailyTokenUsageDay>();
        Units = units;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the daily data points.
    /// </summary>
    public IReadOnlyList<ChatGptDailyTokenUsageDay> Data { get; }
    /// <summary>
    /// Gets the unit label reported by the API.
    /// </summary>
    public string? Units { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a daily token usage breakdown payload from JSON.
    /// </summary>
    public static ChatGptDailyTokenUsageBreakdown FromJson(JsonObject obj) {
        var data = new List<ChatGptDailyTokenUsageDay>();
        var array = obj.GetArray("data");
        if (array is not null) {
            foreach (var item in array) {
                var day = item.AsObject();
                if (day is null) {
                    continue;
                }

                data.Add(ChatGptDailyTokenUsageDay.FromJson(day));
            }
        }

        var units = obj.GetString("units");
        var additional = obj.ExtractAdditional("data", "units");
        return new ChatGptDailyTokenUsageBreakdown(data, units, obj, additional);
    }

    /// <summary>
    /// Serializes the breakdown to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }

        var obj = new JsonObject();
        var data = new JsonArray();
        foreach (var day in Data) {
            data.Add(JsonValue.From(day.ToJson()));
        }

        obj.Add("data", data);
        if (!string.IsNullOrWhiteSpace(Units)) {
            obj.Add("units", Units);
        }

        return obj;
    }
}

/// <summary>
/// Represents one day's token usage breakdown by product surface.
/// </summary>
public sealed class ChatGptDailyTokenUsageDay {
    /// <summary>
    /// Initializes a new daily token usage row.
    /// </summary>
    public ChatGptDailyTokenUsageDay(string? date, IReadOnlyDictionary<string, double> productSurfaceUsageValues, JsonObject raw,
        JsonObject? additional) {
        Date = date;
        ProductSurfaceUsageValues = productSurfaceUsageValues ?? new Dictionary<string, double>(StringComparer.Ordinal);
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the bucket date.
    /// </summary>
    public string? Date { get; }
    /// <summary>
    /// Gets usage values keyed by product surface.
    /// </summary>
    public IReadOnlyDictionary<string, double> ProductSurfaceUsageValues { get; }
    /// <summary>
    /// Gets the total across all surfaces for the day.
    /// </summary>
    public double Total => ProductSurfaceUsageValues.Values.Sum();
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a daily token usage row from JSON.
    /// </summary>
    public static ChatGptDailyTokenUsageDay FromJson(JsonObject obj) {
        var date = obj.GetString("date");
        var usageValues = new Dictionary<string, double>(StringComparer.Ordinal);
        var usageObj = obj.GetObject("product_surface_usage_values") ?? obj.GetObject("productSurfaceUsageValues");
        if (usageObj is not null) {
            foreach (var pair in usageObj) {
                var value = pair.Value?.AsDouble();
                if (value.HasValue) {
                    usageValues[pair.Key] = value.Value;
                }
            }
        }

        var additional = obj.ExtractAdditional("date", "product_surface_usage_values", "productSurfaceUsageValues");
        return new ChatGptDailyTokenUsageDay(date, usageValues, obj, additional);
    }

    /// <summary>
    /// Serializes the day to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }

        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(Date)) {
            obj.Add("date", Date);
        }

        var usage = new JsonObject();
        foreach (var pair in ProductSurfaceUsageValues) {
            usage.Add(pair.Key, pair.Value);
        }

        obj.Add("product_surface_usage_values", usage);
        return obj;
    }
}

/// <summary>
/// Represents a combined usage snapshot and usage events report.
/// </summary>
public sealed class ChatGptUsageReport {
    /// <summary>
    /// Initializes a new usage report.
    /// </summary>
    public ChatGptUsageReport(ChatGptUsageSnapshot snapshot, IReadOnlyList<ChatGptCreditUsageEvent> events,
        ChatGptDailyTokenUsageBreakdown? dailyBreakdown = null) {
        Snapshot = snapshot;
        Events = events;
        DailyBreakdown = dailyBreakdown;
    }

    /// <summary>
    /// Gets the usage snapshot.
    /// </summary>
    public ChatGptUsageSnapshot Snapshot { get; }
    /// <summary>
    /// Gets the credit usage events.
    /// </summary>
    public IReadOnlyList<ChatGptCreditUsageEvent> Events { get; }
    /// <summary>
    /// Gets the daily token usage breakdown when requested.
    /// </summary>
    public ChatGptDailyTokenUsageBreakdown? DailyBreakdown { get; }

    /// <summary>
    /// Serializes the report to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("usage", Snapshot.ToJson());
        if (Events is not null) {
            var array = new JsonArray();
            foreach (var evt in Events) {
                array.Add(JsonValue.From(evt.ToJson()));
            }

            obj.Add("events", array);
        }

        if (DailyBreakdown is not null) {
            obj.Add("dailyBreakdown", DailyBreakdown.ToJson());
        }

        return obj;
    }
}
