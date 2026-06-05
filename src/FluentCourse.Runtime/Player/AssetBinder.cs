using FluentCourse.Core.Packaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Walks a freshly parsed page tree and replaces sentinel asset URIs with real resources
/// loaded from the .fcourse package. Runs once after <see cref="PageLoader.Load"/>.
/// </summary>
public sealed class AssetBinder
{
    private const string SchemePrefix = "https://fcourse.local/";
    private readonly CoursePackage _package;

    public AssetBinder(CoursePackage package)
    {
        _package = package;
    }

    public async Task BindAsync(DependencyObject root)
    {
        foreach (var (image, uri) in EnumerateImageSources(root))
        {
            if (!TryParseEntry(uri, out var entry)) continue;
            try
            {
                using var stream = _package.OpenSeekable(entry);
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                image.Source = bmp;
            }
            catch (Exception ex)
            {
                // A bad image, missing entry, or stream error should never abort the whole
                // page transition. Leave the image blank and log the rest for diagnostics.
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] Failed to bind '{entry}': {ex.Message}");
            }
        }
    }

    private static bool TryParseEntry(string uri, out string entry)
    {
        if (uri.StartsWith(SchemePrefix, StringComparison.Ordinal))
        {
            entry = uri[SchemePrefix.Length..];
            return entry.Length > 0;
        }
        entry = "";
        return false;
    }

    private static IEnumerable<(Image image, string uri)> EnumerateImageSources(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is Image img && img.Source is BitmapImage bmp && bmp.UriSource is { } u)
                yield return (img, u.ToString());

            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
    }
}
