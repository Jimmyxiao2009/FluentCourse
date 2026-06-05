using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FluentCourse.Runtime.Controls;

public enum QuizMode { Single, Multiple, TrueFalse }
public enum QuizResult { Pending, Correct, Incorrect }

/// <summary>One choice in a quiz. <see cref="IsAnswer"/> defines the scoring key.</summary>
[ContentProperty(Name = nameof(Text))]
public sealed partial class QuizChoice : DependencyObject
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(QuizChoice), new PropertyMetadata(""));
    public static readonly DependencyProperty IsAnswerProperty =
        DependencyProperty.Register(nameof(IsAnswer), typeof(bool), typeof(QuizChoice), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(QuizChoice), new PropertyMetadata(false));
    public static readonly DependencyProperty FeedbackProperty =
        DependencyProperty.Register(nameof(Feedback), typeof(string), typeof(QuizChoice), new PropertyMetadata(""));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsAnswer { get => (bool)GetValue(IsAnswerProperty); set => SetValue(IsAnswerProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public string Feedback { get => (string)GetValue(FeedbackProperty); set => SetValue(FeedbackProperty, value); }
}

[ContentProperty(Name = nameof(Choices))]
public sealed partial class QuizControl : Control
{
    public static readonly DependencyProperty QuestionProperty =
        DependencyProperty.Register(nameof(Question), typeof(string), typeof(QuizControl), new PropertyMetadata(""));
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(QuizMode), typeof(QuizControl), new PropertyMetadata(QuizMode.Single));
    public static readonly DependencyProperty ChoicesProperty =
        DependencyProperty.Register(nameof(Choices), typeof(ObservableCollection<QuizChoice>), typeof(QuizControl),
            new PropertyMetadata(null));
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.Register(nameof(Result), typeof(QuizResult), typeof(QuizControl),
            new PropertyMetadata(QuizResult.Pending, OnResultChanged));
    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(QuizControl), new PropertyMetadata(0.0));
    public static readonly DependencyProperty ShowFeedbackProperty =
        DependencyProperty.Register(nameof(ShowFeedback), typeof(bool), typeof(QuizControl),
            new PropertyMetadata(true));
    public static readonly DependencyProperty AllowRetryProperty =
        DependencyProperty.Register(nameof(AllowRetry), typeof(bool), typeof(QuizControl), new PropertyMetadata(true));

    public string Question { get => (string)GetValue(QuestionProperty); set => SetValue(QuestionProperty, value); }
    public QuizMode Mode { get => (QuizMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public ObservableCollection<QuizChoice> Choices => (ObservableCollection<QuizChoice>)GetValue(ChoicesProperty);
    public QuizResult Result { get => (QuizResult)GetValue(ResultProperty); private set => SetValue(ResultProperty, value); }
    public double Score { get => (double)GetValue(ScoreProperty); private set => SetValue(ScoreProperty, value); }
    public bool ShowFeedback { get => (bool)GetValue(ShowFeedbackProperty); set => SetValue(ShowFeedbackProperty, value); }
    public bool AllowRetry { get => (bool)GetValue(AllowRetryProperty); set => SetValue(AllowRetryProperty, value); }

    public event EventHandler<QuizSubmittedEventArgs>? Submitted;

    private ItemsControl? _choicesHost;
    private Button? _submitButton;
    private Button? _retryButton;
    private TextBlock? _feedbackBlock;

    public QuizControl()
    {
        DefaultStyleKey = typeof(QuizControl);
        SetValue(ChoicesProperty, new ObservableCollection<QuizChoice>());
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_submitButton is not null) _submitButton.Click -= OnSubmit;
        if (_retryButton is not null) _retryButton.Click -= OnRetry;

        _choicesHost = GetTemplateChild("PART_Choices") as ItemsControl;
        _submitButton = GetTemplateChild("PART_Submit") as Button;
        _retryButton = GetTemplateChild("PART_Retry") as Button;
        _feedbackBlock = GetTemplateChild("PART_Feedback") as TextBlock;

        if (_submitButton is not null) _submitButton.Click += OnSubmit;
        if (_retryButton is not null) _retryButton.Click += OnRetry;
    }

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        var picks = Choices.Where(c => c.IsSelected).ToList();
        if (picks.Count == 0) return;

        var correctSet = Choices.Where(c => c.IsAnswer).ToHashSet();
        var pickedSet = picks.ToHashSet();
        var allCorrect = correctSet.SetEquals(pickedSet);

        Result = allCorrect ? QuizResult.Correct : QuizResult.Incorrect;
        Score = allCorrect ? 1.0 : 0.0;

        if (_feedbackBlock is not null && ShowFeedback)
        {
            _feedbackBlock.Text = allCorrect
                ? string.Join(' ', picks.Where(p => !string.IsNullOrEmpty(p.Feedback)).Select(p => p.Feedback))
                : "再想想 — 答案不完全正确。";
        }

        if (_retryButton is not null && AllowRetry)
            _retryButton.Visibility = Visibility.Visible;
        if (_submitButton is not null)
            _submitButton.IsEnabled = false;

        Submitted?.Invoke(this, new QuizSubmittedEventArgs(allCorrect, Score));
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        foreach (var c in Choices) c.IsSelected = false;
        Result = QuizResult.Pending;
        Score = 0;
        if (_feedbackBlock is not null) _feedbackBlock.Text = "";
        if (_retryButton is not null) _retryButton.Visibility = Visibility.Collapsed;
        if (_submitButton is not null) _submitButton.IsEnabled = true;
    }

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QuizControl q)
        {
            var state = q.Result switch
            {
                QuizResult.Correct => "Correct",
                QuizResult.Incorrect => "Incorrect",
                _ => "Pending"
            };
            VisualStateManager.GoToState(q, state, useTransitions: true);
        }
    }
}

public sealed class QuizSubmittedEventArgs(bool isCorrect, double score) : EventArgs
{
    public bool IsCorrect { get; } = isCorrect;
    public double Score { get; } = score;
}
