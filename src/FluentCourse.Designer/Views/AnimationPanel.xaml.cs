using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentCourse.Designer.Views;

/// <summary>
/// Animation panel. Top: a collapsible "add animation" box for the selected element. Below: an
/// overview list of EVERY animation on the current page (entrance/exit/emphasis + button click
/// actions). Click a row to focus that element on the canvas and open a detail editor.
/// </summary>
public sealed partial class AnimationPanel : UserControl
{
    private ShellViewModel? _vm;

    private static readonly string[] EnterEffects = { "淡入", "淡入上移", "淡入下移", "淡入左移", "淡入右移", "缩放进入", "弹入" };
    private static readonly string[] ExitEffects = { "淡出", "淡出上移", "淡出下移", "淡出左移", "淡出右移", "缩放退出" };
    private static readonly string[] EmphasisEffects = { "脉冲", "抖动" };

    private readonly ObservableCollection<AnimOverviewItem> _items = new();

    public AnimationPanel()
    {
        try { InitializeComponent(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AnimationPanel] init failed: {ex}"); }

        OverviewList.ItemsSource = _items;
        foreach (var e in EnterEffects) EnterEffect.Items.Add(e);
        foreach (var e in ExitEffects) ExitEffect.Items.Add(e);
        foreach (var e in EmphasisEffects) EmphasisEffect.Items.Add(e);
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        // Selection only refreshes the "add" box — NOT the overview list, so clicking a row
        // (which focuses an element) doesn't rebuild and lose the detail panel.
        _vm.SelectionChanged += (_, _) => RefreshAddArea();
        _vm.RequestRefreshAnimations += (_, _) => BuildOverview();
        _vm.RequestRefreshTriggers += (_, _) => BuildOverview();
        _vm.DocumentChanged += (_, _) => { RefreshAddArea(); BuildOverview(); CollapseDetail(); };
        RefreshAddArea();
        BuildOverview();
    }

    public void Refresh() { RefreshAddArea(); BuildOverview(); }

    private void RefreshAddArea()
    {
        var em = _vm?.SelectedElement;
        NoElementHint.Visibility = em is null ? Visibility.Visible : Visibility.Collapsed;
        AddArea.Visibility = em is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Overview list ───────────────────────────────────────────────────────────

    private void BuildOverview()
    {
        _items.Clear();
        var doc = _vm?.Document;
        var page = doc?.SelectedPage;
        if (doc is not null && page is not null)
        {
            var pageId = page.Descriptor.Id;

            // 主体动画 (entrance / exit / emphasis), declared as named animations.
            foreach (var anim in doc.Animations)
            {
                var parts = anim.Name.Split('-');
                if (parts.Length < 3) continue;
                var elName = parts[0];
                var phase = parts[1];
                var effect = parts[^1];
                var el = _vm!.FindElementByName(elName);
                if (el is null) continue;
                string kind = phase switch { "Enter" => "enter", "Exit" => "exit", "Emphasis" => "emphasis", _ => "" };
                if (kind.Length == 0) continue;

                _items.Add(new AnimOverviewItem
                {
                    Kind = kind,
                    ElementName = elName,
                    Element = el,
                    Effect = effect,
                    FullName = anim.Name,
                    DurationMs = anim.Descriptor.DurationMs,
                    Icon = kind switch { "enter" => "▶", "exit" => "◀", _ => "✦" },
                    Title = $"{Friendly(el)} · {(kind == "enter" ? "进入" : kind == "exit" ? "退出" : "强调")}：{effect}"
                });
            }

            // 按钮控制的动画.
            foreach (var t in doc.Triggers)
            {
                if (t.On.Type != "Click") continue;
                if (!(t.Page is null || t.Page == pageId)) continue;
                var btn = _vm!.FindElementByName(t.On.Target);
                if (btn is null || btn.Type != "Button") continue;
                var info = _vm.GetButtonAction(btn);
                if (info.Kind == "none") continue;   // 无 → 不显示

                _items.Add(new AnimOverviewItem
                {
                    Kind = "button",
                    ElementName = t.On.Target!,
                    Element = btn,
                    ButtonInfo = info,
                    Icon = "👆",
                    Title = $"按钮『{BtnContent(btn)}』 · {ActionDesc(info)}"
                });
            }
        }

        OverviewEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAnimItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not AnimOverviewItem item || _vm is null) return;
        if (item.Element is not null) _vm.SelectedElement = item.Element;   // focus on canvas
        BuildDetail(item);
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void CollapseDetail() => DetailPanel.Visibility = Visibility.Collapsed;

    // ── Detail editor ───────────────────────────────────────────────────────────

    private void BuildDetail(AnimOverviewItem item)
    {
        DetailContent.Children.Clear();
        DetailContent.Children.Add(new TextBlock { Text = "动画细节", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });

        if (item.Kind == "button")
        {
            var info = item.ButtonInfo!;
            DetailContent.Children.Add(InfoRow("动作", ActionDesc(info)));
            if (info.Kind is "show" or "hide" or "toggle" or "anim")
                DetailContent.Children.Add(InfoRow("目标", TargetLabel(info.Target)));
            if (info.Kind == "anim")
                DetailContent.Children.Add(InfoRow("动画", AnimLabel(info.Animation)));

            DetailContent.Children.Add(new TextBlock
            {
                Text = "想修改？点上面这一项已选中按钮，到“属性”里的「点击动作」改。",
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush
            });

            var del = new Button { Content = "删除这个动作", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            del.Click += (_, _) =>
            {
                if (item.Element is not null) _vm!.SetButtonAction(item.Element, "none", null, "");
                CollapseDetail();
                BuildOverview();
            };
            DetailContent.Children.Add(del);
        }
        else
        {
            DetailContent.Children.Add(InfoRow("元素", Friendly(item.Element)));
            DetailContent.Children.Add(InfoRow("效果", item.Effect));

            // 时长 editable
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = "时长(ms)", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var dur = new NumberBox { Value = item.DurationMs, Minimum = 100, Maximum = 10000, SmallChange = 100, FontSize = 12 };
            dur.ValueChanged += (_, ev) =>
            {
                if (double.IsNaN(ev.NewValue) || _vm is null) return;
                item.DurationMs = ev.NewValue;
                var ad = BuildAnimationDescriptor(item.Effect, ev.NewValue);
                _vm.AddAnimation(item.FullName, ad);   // overwrites same-named animation
            };
            Grid.SetColumn(dur, 1);
            grid.Children.Add(dur);
            DetailContent.Children.Add(grid);

            var del = new Button { Content = "删除这个动画", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            del.Click += (_, _) => { DeleteBodyAnimation(item.FullName); CollapseDetail(); };
            DetailContent.Children.Add(del);
        }
    }

    private static FrameworkElement InfoRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextTertiaryBrush"] as Brush });
        var v = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    // ── Add main-body animation ─────────────────────────────────────────────────

    private void OnAddEnter(object sender, RoutedEventArgs e)
    {
        if (EnterEffect.SelectedItem is string s && s.Length > 0) AddAnimation(s, "Enter");
    }
    private void OnAddExit(object sender, RoutedEventArgs e)
    {
        if (ExitEffect.SelectedItem is string s && s.Length > 0) AddAnimation(s, "Exit");
    }
    private void OnAddEmphasis(object sender, RoutedEventArgs e)
    {
        if (EmphasisEffect.SelectedItem is string s && s.Length > 0) AddAnimation(s, "Emphasis");
    }

    private void AddAnimation(string effect, string phase)
    {
        if (_vm?.SelectedElement is null || _vm.Document is null) return;
        var em = _vm.SelectedElement;
        if (string.IsNullOrEmpty(em.Name)) _vm.AssignNameIfMissing(em);
        if (string.IsNullOrEmpty(em.Name)) return;

        var animKey = $"{em.Name}-{phase}-{effect}";
        var ad = BuildAnimationDescriptor(effect, DurationBox.Value);
        _vm.AddAnimation(animKey, ad);

        var pageId = _vm.Document.SelectedPage?.Descriptor.Id;

        // All element animations (entrance/exit/emphasis) are PPT "build steps", advanced by the
        // player's prev/next overlay in order of appearance — NOT triggered by clicking the element.
        int order = 0;
        foreach (var t in _vm.Document.Triggers)
            if (t.On.Type == "Step" && (t.Page is null || t.Page == pageId)) order++;
        var on = new TriggerEvent { Type = "Step", At = order, Target = em.Name };

        var trigger = new TriggerDescriptor
        {
            Id = $"auto-{animKey}",
            Page = pageId,
            On = on,
            Do = new List<ActionDescriptor>
            {
                new ActionDescriptor { Type = "PlayAnimation", Target = em.Name }
            }
        };
        trigger.Do[0].Parameters["animation"] = System.Text.Json.JsonSerializer.SerializeToElement(animKey);
        _vm.AddTrigger(trigger);

        BuildOverview();
    }

    private void DeleteBodyAnimation(string name)
    {
        if (_vm?.Document is null) return;
        var entry = _vm.Document.Animations.FirstOrDefault(a => a.Name == name);
        if (entry is not null) _vm.DeleteAnimation(entry);
        // remove the auto-trigger that played it
        for (int i = _vm.Document.Triggers.Count - 1; i >= 0; i--)
            if (_vm.Document.Triggers[i].Id == $"auto-{name}")
                _vm.Document.Triggers.RemoveAt(i);
        _vm.MarkDirty();
        BuildOverview();
    }

    // ── labels ──────────────────────────────────────────────────────────────────

    private static string Friendly(ElementModel? e)
    {
        if (e is null) return "元素";
        string type = e.Type switch
        {
            "Button" => "按钮", "TextBlock" => "文本", "Image" => "图片",
            "MediaPlayerElement" => "视频", "Rectangle" => "矩形", "Ellipse" => "圆形",
            "Polygon" => "形状", "Line" => "线条", "QuizControl" => "题目",
            "StackPanel" or "Grid" or "Canvas" or "Border" or "ScrollViewer" => "容器",
            _ => e.Type
        };
        var text = e.Props.TryGetValue("Text", out var t) ? t
                 : e.Props.TryGetValue("Content", out var c) ? c
                 : e.Props.TryGetValue("Question", out var q) ? q
                 : e.Name ?? "";
        if (string.IsNullOrWhiteSpace(text)) return type;
        if (text.Length > 10) text = text.Substring(0, 10) + "…";
        return $"{type}『{text}』";
    }

    private static string TargetLabel(ElementModel? e) => e is null ? "（未选目标）" : Friendly(e);

    private static string BtnContent(ElementModel b) =>
        b.Props.TryGetValue("Content", out var c) && !string.IsNullOrWhiteSpace(c) ? c : (b.Name ?? "按钮");

    private static string ActionDesc(ButtonActionInfo info) => info.Kind switch
    {
        "show"     => $"显示 {TargetLabel(info.Target)}",
        "hide"     => $"隐藏 {TargetLabel(info.Target)}",
        "toggle"   => $"点击切换显示/隐藏 {TargetLabel(info.Target)}",
        "anim"     => $"播放{AnimLabel(info.Animation)} {TargetLabel(info.Target)}",
        "prevpage" => "上一页",
        "nextpage" => "下一页",
        _          => "无"
    };

    private static string AnimLabel(string key) => key switch
    {
        "FadeIn" => "淡入", "FadeOut" => "淡出",
        "FadeInUp" => "上滑淡入", "FadeInDown" => "下滑淡入",
        "FadeInLeft" => "左滑淡入", "FadeInRight" => "右滑淡入",
        _ => key
    };

    private static AnimationDescriptor BuildAnimationDescriptor(string effect, double durationMs)
    {
        var ad = new AnimationDescriptor { DurationMs = durationMs, Easing = "EaseOutCubic" };
        switch (effect)
        {
            case "淡入":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                break;
            case "淡入上移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateY", From = 30, To = 0 });
                break;
            case "淡入下移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateY", From = -30, To = 0 });
                break;
            case "淡入左移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateX", From = 30, To = 0 });
                break;
            case "淡入右移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateX", From = -30, To = 0 });
                break;
            case "缩放进入":
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleX", From = 0.5, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleY", From = 0.5, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                break;
            case "弹入":
                ad.Easing = "EaseOutBack";
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleX", From = 0.3, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleY", From = 0.3, To = 1 });
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 0, To = 1 });
                break;
            case "淡出":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                break;
            case "淡出上移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateY", From = 0, To = -30 });
                break;
            case "淡出下移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateY", From = 0, To = 30 });
                break;
            case "淡出左移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateX", From = 0, To = -30 });
                break;
            case "淡出右移":
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateX", From = 0, To = 30 });
                break;
            case "缩放退出":
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleX", From = 1, To = 0.5 });
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleY", From = 1, To = 0.5 });
                ad.Tracks.Add(new AnimationTrack { Property = "Opacity", From = 1, To = 0 });
                break;
            case "脉冲":
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleX", From = 1, To = 1.05 });
                ad.Tracks.Add(new AnimationTrack { Property = "ScaleY", From = 1, To = 1.05 });
                ad.Repeat = 2; ad.AutoReverse = true;
                break;
            case "抖动":
                ad.Tracks.Add(new AnimationTrack { Property = "TranslateX", From = -5, To = 5 });
                ad.Repeat = 3; ad.AutoReverse = true;
                break;
        }
        return ad;
    }
}

public sealed class AnimOverviewItem
{
    public string Kind { get; set; } = "";        // enter | exit | emphasis | button
    public string ElementName { get; set; } = "";
    public ElementModel? Element { get; set; }
    public string Effect { get; set; } = "";
    public string FullName { get; set; } = "";
    public double DurationMs { get; set; }
    public ButtonActionInfo? ButtonInfo { get; set; }
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
}
