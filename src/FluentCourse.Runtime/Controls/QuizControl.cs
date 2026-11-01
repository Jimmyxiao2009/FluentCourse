using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FluentCourse.Runtime.Controls;

public enum QuizMode { Single, Multiple, TrueFalse, FillInBlank }
public enum QuizResult { Pending, Correct, Incorrect }
public enum ScoreStrategy { AllOrNothing, PartialCredit, Proportional }

// ── QuizChoice ──

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
    public static readonly DependencyProperty ExplanationProperty =
        DependencyProperty.Register(nameof(Explanation), typeof(string), typeof(QuizChoice), new PropertyMetadata(""));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsAnswer { get => (bool)GetValue(IsAnswerProperty); set => SetValue(IsAnswerProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public string Feedback { get => (string)GetValue(FeedbackProperty); set => SetValue(FeedbackProperty, value); }
    public string Explanation { get => (string)GetValue(ExplanationProperty); set => SetValue(ExplanationProperty, value); }
}

// ── QuizBlank (FillInBlank) ──

[ContentProperty(Name = nameof(Pattern))]
public sealed partial class QuizBlank : DependencyObject
{
    public static readonly DependencyProperty PatternProperty =
        DependencyProperty.Register(nameof(Pattern), typeof(string), typeof(QuizBlank), new PropertyMetadata("*"));
    public static readonly DependencyProperty CaseSensitiveProperty =
        DependencyProperty.Register(nameof(CaseSensitive), typeof(bool), typeof(QuizBlank), new PropertyMetadata(false));
    public static readonly DependencyProperty ExplanationProperty =
        DependencyProperty.Register(nameof(Explanation), typeof(string), typeof(QuizBlank), new PropertyMetadata(""));
    public static readonly DependencyProperty UserAnswerProperty =
        DependencyProperty.Register(nameof(UserAnswer), typeof(string), typeof(QuizBlank), new PropertyMetadata(""));

    public string Pattern { get => (string)GetValue(PatternProperty); set => SetValue(PatternProperty, value); }
    public bool CaseSensitive { get => (bool)GetValue(CaseSensitiveProperty); set => SetValue(CaseSensitiveProperty, value); }
    public string Explanation { get => (string)GetValue(ExplanationProperty); set => SetValue(ExplanationProperty, value); }
    public string UserAnswer { get => (string)GetValue(UserAnswerProperty); set => SetValue(UserAnswerProperty, value); }
}

// ── QuizControl ──

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
    public static readonly DependencyProperty BlanksProperty =
        DependencyProperty.Register(nameof(Blanks), typeof(ObservableCollection<QuizBlank>), typeof(QuizControl),
            new PropertyMetadata(null));
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.Register(nameof(Result), typeof(QuizResult), typeof(QuizControl),
            new PropertyMetadata(QuizResult.Pending, OnResultChanged));
    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(QuizControl), new PropertyMetadata(0.0));
    public static readonly DependencyProperty ScoreStrategyProperty =
        DependencyProperty.Register(nameof(ScoreStrategy), typeof(ScoreStrategy), typeof(QuizControl),
            new PropertyMetadata(ScoreStrategy.AllOrNothing));
    public static readonly DependencyProperty ShowFeedbackProperty =
        DependencyProperty.Register(nameof(ShowFeedback), typeof(bool), typeof(QuizControl), new PropertyMetadata(true));
    public static readonly DependencyProperty AllowRetryProperty =
        DependencyProperty.Register(nameof(AllowRetry), typeof(bool), typeof(QuizControl), new PropertyMetadata(true));

    public string Question { get => (string)GetValue(QuestionProperty); set => SetValue(QuestionProperty, value); }
    public QuizMode Mode { get => (QuizMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public ObservableCollection<QuizChoice> Choices => (ObservableCollection<QuizChoice>)GetValue(ChoicesProperty);
    public ObservableCollection<QuizBlank> Blanks => (ObservableCollection<QuizBlank>)GetValue(BlanksProperty);
    public QuizResult Result { get => (QuizResult)GetValue(ResultProperty); private set => SetValue(ResultProperty, value); }
    public double Score { get => (double)GetValue(ScoreProperty); private set => SetValue(ScoreProperty, value); }
    public ScoreStrategy ScoreStrategy { get => (ScoreStrategy)GetValue(ScoreStrategyProperty); set => SetValue(ScoreStrategyProperty, value); }
    public bool ShowFeedback { get => (bool)GetValue(ShowFeedbackProperty); set => SetValue(ShowFeedbackProperty, value); }
    public bool AllowRetry { get => (bool)GetValue(AllowRetryProperty); set => SetValue(AllowRetryProperty, value); }

    public event EventHandler<QuizSubmittedEventArgs>? Submitted;

    private ItemsControl? _choicesHost;
    private ItemsControl? _blanksHost;
    private Button? _submitButton;
    private Button? _retryButton;
    private TextBlock? _feedbackBlock;
    private TextBlock? _analysisBlock;
    private Border? _analysisHost;
    private Border? _feedbackHost;

    // 单选模式互斥用:每个 QuizChoice 的 IsSelected 属性变更回调令牌
    private readonly Dictionary<QuizChoice, long> _selectionCallbacks = new();
    private bool _syncingSelection;

    public QuizControl()
    {
        DefaultStyleKey = typeof(QuizControl);
        var choices = new ObservableCollection<QuizChoice>();
        SetValue(ChoicesProperty, choices);
        SetValue(BlanksProperty, new ObservableCollection<QuizBlank>());
        choices.CollectionChanged += OnChoicesCollectionChanged;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_submitButton is not null) _submitButton.Click -= OnSubmit;
        if (_retryButton is not null) _retryButton.Click -= OnRetry;

        _choicesHost = GetTemplateChild("PART_Choices") as ItemsControl;
        _blanksHost  = GetTemplateChild("PART_Blanks") as ItemsControl;
        _submitButton = GetTemplateChild("PART_Submit") as Button;
        _retryButton = GetTemplateChild("PART_Retry") as Button;
        _feedbackBlock = GetTemplateChild("PART_Feedback") as TextBlock;
        _analysisBlock = GetTemplateChild("PART_Analysis") as TextBlock;
        _analysisHost  = GetTemplateChild("PART_AnalysisHost") as Border;
        _feedbackHost  = GetTemplateChild("PART_FeedbackHost") as Border;

        if (_submitButton is not null) _submitButton.Click += OnSubmit;
        if (_retryButton is not null) _retryButton.Click += OnRetry;
    }

    // ── 单选约束(Single/TrueFalse 模式) ──

    private void OnChoicesCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var item in e.OldItems.OfType<QuizChoice>())
                UnhookChoice(item);

        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            foreach (var (choice, token) in _selectionCallbacks.ToList())
                choice.UnregisterPropertyChangedCallback(QuizChoice.IsSelectedProperty, token);
            _selectionCallbacks.Clear();
        }

        if (e.NewItems is not null)
            foreach (var item in e.NewItems.OfType<QuizChoice>())
                HookChoice(item);
    }

    private void HookChoice(QuizChoice choice)
    {
        if (_selectionCallbacks.ContainsKey(choice)) return;
        var token = choice.RegisterPropertyChangedCallback(QuizChoice.IsSelectedProperty, OnChoiceSelectedChanged);
        _selectionCallbacks[choice] = token;
    }

    private void UnhookChoice(QuizChoice choice)
    {
        if (_selectionCallbacks.TryGetValue(choice, out var token))
        {
            choice.UnregisterPropertyChangedCallback(QuizChoice.IsSelectedProperty, token);
            _selectionCallbacks.Remove(choice);
        }
    }

    private void OnChoiceSelectedChanged(DependencyObject d, DependencyProperty dp)
    {
        // Single/TrueFalse 是单选题:勾选某项时取消其它项,避免 CheckBox 模板可多选
        if (_syncingSelection) return;
        if (Mode is not (QuizMode.Single or QuizMode.TrueFalse)) return;
        if (d is not QuizChoice changed || !changed.IsSelected) return;

        _syncingSelection = true;
        try
        {
            foreach (var c in Choices)
                if (!ReferenceEquals(c, changed) && c.IsSelected)
                    c.IsSelected = false;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // ── 提交 / 重做(公开方法供 ActionExecutor 程序化调用) ──

    /// <summary>程序化提交(等同点击「提交」按钮)。已提交未重做时为空操作。</summary>
    public void Submit()
    {
        if (Result != QuizResult.Pending) return;
        OnSubmit(this, new RoutedEventArgs());
    }

    /// <summary>程序化重做(等同点击「重做」按钮)。</summary>
    public void Reset() => OnRetry(this, new RoutedEventArgs());

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        bool allCorrect;
        double score;

        if (Mode == QuizMode.FillInBlank)
        {
            (allCorrect, score) = GradeFillInBlank();
        }
        else
        {
            var picks = Choices.Where(c => c.IsSelected).ToList();
            if (picks.Count == 0)
            {
                // 未选任何项:在反馈区显示中文提示而非静默返回
                if (_feedbackBlock is not null) _feedbackBlock.Text = "请先选择答案";
                if (_feedbackHost is not null) _feedbackHost.Visibility = Visibility.Visible;
                return;
            }
            // 清掉可能残留的「请先选择答案」提示(本地值),交回 VSM 控制
            if (_feedbackHost is not null) _feedbackHost.Visibility = Visibility.Collapsed;
            (allCorrect, score) = GradeChoice(picks);
        }

        Result = allCorrect ? QuizResult.Correct : QuizResult.Incorrect;
        Score = score;

        if (_feedbackBlock is not null && ShowFeedback)
        {
            _feedbackBlock.Text = allCorrect
                ? "正确！"
                : "再想想 — 答案不完全正确。";
        }

        if (_analysisBlock is not null && ShowFeedback)
        {
            var explanations = Mode == QuizMode.FillInBlank
                ? Blanks.Where(b => !string.IsNullOrEmpty(b.Explanation)).Select(b => b.Explanation).ToList()
                : Choices.Where(c => c.IsAnswer && !string.IsNullOrEmpty(c.Explanation))
                         .Select(c => c.Explanation).ToList();
            _analysisBlock.Text = explanations.Count > 0
                ? string.Join("\n\n", explanations)
                : "（本题暂无解析）";
            if (_analysisHost is not null) _analysisHost.Visibility = Visibility.Visible;
        }

        if (_retryButton is not null && AllowRetry)
            _retryButton.Visibility = Visibility.Visible;
        if (_submitButton is not null)
            _submitButton.IsEnabled = false;

        Submitted?.Invoke(this, new QuizSubmittedEventArgs(allCorrect, Score));
    }

    private (bool allCorrect, double score) GradeChoice(List<QuizChoice> picks)
    {
        var correctSet = Choices.Where(c => c.IsAnswer).ToHashSet();
        var pickedSet  = picks.ToHashSet();
        var allCorrect = correctSet.SetEquals(pickedSet);

        double score;
        switch (ScoreStrategy)
        {
            case ScoreStrategy.AllOrNothing:
                score = allCorrect ? 1.0 : 0.0;
                break;
            case ScoreStrategy.Proportional:
                score = correctSet.Count > 0
                    ? (double)correctSet.Intersect(pickedSet).Count() / correctSet.Count
                    : 0.0;
                break;
            case ScoreStrategy.PartialCredit:
                if (correctSet.Count == 0) { score = 0.0; break; }
                int correctPicks = correctSet.Intersect(pickedSet).Count();
                int wrongPicks   = pickedSet.Count(p => !correctSet.Contains(p));
                double raw = (correctPicks - wrongPicks) / (double)correctSet.Count;
                score = Math.Max(0.0, raw);
                break;
            default:
                score = allCorrect ? 1.0 : 0.0;
                break;
        }

        return (allCorrect, score);
    }

    private (bool allCorrect, double score) GradeFillInBlank()
    {
        if (Blanks.Count == 0) return (true, 1.0);
        int matched = 0;
        foreach (var blank in Blanks)
        {
            if (IsBlankCorrect(blank)) matched++;
        }
        bool all = matched == Blanks.Count;
        double s = (double)matched / Blanks.Count;
        return (all, s);
    }

    private static bool IsBlankCorrect(QuizBlank blank)
    {
        if (blank.Pattern == "*") return !string.IsNullOrWhiteSpace(blank.UserAnswer);
        var options = blank.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        try
        {
            return Regex.IsMatch(blank.UserAnswer ?? "", blank.Pattern, options);
        }
        catch
        {
            return string.Equals(blank.UserAnswer ?? "", blank.Pattern,
                blank.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        foreach (var c in Choices) c.IsSelected = false;
        foreach (var b in Blanks) b.UserAnswer = "";
        Result = QuizResult.Pending;
        Score = 0;
        if (_feedbackBlock is not null) _feedbackBlock.Text = "";
        if (_analysisBlock is not null)
        {
            _analysisBlock.Text = "";
            if (_analysisHost is not null) _analysisHost.Visibility = Visibility.Collapsed;
        }
        if (_retryButton is not null) _retryButton.Visibility = Visibility.Collapsed;
        if (_submitButton is not null) _submitButton.IsEnabled = true;
        if (_feedbackHost is not null) _feedbackHost.Visibility = Visibility.Collapsed;
    }

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QuizControl q)
        {
            // ShowFeedback=false 时不显示反馈框(Correct/Incorrect 状态会把
            // PART_FeedbackHost 设为可见,因此停留在 Pending 状态)
            var state = q.Result switch
            {
                QuizResult.Correct when q.ShowFeedback => "Correct",
                QuizResult.Incorrect when q.ShowFeedback => "Incorrect",
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
