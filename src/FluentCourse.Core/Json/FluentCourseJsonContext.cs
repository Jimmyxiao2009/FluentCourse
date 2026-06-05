using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCourse.Core.Models;

namespace FluentCourse.Core.Json;

/// <summary>
/// AOT-friendly JSON context. All polymorphic shapes go through extension data,
/// so we don't need converter hooks beyond the defaults.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CourseManifest))]
[JsonSerializable(typeof(PageDescriptor))]
[JsonSerializable(typeof(StageDescriptor))]
[JsonSerializable(typeof(AssetDescriptor))]
[JsonSerializable(typeof(TriggerDescriptor))]
[JsonSerializable(typeof(ActionDescriptor))]
[JsonSerializable(typeof(AnimationDescriptor))]
[JsonSerializable(typeof(AnimationTrack))]
[JsonSerializable(typeof(List<TriggerDescriptor>))]
[JsonSerializable(typeof(List<List<ActionDescriptor>>))]
[JsonSerializable(typeof(Dictionary<string, AnimationDescriptor>))]
public sealed partial class FluentCourseJsonContext : JsonSerializerContext;
