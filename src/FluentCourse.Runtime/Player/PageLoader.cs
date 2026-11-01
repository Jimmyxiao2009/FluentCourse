using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Loads a page's XAML tree. XamlReader is the chokepoint for the whole runtime,
/// so error handling and asset URI rewriting both live here.
/// </summary>
public sealed class PageLoader
{
    private readonly CoursePackage _package;

    public PageLoader(CoursePackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Returns a fresh <see cref="FrameworkElement"/> tree for a page id. Always returns a new
    /// instance — the same element can't appear in two visual trees.
    /// </summary>
    public FrameworkElement Load(string pageId)
    {
        var page = FindPage(pageId);
        var raw = _package.ReadText(page.File);
        var rewritten = AssetUriRewriter.Rewrite(raw);

        try
        {
            var loaded = XamlReader.Load(rewritten)
                ?? throw new InvalidOperationException($"XamlReader returned null for page '{pageId}'.");

            if (loaded is not FrameworkElement fe)
                throw new InvalidOperationException(
                    $"Page '{pageId}' root must be a FrameworkElement (got {loaded.GetType().Name}).");

            return fe;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse XAML for page '{pageId}': {ex.Message}", ex);
        }
    }

    public PageDescriptor FindPage(string pageId)
    {
        var page = _package.Manifest.Pages.FirstOrDefault(p => p.Id == pageId);
        return page ?? throw new KeyNotFoundException($"Page '{pageId}' not in manifest.");
    }
}

/// <summary>
/// Rewrites <c>ms-fcourse:///foo</c> URIs into a parse-safe https:// scheme at parse time.
/// XamlReader runs in the UI process and we can't register a custom URI scheme for it.
/// After parse, <see cref="Player.AssetBinder"/> walks the tree and swaps the https://
/// placeholders for real image streams loaded from the package.
/// </summary>
internal static class AssetUriRewriter
{
    // 仅替换 XML 属性值（="..."）内部出现的 ms-fcourse:///，避免把 TextBlock 文本内容
    // 等元素正文里的字面 URI 一并改写。项目约定属性值内不允许出现 ASCII 双引号,
    // 因此用 ="[^"]*" 匹配整个属性值是安全的。
    private static readonly System.Text.RegularExpressions.Regex AttributeValue =
        new("=\"[^\"]*\"", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Rewrite(string xaml) =>
        AttributeValue.Replace(xaml, m =>
            m.Value.Contains("ms-fcourse:///", StringComparison.OrdinalIgnoreCase)
                ? System.Text.RegularExpressions.Regex.Replace(
                    m.Value, "ms-fcourse:///", "https://fcourse.local/",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                : m.Value);
}
