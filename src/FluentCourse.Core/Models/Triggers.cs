using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCourse.Core.Models;

/// <summary>
/// One trigger = one (event source, event type) → list of actions.
/// When <see cref="Steps"/> is non-empty, each activation advances to the next step
/// and executes that step's actions. After the last step it wraps back to 0 (or stops
/// if <see cref="StopAfterLastStep"/> is true).
/// </summary>
public sealed class TriggerDescriptor
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("page")] public string? Page { get; set; }
    [JsonPropertyName("on")] public TriggerEvent On { get; set; } = new();

    /// <summary>Simple single-step actions. Used when <see cref="Steps"/> is empty.</summary>
    [JsonPropertyName("do")] public List<ActionDescriptor> Do { get; set; } = new();

    /// <summary>
    /// Multi-step actions. Each element is one "click" worth of actions.
    /// Takes priority over <see cref="Do"/> when non-empty.
    /// </summary>
    [JsonPropertyName("steps")] public List<List<ActionDescriptor>> Steps { get; set; } = new();

    /// <summary>When true, the trigger does nothing after the last step (no wrap-around).</summary>
    [JsonPropertyName("stopAfterLastStep")] public bool StopAfterLastStep { get; set; } = true;

    [JsonPropertyName("condition")] public string? Condition { get; set; }
}

public sealed class TriggerEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "PageLoaded";
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("at")] public double? At { get; set; }
}

/// <summary>
/// Polymorphic action. Discriminator = "type". Extra fields land in <see cref="Parameters"/>.
/// </summary>
public sealed class ActionDescriptor
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("target")] public string? Target { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();

    public string? GetString(string key) =>
        Parameters.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    public double? GetDouble(string key) =>
        Parameters.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;

    public bool? GetBool(string key) => Parameters.TryGetValue(key, out var el) ? el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    } : null;
}
