using System.Text.Json.Serialization;

namespace FluentCourse.Core.Models;

/// <summary>Root manifest for a .fcourse package. One per archive.</summary>
public sealed class CourseManifest
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("title")] public string Title { get; set; } = "Untitled";
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("language")] public string Language { get; set; } = "zh-CN";

    [JsonPropertyName("stage")] public StageDescriptor Stage { get; set; } = new();
    [JsonPropertyName("pages")] public List<PageDescriptor> Pages { get; set; } = new();
    [JsonPropertyName("triggers")] public List<TriggerDescriptor> Triggers { get; set; } = new();
    [JsonPropertyName("animations")] public Dictionary<string, AnimationDescriptor> Animations { get; set; } = new();
    [JsonPropertyName("assets")] public List<AssetDescriptor> Assets { get; set; } = new();

    [JsonIgnore] public DiagnosticCollection Diagnostics { get; set; } = new();

    public void AddWarning(string code, string message, string? location = null)
    {
        Diagnostics ??= new();
        Diagnostics.Warnings.Add(new DiagnosticEntry { Code = code, Message = message, Location = location });
    }

    public void AddError(string code, string message, string? location = null)
    {
        Diagnostics ??= new();
        Diagnostics.Errors.Add(new DiagnosticEntry { Code = code, Message = message, Location = location });
    }
}

public sealed class StageDescriptor
{
    [JsonPropertyName("designWidth")] public double DesignWidth { get; set; } = 1920;
    [JsonPropertyName("designHeight")] public double DesignHeight { get; set; } = 1080;
    [JsonPropertyName("scaleMode")] public string ScaleMode { get; set; } = "Uniform";
    [JsonPropertyName("background")] public string Background { get; set; } = "#FF000000";
    [JsonPropertyName("theme")] public string Theme { get; set; } = "System";
}

public sealed class PageDescriptor
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("transition")] public string Transition { get; set; } = "Fade";
    [JsonPropertyName("transitionDurationMs")] public int? TransitionDurationMs { get; set; }
    [JsonPropertyName("states")] public List<string> States { get; set; } = new();
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("section")] public string? Section { get; set; }
    [JsonPropertyName("background")] public string? Background { get; set; }
}

public sealed class AssetDescriptor
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "binary";
    [JsonPropertyName("fit")] public string Fit { get; set; } = "stretch";
    [JsonPropertyName("placeholderColor")] public string? PlaceholderColor { get; set; }
    [JsonPropertyName("preload")] public string Preload { get; set; } = "eager";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}

public sealed class DiagnosticEntry
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("location")] public string? Location { get; set; }
}

public sealed class DiagnosticCollection
{
    [JsonPropertyName("warnings")] public List<DiagnosticEntry> Warnings { get; set; } = new();
    [JsonPropertyName("errors")] public List<DiagnosticEntry> Errors { get; set; } = new();
}
