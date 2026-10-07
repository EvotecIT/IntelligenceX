using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>A provider daily measurement attributed to a feature, trigger, model and surface.</summary>
public sealed class ChatGptDailyUsageAttribution {
    internal ChatGptDailyUsageAttribution(JsonObject obj) {
        ThreadSource = obj.GetString("thread_source"); TurnTrigger = obj.GetString("turn_trigger");
        Model = obj.GetString("model"); Surface = obj.GetString("surface"); Value = ChatGptAnalyticsJson.Number(obj, "value");
    }
    /// <summary>Provider feature key; for example user, subagent or memory_consolidation.</summary>
    public string? ThreadSource { get; }
    /// <summary>Structured trigger, such as composer or automation_heartbeat_scheduled.</summary>
    public string? TurnTrigger { get; }
    /// <summary>Provider model identifier.</summary>
    public string? Model { get; }
    /// <summary>Provider product surface.</summary>
    public string? Surface { get; }
    /// <summary>Reported value in the enclosing daily response's units, not necessarily tokens.</summary>
    public double? Value { get; }
}

/// <summary>A model/speed daily measurement in the enclosing response's units.</summary>
public sealed class ChatGptDailyModelUsage {
    internal ChatGptDailyModelUsage(JsonObject obj) {
        Model = obj.GetString("model"); Speed = obj.GetString("speed"); Value = ChatGptAnalyticsJson.Number(obj, "credits");
    }
    /// <summary>Provider model identifier.</summary>
    public string? Model { get; }
    /// <summary>Provider speed setting.</summary>
    public string? Speed { get; }
    /// <summary>Provider credits field interpreted using enclosing units; not necessarily a credit debit.</summary>
    public double? Value { get; }
}
