using System.Text.Json;
using FluentCourse.Core.Models;

namespace FluentCourse.Designer.Views;

/// <summary>
/// Metadata for each known action type: which extra parameters it expects.
/// </summary>
internal static class ActionMeta
{
    public static readonly IReadOnlyDictionary<string, (string Label, string Key, string Type)[]> Schema = new Dictionary<string, (string, string, string)[]>
    {
        ["GoToPage"]       = new[] { ("跳转页面ID", "page", "string") },
        ["NextPage"]       = Array.Empty<(string, string, string)>(),
        ["PreviousPage"]   = Array.Empty<(string, string, string)>(),
        ["FirstPage"]      = Array.Empty<(string, string, string)>(),
        ["LastPage"]       = Array.Empty<(string, string, string)>(),
        ["GoBack"]         = Array.Empty<(string, string, string)>(),
        ["PlayAnimation"]  = new[] { ("动画名", "animation", "string") },
        ["StopAnimation"]  = new[] { ("动画名", "animation", "string") },
        ["PauseAnimation"] = new[] { ("动画名", "animation", "string") },
        ["ResumeAnimation"]= new[] { ("动画名", "animation", "string") },
        ["GoToState"]      = new[] { ("状态名", "state", "string") },
        ["ToggleState"]    = new[] { ("状态名", "state", "string") },
        ["PlayMedia"]      = Array.Empty<(string, string, string)>(),
        ["PauseMedia"]     = Array.Empty<(string, string, string)>(),
        ["StopMedia"]      = Array.Empty<(string, string, string)>(),
        ["SeekMedia"]      = new[] { ("目标秒数", "to", "number") },
        ["SetVolume"]      = new[] { ("音量 0-1", "value", "number") },
        ["Mute"]           = new[] { ("静音", "value", "bool") },
        ["NextStep"]       = Array.Empty<(string, string, string)>(),
        ["PreviousStep"]   = Array.Empty<(string, string, string)>(),
        ["ResetSteps"]     = Array.Empty<(string, string, string)>(),
        ["GoToStep"]       = new[] { ("步数", "step", "number") },
        ["SubmitQuiz"]     = Array.Empty<(string, string, string)>(),
        ["ResetQuiz"]      = Array.Empty<(string, string, string)>(),
        ["ShowFeedback"]   = Array.Empty<(string, string, string)>(),
        ["HideFeedback"]   = Array.Empty<(string, string, string)>(),
        ["SetVariable"]    = new[] { ("变量名", "name", "string"), ("值", "value", "string") },
        ["IncrementVariable"] = new[] { ("变量名", "name", "string"), ("增量", "by", "number") },
        ["ToggleVariable"]    = new[] { ("变量名", "name", "string") },
        ["OpenHyperlink"]     = new[] { ("URL", "url", "string") },
        ["OpenFile"]          = new[] { ("路径", "path", "string") },
        ["ExitPresentation"]  = Array.Empty<(string, string, string)>(),
        ["ToggleFullscreen"]  = Array.Empty<(string, string, string)>(),
        ["ShowToast"]         = new[] { ("标题", "title", "string"), ("消息", "message", "string") },
        ["SetProperty"]       = new[] { ("属性名", "property", "string"), ("值", "value", "string") },
    };

    public static (string Label, string Key, string Type)[] GetParams(string type) =>
        Schema.TryGetValue(type, out var p) ? p : Array.Empty<(string, string, string)>();

    public static string[] AllActionTypes => Schema.Keys.OrderBy(k => k).ToArray();

    public static readonly string[] AllEventTypes = new[]
    {
        "PageLoaded", "Click", "DoubleClick", "ValueChanged", "Key", "Time",
        "PointerEnter", "PointerExit", "PointerPressed", "PointerReleased",
        "Hold", "SwipeLeft", "SwipeRight", "SwipeUp", "SwipeDown",
        "Drag", "Focus", "LostFocus", "Scroll", "Drop", "MediaEnded"
    };
}

/// <summary>
/// Helper to pack/unpack a TriggerDescriptor into a flat UI model for binding.
/// </summary>
internal sealed class TriggerEditModel
{
    public TriggerDescriptor Descriptor { get; }

    public TriggerEditModel(TriggerDescriptor d) { Descriptor = d; }

    public string Id { get => Descriptor.Id; set => Descriptor.Id = value; }
    public string? Page { get => Descriptor.Page; set => Descriptor.Page = value; }
    public string EventType { get => Descriptor.On.Type; set => Descriptor.On.Type = value; }
    public string? Target { get => Descriptor.On.Target; set => Descriptor.On.Target = value; }
    public string? Key { get => Descriptor.On.Key; set => Descriptor.On.Key = value; }
    public double? At { get => Descriptor.On.At; set => Descriptor.On.At = value; }
    public int? TapCount { get => Descriptor.On.TapCount; set => Descriptor.On.TapCount = value; }
    public string? ModifiersStr
    {
        get => Descriptor.On.Modifiers is { Count: > 0 } m ? string.Join(",", m) : null;
        set => Descriptor.On.Modifiers = string.IsNullOrWhiteSpace(value) ? null : value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }
    public int? DebounceMs { get => Descriptor.On.DebounceMs; set => Descriptor.On.DebounceMs = value; }
    public bool Once { get => Descriptor.On.Once; set => Descriptor.On.Once = value; }
    public string? ComparedTo { get => Descriptor.On.ComparedTo; set => Descriptor.On.ComparedTo = value; }
    public double? Threshold { get => Descriptor.On.Threshold; set => Descriptor.On.Threshold = value; }
    public bool StopAfterLastStep { get => Descriptor.StopAfterLastStep; set => Descriptor.StopAfterLastStep = value; }
    public string? Condition { get => Descriptor.Condition; set => Descriptor.Condition = value; }

    public bool UseSteps => Descriptor.Steps.Count > 0;

    public string Summary
    {
        get
        {
            var ev = $"{EventType}" + (Target is { } t ? $" → {t}" : "");
            var actCount = UseSteps ? $"{Descriptor.Steps.Sum(s => s.Count)} 动作 ({Descriptor.Steps.Count} 步)"
                                    : $"{Descriptor.Do.Count} 动作";
            return $"{Id} | {Page} | {ev} | {actCount}";
        }
    }
}
