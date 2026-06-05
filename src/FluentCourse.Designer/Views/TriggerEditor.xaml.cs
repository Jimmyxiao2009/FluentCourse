using System.Collections.ObjectModel;
using System.Text.Json;
using FluentCourse.Core.Models;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class TriggerEditor : UserControl
{
    private ShellViewModel? _vm;
    private ObservableCollection<TriggerEditModel> _editModels = new();
    private bool _suppress;
    private TriggerEditModel? _selected;

    public TriggerEditor()
    {
        InitializeComponent();
        TEventTypeBox.ItemsSource = ActionMeta.AllEventTypes;
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        _vm.RequestRefreshTriggers += (_, _) => Refresh();
        if (_vm.Document is not null)
            _vm.Document.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        if (_vm?.Document is null) return;
        _editModels = new ObservableCollection<TriggerEditModel>(
            _vm.Document.Triggers.Select(t => new TriggerEditModel(t)));
        TriggerList.ItemsSource = _editModels;
    }

    private void OnTriggerSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = TriggerList.SelectedItem as TriggerEditModel;
        if (_selected is null) { EditForm.Visibility = Visibility.Collapsed; return; }
        EditForm.Visibility = Visibility.Visible;
        LoadTrigger(_selected);
    }

    private void OnAddTrigger(object sender, RoutedEventArgs e)
    {
        var td = new TriggerDescriptor
        {
            Id = $"t-{Guid.NewGuid():N}"[..12],
            On = new TriggerEvent { Type = "Click" }
        };
        _vm?.AddTrigger(td);
    }

    private void OnDeleteTrigger(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (_selected is null) return;
        _vm?.RemoveTrigger(_selected.Descriptor);
        _selected = null;
        EditForm.Visibility = Visibility.Collapsed;
    }

    private void LoadTrigger(TriggerEditModel m)
    {
        _suppress = true;
        var d = m.Descriptor;
        TIdBox.Text = d.Id;
        TPageBox.ItemsSource = _vm!.Document!.Pages.Select(p => p.Descriptor.Id).ToList();
        TPageBox.SelectedItem = d.Page ?? "";
        TEventTypeBox.SelectedIndex = System.Array.IndexOf(ActionMeta.AllEventTypes, d.On.Type);
        TTargetBox.Text = d.On.Target ?? "";
        TKeyBox.Text = d.On.Key ?? "";
        TAtBox.Text = d.On.At?.ToString() ?? "";
        TTapCountBox.Text = d.On.TapCount?.ToString() ?? "";
        TModifiersBox.Text = m.ModifiersStr ?? "";
        TDebounceBox.Text = d.On.DebounceMs?.ToString() ?? "";
        TComparedBox.Text = d.On.ComparedTo ?? "";
        TThresholdBox.Text = d.On.Threshold?.ToString() ?? "";
        TOnceBox.IsChecked = d.On.Once;
        TStopBox.IsChecked = d.StopAfterLastStep;
        TConditionBox.Text = d.Condition ?? "";
        PopulateActions(d);
        _suppress = false;
    }

    private void PopulateActions(TriggerDescriptor d)
    {
        ActionsList.ItemsSource = null;
        var items = new ObservableCollection<ActionEditModel>();
        foreach (var a in d.Do)
            items.Add(new ActionEditModel(a));
        ActionsList.ItemsSource = items;
    }

    private sealed class ActionEditModel(ActionDescriptor a)
    {
        public ActionDescriptor Descriptor = a;
        public string Type { get => a.Type; set => a.Type = value; }
        public string? Target { get => a.Target; set => a.Target = value; }
    }

    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var a = new ActionDescriptor { Type = "PlayAnimation" };
        _selected.Descriptor.Do.Add(a);
        PopulateActions(_selected.Descriptor);
        _vm?.MarkDirty();
    }

    private void OnRemoveAction(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not Button btn) return;
        if (btn.DataContext is not ActionEditModel aem) return;
        _selected.Descriptor.Do.Remove(aem.Descriptor);
        PopulateActions(_selected.Descriptor);
        _vm?.MarkDirty();
    }

    private void OnActionFieldChanged(object sender, object e)
    {
        if (_suppress) return;
        if (sender is FrameworkElement fe && fe.DataContext is ActionEditModel aem)
        {
            if (fe is ComboBox cb) aem.Descriptor.Type = cb.SelectedItem as string ?? "";
            if (fe is TextBox tb && tb.Tag is string key)
                aem.Descriptor.Parameters[key] = JsonSerializer.SerializeToElement(tb.Text);
            _vm?.MarkDirty();
        }
    }

    private void OnFieldChanged(object sender, object e)
    {
        if (_suppress || _selected is null) return;
        var d = _selected.Descriptor;
        d.Id = TIdBox.Text;
        d.Page = TPageBox.SelectedItem as string ?? "";
        if (TEventTypeBox.SelectedItem is string et) d.On.Type = et;
        d.On.Target = TTargetBox.Text.NullIfEmpty();
        d.On.Key = TKeyBox.Text.NullIfEmpty();
        d.On.At = double.TryParse(TAtBox.Text, out var at) ? at : null;
        d.On.TapCount = int.TryParse(TTapCountBox.Text, out var tc) ? tc : null;
        d.On.Modifiers = string.IsNullOrWhiteSpace(TModifiersBox.Text) ? null
            : TModifiersBox.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        d.On.DebounceMs = int.TryParse(TDebounceBox.Text, out var db) ? db : null;
        d.On.ComparedTo = TComparedBox.Text.NullIfEmpty();
        d.On.Threshold = double.TryParse(TThresholdBox.Text, out var th) ? th : null;
        d.On.Once = TOnceBox.IsChecked == true;
        d.StopAfterLastStep = TStopBox.IsChecked == true;
        d.Condition = TConditionBox.Text.NullIfEmpty();
        _vm!.MarkDirty();
        Refresh();
    }
}
