using Microsoft.UI.Xaml;

namespace FluentCourse.Runtime.Controls;

/// <summary>
/// 媒体类型标记（附加属性）。<c>MediaPlayerElement</c> 在运行时对音频/视频表现一致，
/// 该属性仅供设计器往返时区分插入的是「音频」还是「视频」（影响默认尺寸与属性面板呈现），
/// 运行时忽略其值。序列化为 <c>fc:Media.Kind="Audio"</c>
/// （fc = <c>using:FluentCourse.Runtime.Controls</c>，与 QuizControl 等同命名空间，XamlReader 可解析）。
/// </summary>
public static class Media
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.RegisterAttached(
            "Kind", typeof(string), typeof(Media), new PropertyMetadata(null));

    public static string GetKind(DependencyObject obj) => (string)obj.GetValue(KindProperty);
    public static void SetKind(DependencyObject obj, string value) => obj.SetValue(KindProperty, value);
}
