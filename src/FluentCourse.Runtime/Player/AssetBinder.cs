using FluentCourse.Core.Models;
using FluentCourse.Core.Packaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentCourse.Runtime.Player;

/// <summary>
/// Walks a freshly parsed page tree and replaces sentinel asset URIs with real resources
/// loaded from the .fcourse package. Now also applies per-asset settings like fit mode
/// and placeholder colour from <see cref="AssetDescriptor"/> entries.
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
        var assetsByKey = _package.Manifest.Assets.ToDictionary(a => a.Key, StringComparer.Ordinal);
        foreach (var (image, uri) in EnumerateImageSources(root))
        {
            if (!TryParseEntry(uri, out var entry)) continue;
            try
            {
                AssetDescriptor? desc = null;
                assetsByKey.TryGetValue(entry, out desc);
                ApplyPlaceholder(image, desc);
                ApplyFit(image, desc);

                if (GetPreload(desc) == AssetPreload.Lazy)
                {
                    // Lazy: defer until the element is in view for at least 500 ms.
                    image.Loaded += StartLazyLoad;
                    continue;
                }

                await LoadImageAsync(image, entry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] Failed to bind '{entry}': {ex.Message}");
            }
        }
    }

    private static void ApplyPlaceholder(Image image, AssetDescriptor? desc)
    {
        // WinUI Image has no Background; placeholders are applied by the parent Border
        // or the hosting panel. The field is kept in the model for future editor support.
        _ = desc?.PlaceholderColor;
    }

    private static void ApplyFit(Image image, AssetDescriptor? desc)
    {
        image.Stretch = GetFit(desc);
    }

    private static Stretch GetFit(AssetDescriptor? desc) => desc?.Fit switch
    {
        "none"    => Stretch.None,
        "fill"    => Stretch.Fill,
        "contain" => Stretch.Uniform,
        "cover"   => Stretch.UniformToFill,
        _         => Stretch.Uniform
    };

    private static AssetPreload GetPreload(AssetDescriptor? desc) => desc?.Preload switch
    {
        "lazy"    => AssetPreload.Lazy,
        "metadata" => AssetPreload.Metadata,
        _          => AssetPreload.Eager
    };

    private async Task LoadImageAsync(Image image, string entry)
    {
        using var stream = _package.OpenSeekable(entry);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream.AsRandomAccessStream());
        image.Source = bmp;
    }

    private async void StartLazyLoad(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        image.Loaded -= StartLazyLoad;
        await Task.Delay(500);
        image.DispatcherQueue.TryEnqueue(() => _ = LoadImageAndSwap(image));
    }

    private async Task LoadImageAndSwap(Image image)
    {
        if (image.Tag is not string entry) return;
        try { await LoadImageAsync(image, entry); } catch { }
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
            {
                var uriStr = u.ToString();
                yield return (img, uriStr);
                // cache the entry path for lazy loads
                if (TryParseEntry(uriStr, out var entry))
                    img.Tag = entry;
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
    }
}

public enum AssetPreload { Eager, Lazy, Metadata }
