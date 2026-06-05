using System.Text.Json.Serialization;

namespace FluentCourse.Core.Models;

public sealed class AnimationDescriptor
{
    [JsonPropertyName("duration")] public double DurationMs { get; set; } = 400;
    [JsonPropertyName("delay")] public double DelayMs { get; set; }
    [JsonPropertyName("easing")] public string Easing { get; set; } = "EaseOutCubic";
    [JsonPropertyName("repeat")] public int Repeat { get; set; } = 1;
    [JsonPropertyName("autoReverse")] public bool AutoReverse { get; set; }
    [JsonPropertyName("tracks")] public List<AnimationTrack> Tracks { get; set; } = new();
}

public sealed class AnimationTrack
{
    /// <summary>One of: Opacity, TranslateX, TranslateY, ScaleX, ScaleY, Rotation, Color.</summary>
    [JsonPropertyName("property")] public string Property { get; set; } = "Opacity";
    [JsonPropertyName("from")] public double? From { get; set; }
    [JsonPropertyName("to")] public double? To { get; set; }

    [JsonPropertyName("fromColor")] public string? FromColor { get; set; }
    [JsonPropertyName("toColor")] public string? ToColor { get; set; }
}
