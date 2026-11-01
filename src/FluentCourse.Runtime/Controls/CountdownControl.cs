using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Runtime.Controls;

/// <summary>
/// A simple MM:SS countdown timer. Counts down from <see cref="Seconds"/> once loaded
/// (when <see cref="AutoStart"/> is true). Triggers can also call Start/Pause/Reset.
/// </summary>
public sealed partial class CountdownControl : Control
{
    public static readonly DependencyProperty SecondsProperty =
        DependencyProperty.Register(nameof(Seconds), typeof(int), typeof(CountdownControl),
            new PropertyMetadata(60, OnSecondsChanged));

    public static readonly DependencyProperty AutoStartProperty =
        DependencyProperty.Register(nameof(AutoStart), typeof(bool), typeof(CountdownControl),
            new PropertyMetadata(true));

    public int Seconds { get => (int)GetValue(SecondsProperty); set => SetValue(SecondsProperty, value); }
    public bool AutoStart { get => (bool)GetValue(AutoStartProperty); set => SetValue(AutoStartProperty, value); }

    private TextBlock? _display;
    private DispatcherTimer? _timer;
    private int _remaining;

    public CountdownControl()
    {
        DefaultStyleKey = typeof(CountdownControl);
        Loaded += (_, _) => { _remaining = Seconds; UpdateDisplay(); if (AutoStart) Start(); };
        Unloaded += (_, _) => _timer?.Stop();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _display = GetTemplateChild("PART_Display") as TextBlock;
        UpdateDisplay();
    }

    public void Start()
    {
        _timer ??= CreateTimer();
        if (_remaining <= 0) _remaining = Seconds;
        _timer.Start();
    }

    public void Pause() => _timer?.Stop();

    public void Reset()
    {
        _timer?.Stop();
        _remaining = Seconds;
        UpdateDisplay();
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) =>
        {
            if (_remaining > 0) _remaining--;
            UpdateDisplay();
            if (_remaining <= 0) t.Stop();
        };
        return t;
    }

    private static void OnSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CountdownControl c) { c._remaining = (int)e.NewValue; c.UpdateDisplay(); }
    }

    private void UpdateDisplay()
    {
        if (_display is null) return;
        _display.Text = $"{_remaining / 60:00}:{_remaining % 60:00}";
    }
}
