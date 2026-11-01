namespace FluentCourse.Runtime.Player;

/// <summary>
/// 资源占位 URI 的公共工具。页面 XAML 内用自定义 scheme <c>ms-fcourse:///</c>，
/// 解析前由 <see cref="PageLoader"/> 临时改写为 <c>https://fcourse.local/</c>（绕开 XamlReader 的 URI 限制）。
/// 取「包内相对路径」时这两种前缀都可能出现，故统一在此剥离，避免各处重复 .Replace 链。
/// </summary>
public static class AssetUri
{
    /// <summary>自定义资源 scheme（页面 XAML 中书写的形式）。</summary>
    public const string CustomScheme = "ms-fcourse:///";

    /// <summary>解析期临时改写后的 https 占位前缀（见 <see cref="AssetBinder"/> / <see cref="PageLoader"/>）。</summary>
    public const string ParseScheme = "https://fcourse.local/";

    /// <summary>
    /// 把占位 scheme（<see cref="CustomScheme"/> 或 <see cref="ParseScheme"/>）从 URI 中剥离，
    /// 得到包内相对路径（如 <c>assets/img.png</c>）。两种前缀都去掉，与原有 .Replace 链行为等价。
    /// </summary>
    public static string StripScheme(string raw) =>
        raw.Replace(CustomScheme, "").Replace(ParseScheme, "");
}
