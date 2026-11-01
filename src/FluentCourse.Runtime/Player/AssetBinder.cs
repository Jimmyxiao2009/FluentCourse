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
/// 每个页面绑定的媒体流/MediaPlayer 会被记录,翻页时由 <see cref="UnbindPage"/> 释放,
/// 避免翻页后继续播放与句柄泄漏。
/// </summary>
public sealed class AssetBinder
{
    private const string SchemePrefix = "https://fcourse.local/";
    private readonly CoursePackage _package;

    // 防止 Loaded 后补扫描重复绑定同一元素的附加标记
    private static readonly DependencyProperty BoundProperty =
        DependencyProperty.RegisterAttached("AssetBound", typeof(bool), typeof(AssetBinder),
            new PropertyMetadata(false));

    /// <summary>每个页面根 → 该页打开的媒体流与 MediaPlayerElement,翻页时统一释放。</summary>
    private readonly Dictionary<DependencyObject, PageResources> _pageResources = new();

    /// <summary>
    /// 每个页面根 → 注册在其 Loaded 上的补扫描处理器。UnbindPage 必须用它解绑,
    /// 否则快速翻页时延迟触发的 Loaded 会在页面释放后重建 _pageResources 条目造成泄漏。
    /// 同时它也作为「页面是否仍然活跃」的标志:不在此字典中即表示已被 UnbindPage。
    /// </summary>
    private readonly Dictionary<FrameworkElement, RoutedEventHandler> _rescanHandlers = new();

    private sealed class PageResources
    {
        public List<System.IO.Stream> Streams { get; } = new();
        public List<MediaPlayerElement> Media { get; } = new();
    }

    public AssetBinder(CoursePackage package)
    {
        _package = package;
    }

    public async Task BindAsync(DependencyObject root)
    {
        await BindCoreAsync(root);

        // 兜底:元素入树前 VisualTreeHelper 看不到 ItemsControl 项/未应用模板的控件内部,
        // 页面根 Loaded 后再补扫一次(已绑定的元素由 BoundProperty 标记跳过)。
        if (root is FrameworkElement fe)
        {
            RoutedEventHandler? rescan = null;
            rescan = async (_, _) =>
            {
                fe.Loaded -= rescan;

                // 防线二:若该页已被 UnbindPage(其处理器已从字典移除),说明页面已释放,
                // 直接返回,避免补扫描重新 OpenSeekable 重建 _pageResources 条目造成句柄泄漏。
                // 正常翻页路径下页面仍活跃,处理器仍在字典中,补扫描照常进行。
                if (!_rescanHandlers.Remove(fe, out var current) || !ReferenceEquals(current, rescan))
                    return;

                try { await BindCoreAsync(root); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AssetBinder] Loaded 后补扫描失败: {ex.Message}");
                }
            };
            // 记录处理器引用,供 UnbindPage 解绑;若同一 root 重复绑定,先清掉旧的。
            if (_rescanHandlers.TryGetValue(fe, out var old))
                fe.Loaded -= old;
            _rescanHandlers[fe] = rescan;
            fe.Loaded += rescan;
        }
    }

    private async Task BindCoreAsync(DependencyObject root)
    {
        // 容忍 manifest 中重复的资源 key(取第一个),避免 ToDictionary 抛异常导致翻页崩溃
        var assetsByKey = new Dictionary<string, AssetDescriptor>(StringComparer.Ordinal);
        foreach (var a in _package.Manifest.Assets)
        {
            if (!assetsByKey.TryAdd(a.Key, a))
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] manifest 中存在重复资源 key '{a.Key}',已忽略后者。");
        }

        foreach (var (image, uri) in EnumerateImageSources(root))
        {
            if (!TryParseEntry(uri, out var entry)) continue;
            if (IsBound(image)) continue;
            try
            {
                AssetDescriptor? desc = null;
                assetsByKey.TryGetValue(entry, out desc);
                ApplyPlaceholder(image, desc);
                ApplyFit(image, desc);
                MarkBound(image);

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

        // Media (audio/video): the source is carried in Tag (a string), since MediaPlayerElement.Source
        // can't be set from a XAML string. Turn it into a real MediaSource from the package.
        foreach (var (mpe, uri) in EnumerateMediaSources(root))
        {
            if (!TryParseEntry(uri, out var entry)) continue;
            if (IsBound(mpe)) continue;
            try
            {
                var stream = _package.OpenSeekable(entry);   // kept open for playback; UnbindPage 负责释放
                mpe.Source = Windows.Media.Core.MediaSource.CreateFromStream(
                    stream.AsRandomAccessStream(), ContentTypeFor(entry));
                mpe.AreTransportControlsEnabled = true;
                MarkBound(mpe);

                var res = GetOrCreateResources(root);
                res.Streams.Add(stream);
                res.Media.Add(mpe);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] Failed to bind media '{entry}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 释放某个页面绑定的全部媒体资源:暂停播放、断开 Source、Dispose MediaPlayer 与底层流。
    /// 由 PagePlayer 在页面从舞台移除时调用。
    /// </summary>
    public void UnbindPage(DependencyObject root)
    {
        // 先解绑补扫描处理器并从字典移除:即使本页没有媒体资源(从未建过 _pageResources 条目),
        // 也必须断掉延迟 Loaded 回调,否则它会在本页释放后重扫并重建条目,造成句柄泄漏。
        // 从字典移除后,延迟触发的回调会因防线二判定页面已释放而直接返回。
        if (root is FrameworkElement fe && _rescanHandlers.Remove(fe, out var rescan))
            fe.Loaded -= rescan;

        if (!_pageResources.TryGetValue(root, out var res)) return;
        _pageResources.Remove(root);

        foreach (var mpe in res.Media)
        {
            try
            {
                var mp = mpe.MediaPlayer;
                mp?.Pause();
                var source = mpe.Source as IDisposable;
                mpe.Source = null;
                // 先与元素解绑再释放旧 player,避免元素持有已释放对象
                mpe.SetMediaPlayer(null!);
                source?.Dispose();
                mp?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] 释放媒体失败: {ex.Message}");
            }
        }

        foreach (var stream in res.Streams)
        {
            try { stream.Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AssetBinder] 释放媒体流失败: {ex.Message}");
            }
        }
    }

    private PageResources GetOrCreateResources(DependencyObject root)
    {
        if (!_pageResources.TryGetValue(root, out var res))
        {
            res = new PageResources();
            _pageResources[root] = res;
        }
        return res;
    }

    private static bool IsBound(DependencyObject d) => d.GetValue(BoundProperty) is true;
    private static void MarkBound(DependencyObject d) => d.SetValue(BoundProperty, true);

    private static IEnumerable<(MediaPlayerElement mpe, string uri)> EnumerateMediaSources(DependencyObject root)
    {
        foreach (var node in EnumerateTree(root))
        {
            if (node is MediaPlayerElement mpe && mpe.Tag is string tag && tag.Length > 0)
                yield return (mpe, tag);
        }
    }

    private static string ContentTypeFor(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3"          => "audio/mpeg",
            ".m4a" or ".aac" => "audio/aac",
            ".wav"          => "audio/wav",
            ".ogg"          => "audio/ogg",
            ".flac"         => "audio/flac",
            ".webm"         => "video/webm",
            ".mov"          => "video/quicktime",
            ".avi"          => "video/x-msvideo",
            ".wmv"          => "video/x-ms-wmv",
            _               => "video/mp4"
        };
    }

    private static void ApplyPlaceholder(Image image, AssetDescriptor? desc)
    {
        // WinUI Image has no Background; placeholders are applied by the parent Border
        // or the hosting panel. The field is kept in the model for future editor support.
        _ = desc?.PlaceholderColor;
    }

    private static void ApplyFit(Image image, AssetDescriptor? desc)
    {
        // 仅当 manifest 资源条目显式给出可识别的 fit 值时才覆盖页面 XAML 中的 Stretch;
        // 默认值("stretch")或未识别值一律保留页面声明。
        switch (desc?.Fit)
        {
            case "none":    image.Stretch = Stretch.None; break;
            case "fill":    image.Stretch = Stretch.Fill; break;
            case "contain": image.Stretch = Stretch.Uniform; break;
            case "cover":   image.Stretch = Stretch.UniformToFill; break;
        }
    }

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
        foreach (var node in EnumerateTree(root))
        {
            if (node is Image img && img.Source is BitmapImage bmp && bmp.UriSource is { } u)
            {
                var uriStr = u.ToString();
                yield return (img, uriStr);
                // cache the entry path for lazy loads
                if (TryParseEntry(uriStr, out var entry))
                    img.Tag = entry;
            }
        }
    }

    /// <summary>
    /// 同时遍历视觉树与逻辑结构。元素入树前视觉树为空(VisualTreeHelper 看不到
    /// ItemsControl.Items、ContentControl.Content、未应用模板的控件内部),
    /// 因此补充遍历 Panel.Children / Border.Child / ContentControl.Content /
    /// ContentPresenter.Content / ItemsControl.Items。
    /// </summary>
    private static IEnumerable<DependencyObject> EnumerateTree(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        var seen = new HashSet<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node)) continue;
            yield return node;

            // 视觉树(模板已应用、元素已入树时)
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));

            // 逻辑结构(兜住未入树/未模板化的子元素)
            switch (node)
            {
                case Panel panel:
                    foreach (var child in panel.Children) queue.Enqueue(child);
                    break;
                case Border border when border.Child is not null:
                    queue.Enqueue(border.Child);
                    break;
                case ContentControl cc when cc.Content is DependencyObject ccContent:
                    queue.Enqueue(ccContent);
                    break;
                case ContentPresenter cp when cp.Content is DependencyObject cpContent:
                    queue.Enqueue(cpContent);
                    break;
                case ItemsControl ic:
                    foreach (var item in ic.Items)
                        if (item is DependencyObject d) queue.Enqueue(d);
                    break;
            }
        }
    }
}

public enum AssetPreload { Eager, Lazy, Metadata }
