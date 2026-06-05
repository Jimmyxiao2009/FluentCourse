using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCourse.Runtime.Controls;

/// <summary>
/// Reveals children one at a time on Next(). The runtime trigger system can call
/// <see cref="Next"/> via a <c>SetProperty</c> action targeting CurrentStep.
/// </summary>
[ContentProperty(Name = nameof(Items))]
public sealed partial class StepRevealControl : ItemsControl
{
    public static readonly DependencyProperty CurrentStepProperty =
        DependencyProperty.Register(nameof(CurrentStep), typeof(int), typeof(StepRevealControl),
            new PropertyMetadata(0, OnCurrentStepChanged));

    public int CurrentStep { get => (int)GetValue(CurrentStepProperty); set => SetValue(CurrentStepProperty, value); }

    public StepRevealControl()
    {
        Loaded += (_, _) => SyncVisibility();
    }

    public void Next()
    {
        if (Items is { } items && CurrentStep < items.Count) CurrentStep++;
    }

    public void Previous()
    {
        if (CurrentStep > 0) CurrentStep--;
    }

    public void Reset() => CurrentStep = 0;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is FrameworkElement fe)
        {
            fe.Opacity = 0;
            fe.Visibility = Visibility.Collapsed;
        }
    }

    private static void OnCurrentStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StepRevealControl s) s.SyncVisibility();
    }

    private void SyncVisibility()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (ContainerFromIndex(i) is FrameworkElement fe)
            {
                if (i < CurrentStep && fe.Visibility != Visibility.Visible)
                    RevealAnimated(fe);
                else if (i >= CurrentStep)
                {
                    fe.Visibility = Visibility.Collapsed;
                    fe.Opacity = 0;
                }
            }
        }
    }

    private static void RevealAnimated(FrameworkElement fe)
    {
        fe.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, fe);
        Storyboard.SetTargetProperty(anim, "(UIElement.Opacity)");
        sb.Children.Add(anim);
        sb.Begin();
    }
}
