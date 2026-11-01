using System.Collections.ObjectModel;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class AnimationEditor : UserControl
{
    private ShellViewModel? _vm;
    private bool _suppress;
    private AnimationEntry? _selected;

    private static readonly string[] AnimProperties =
        { "Opacity", "TranslateX", "TranslateY", "ScaleX", "ScaleY", "Rotation",
          "Color", "Width", "Height", "FontSize", "CornerRadius", "BackgroundColor", "BorderColor" };

    public AnimationEditor()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        _vm.RequestRefreshAnimations += (_, _) => Refresh();
        _vm.DocumentChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        if (_vm?.Document is null) return;
        AnimList.ItemsSource = _vm.Document.Animations;
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = AnimList.SelectedItem as AnimationEntry;
        if (_selected is null) { EditForm.Visibility = Visibility.Collapsed; return; }
        EditForm.Visibility = Visibility.Visible;
        LoadAnim(_selected);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var name = $"anim-{_vm?.Document?.Animations.Count + 1 ?? 1}";
        _vm?.AddAnimation(name, new AnimationDescriptor());
    }

    private void OnDelete(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (_selected is null) return;
        _vm?.DeleteAnimation(_selected);
        _selected = null;
        EditForm.Visibility = Visibility.Collapsed;
    }

    private void LoadAnim(AnimationEntry entry)
    {
        _suppress = true;
        var d = entry.Descriptor;
        NameBox.Text = entry.Name;
        DurationBox.Text = d.DurationMs.ToString();
        DelayBox.Text = d.DelayMs.ToString();
        EasingBox.SelectedItem = EasingBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Content == d.Easing);
        RepeatBox.Text = d.Repeat.ToString();
        AutoRevBox.IsChecked = d.AutoReverse;
        PopulateTracks(d);
        _suppress = false;
    }

    private void PopulateTracks(AnimationDescriptor d)
    {
        var list = new ObservableCollection<TrackEditModel>();
        foreach (var t in d.Tracks)
            list.Add(new TrackEditModel(t));
        TracksList.ItemsSource = list;
    }

    private void OnAddTrack(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.Descriptor.Tracks.Add(new AnimationTrack());
        PopulateTracks(_selected.Descriptor);
        _vm?.MarkDirty();
    }

    private void OnRemoveTrack(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not Button btn) return;
        if (btn.DataContext is not TrackEditModel tem) return;
        _selected.Descriptor.Tracks.Remove(tem.Track);
        PopulateTracks(_selected.Descriptor);
        _vm?.MarkDirty();
    }

    private void OnTrackComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb) cb.ItemsSource = AnimProperties;
    }

    private void OnChanged(object sender, object e)
    {
        if (_suppress || _selected is null) return;
        var d = _selected.Descriptor;
        _selected.Name = NameBox.Text;
        d.DurationMs = double.TryParse(DurationBox.Text, out var dur) ? dur : 400;
        d.DelayMs = double.TryParse(DelayBox.Text, out var del) ? del : 0;
        if (EasingBox.SelectedItem is ComboBoxItem cbi) d.Easing = (string)cbi.Content;
        d.Repeat = int.TryParse(RepeatBox.Text, out var r) ? r : 1;
        d.AutoReverse = AutoRevBox.IsChecked == true;
        _vm!.MarkDirty();
        Refresh();
    }

    private void OnTrackChanged(object sender, object e)
    {
        if (_suppress) return;
        if (sender is FrameworkElement fe && fe.DataContext is TrackEditModel tem)
        {
            var t = tem.Track;
            if (fe.Name == "TrackProp" && fe is ComboBox cb && cb.SelectedItem is string s) t.Property = s;
            if (fe.Name == "TrackFrom" && fe is TextBox tb0) t.From = double.TryParse(tb0.Text, out var f) ? f : null;
            if (fe.Name == "TrackTo" && fe is TextBox tb1) t.To = double.TryParse(tb1.Text, out var tval) ? tval : null;
            if (fe.Name == "TrackFromColor" && fe is TextBox tb2) t.FromColor = tb2.Text.NullIfEmpty();
            if (fe.Name == "TrackToColor" && fe is TextBox tb3) t.ToColor = tb3.Text.NullIfEmpty();
            _vm?.MarkDirty();
        }
    }

    private sealed class TrackEditModel(AnimationTrack t)
    {
        public AnimationTrack Track { get; } = t;
        public string Property { get => Track.Property; set => Track.Property = value; }
    }
}
