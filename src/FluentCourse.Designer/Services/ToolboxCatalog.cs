using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FluentCourse.Designer.Services;

/// <summary>A toolbox entry: a human label plus a XAML snippet to insert.</summary>
public sealed class ToolboxItem
{
    public ToolboxItem(string label, string category, string glyph, string snippet,
                       string? description = null, double defaultWidth = 120, double defaultHeight = 40,
                       string? displayLabel = null)
    {
        Label = label;
        Category = category;
        Glyph = glyph;
        Snippet = snippet;
        Description = description;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
        DisplayLabel = displayLabel ?? label;
    }

    public string Label { get; }
    public string DisplayLabel { get; }
    public string Category { get; }
    public string Glyph { get; }
    public string Snippet { get; }
    public string? Description { get; }
    public double DefaultWidth { get; }
    public double DefaultHeight { get; }
}

/// <summary>
/// The (deliberately small) set of things a teacher can drop onto a page. Aimed at non-technical
/// users: a few core elements plus four one-click question types. The actual element built for each
/// entry lives in ShellViewModel.CreateElementFromToolbox, keyed by <see cref="ToolboxItem.Label"/>.
/// </summary>
public static class ToolboxCatalog
{
    public static readonly IReadOnlyList<ToolboxItem> All = Build();

    // Categories stay in this declared order (BuildInsertStrip no longer alphabetises) and items
    // appear in the order listed. `label` is the internal key; `DisplayLabel` is the short Chinese
    // name shown on the insert strip.
    private static List<ToolboxItem> Build() => new()
    {
        // 文本
        new("Heading", "文本", "", "", "大号标题文字", 560, 72, "大标题"),
        new("Body",    "文本", "", "", "正文段落文字", 560, 44, "正文"),

        // 题目（一键生成一道完整的题，老师只改文字）
        new("QuizSingle",    "题目", "", "", "单选题：一道题 + 几个选项，选一个正确答案", 880, 380, "单选题"),
        new("QuizMultiple",  "题目", "", "", "多选题：可多选，全对才得分",                 880, 380, "多选题"),
        new("QuizFill",      "题目", "", "", "填空题：句子挖空，学生输入作答",             880, 300, "填空题"),
        new("QuizTrueFalse", "题目", "", "", "判断题：对 / 错 两个选项",                   880, 260, "判断题"),

        // 媒体
        new("Image", "媒体", "", "", "插入图片", 360, 240, "图片"),
        new("Video", "媒体", "", "", "插入视频", 480, 270, "视频"),
        new("Audio", "媒体", "", "", "插入音频 / 录音", 420, 64, "音频"),

        // 形状
        new("Rectangle",     "形状", "", "", "矩形 / 圆角框", 180, 110, "矩形"),
        new("Ellipse",       "形状", "", "", "圆形 / 椭圆",   120, 120, "圆形"),
        new("Line",          "形状", "", "", "直线 / 分隔线", 220,   2, "线条"),
        new("Triangle",      "形状", "", "", "三角形",       140, 120, "三角形"),
        new("Parallelogram", "形状", "", "", "平行四边形",   190, 110, "平行四边形"),
        new("Arrow",         "形状", "", "", "箭头",         190,  90, "箭头"),

        // 交互
        new("Button", "交互", "", "", "可点击的按钮", 150, 50, "按钮"),
        new("HyperlinkButton", "交互", "", "", "点击打开网页 / 链接", 160, 44, "链接按钮"),

        // 其它
        new("Table",     "其它", "", "", "表格（行列单元格）", 600, 280, "表格"),
        new("BarChart",  "其它", "", "", "静态柱状图模板",       640, 360, "柱状图"),
        new("FlowSmart", "其它", "", "", "流程图 / SmartArt 模板", 680, 220, "流程图"),
        new("Countdown", "其它", "", "", "倒计时 / 计时器",     240, 96,  "倒计时"),
        new("Formula",   "其它", "", "", "数学公式（点按钮即可，无需手写代码）", 360, 120, "公式"),
    };
}
