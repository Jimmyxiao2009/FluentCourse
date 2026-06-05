using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FluentCourse.Designer.Services;

/// <summary>A toolbox entry: a human label plus a XAML snippet to insert.</summary>
public sealed class ToolboxItem
{
    public ToolboxItem(string label, string category, string glyph, string snippet, string? description = null)
    {
        Label = label;
        Category = category;
        Glyph = glyph;
        Snippet = snippet;
        Description = description;
    }

    public string Label { get; }
    public string Category { get; }
    public string Glyph { get; }
    public string Snippet { get; }
    public string? Description { get; }
}

/// <summary>
/// Catalogue of every element the editor can drop onto a page. The snippet is what gets
/// inserted into the page XAML; the editor never constructs UI elements in code so that
/// the source XAML stays the source of truth.
/// </summary>
public static class ToolboxCatalog
{
    public static readonly IReadOnlyList<ToolboxItem> All = Build();

    private static List<ToolboxItem> Build() => new()
    {
        // Layout
        new("Grid",      "布局", "\uE80F", "<Grid/>", "多行多列容器"),
        new("StackPanel","布局", "\uE8C8", "<StackPanel Orientation=\"Vertical\" Spacing=\"8\"/>", "垂直/水平堆叠"),
        new("Canvas",    "布局", "\uE8B7", "<Canvas/>", "绝对定位容器"),
        new("Border",    "布局", "\uE71D", "<Border CornerRadius=\"8\" Background=\"#FF333333\"/>", "带背景/边框的容器"),
        new("ScrollViewer","布局","\uE8C9","<ScrollViewer/>", "可滚动容器"),

        // Text
        new("TextBlock", "文本", "\uE8D2", "<TextBlock Text=\"Hello\" FontSize=\"24\"/>", "单行/多行文本"),
        new("RichTextBlock","文本","\uE8D3","<RichTextBlock/>", "富文本 (Run/Hyperlink)"),
        new("TextBox",   "文本", "\uE8A1", "<TextBox Text=\"\"/>", "可编辑文本"),
        new("AutoSuggestBox","文本","\uE82E","<AutoSuggestBox/>", "带建议的输入"),

        // Buttons
        new("Button",    "交互", "\uE8A0", "<Button Content=\"Click me\"/>", "按钮"),
        new("HyperlinkButton","交互","\uE71B","<HyperlinkButton Content=\"Link\"/>", "链接按钮"),
        new("ToggleButton","交互","\uE8FF","<ToggleButton Content=\"Toggle\"/>", "开关按钮"),
        new("CheckBox",  "交互", "\uE73A", "<CheckBox Content=\"Option\"/>", "复选框"),
        new("RadioButton","交互","\uE915","<RadioButton Content=\"Option\" GroupName=\"g1\"/>", "单选"),
        new("Slider",    "交互", "\uE9C9", "<Slider Minimum=\"0\" Maximum=\"100\" Value=\"50\"/>", "滑块"),
        new("ToggleSwitch","交互","\uE7E8","<ToggleSwitch/>", "开关"),

        // Display
        new("Image",     "显示", "\uE8B9", "<Image Source=\"ms-appx:///placeholder.png\" Stretch=\"Uniform\"/>", "图片"),
        new("Rectangle", "形状", "\uE8C5", "<Rectangle Fill=\"#FF0078D4\" Width=\"120\" Height=\"80\"/>", "矩形"),
        new("Ellipse",   "形状", "\uE7E0", "<Ellipse Fill=\"#FF0078D4\" Width=\"80\" Height=\"80\"/>", "椭圆"),
        new("Line",      "形状", "\uE83A", "<Line X1=\"0\" Y1=\"0\" X2=\"100\" Y2=\"0\" Stroke=\"White\" StrokeThickness=\"2\"/>", "直线"),
        new("ProgressRing","显示","\uE9F5","<ProgressRing IsActive=\"True\"/>", "加载环"),
        new("PersonPicture","显示","\uE77B","<PersonPicture/>", "头像占位"),

        // Media
        new("MediaPlayerElement","媒体","\uE714","<MediaPlayerElement Source=\"ms-appx:///video.mp4\"/>", "视频播放器"),

        // FluentCourse 自定义
        new("QuizControl","互动","\uE82D",
            "<fc:QuizControl xmlns:fc=\"using:FluentCourse.Runtime.Controls\"\r\n" +
            "                Question=\"题目？\" Mode=\"Single\">\r\n" +
            "    <fc:QuizControl.Choices>\r\n" +
            "        <fc:QuizChoice Text=\"A. 选项 1\"/>\r\n" +
            "        <fc:QuizChoice Text=\"B. 选项 2\" IsAnswer=\"True\"/>\r\n" +
            "    </fc:QuizControl.Choices>\r\n" +
            "</fc:QuizControl>",
            "选择题/判断题/填空题"),
        new("StepRevealControl","互动","\uE8B1",
            "<fc:StepRevealControl xmlns:fc=\"using:FluentCourse.Runtime.Controls\">\r\n" +
            "    <TextBlock Text=\"步骤 1\"/>\r\n" +
            "    <TextBlock Text=\"步骤 2\"/>\r\n" +
            "</fc:StepRevealControl>",
            "逐步展开内容"),
    };
}
