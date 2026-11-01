using System.Collections.Generic;
using System.Linq;
using FluentCourse.Designer.Models;
using FluentCourse.Designer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCourse.Designer.Views;

public sealed partial class ElementTreeView : UserControl
{
    private ShellViewModel? _vm;

    public ElementTreeView()
    {
        try { InitializeComponent(); }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[ElementTreeView] init failed: {ex}"); }
    }

    public void SetViewModel(ShellViewModel vm)
    {
        _vm = vm;
        vm.DocumentChanged += (_, _) => Refresh();
        vm.RequestRender  += (_, _) => Refresh();
        vm.SelectionChanged += (_, _) => SyncSelection();
    }

    public void Refresh()
    {
        Tree.ItemsSource = null;
        var page = _vm?.Document?.SelectedPage;
        if (page is null || page.Elements.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }
        EmptyHint.Visibility = Visibility.Collapsed;

        var nodes = new List<TreeNodeWrapper>();
        foreach (var em in page.Elements)
        {
            var wrapper = BuildElementTree(em);
            var query = TreeSearchBox?.Text ?? "";
            ApplyFilter(wrapper, query);
            nodes.Add(wrapper);
        }
        Tree.ItemsSource = nodes;
        SyncSelection();
    }

    private void OnTreeSearchTextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private static void ApplyFilter(TreeNodeWrapper node, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            node.IsMatch = true;
            foreach (var c in node.Children) ApplyFilter(c, query);
            return;
        }
        var q = query.Trim();
        foreach (var c in node.Children) ApplyFilter(c, q);
        var selfMatch = node.Element.Type.Contains(q, System.StringComparison.OrdinalIgnoreCase)
            || (node.Element.Name?.Contains(q, System.StringComparison.OrdinalIgnoreCase) ?? false);
        var anyChildMatch = node.Children.Any(c => c.IsMatch);
        node.IsMatch = selfMatch || anyChildMatch;
    }

    private static TreeNodeWrapper BuildElementTree(ElementModel em)
    {
        var label = string.IsNullOrEmpty(em.Name) ? em.Type : $"{em.Type} ({em.Name})";
        var node = new TreeNodeWrapper(em, label, GlyphFor(em.Type));
        foreach (var child in em.Children)
            node.Children.Add(BuildElementTree(child));
        return node;
    }

    private static string GlyphFor(string typeName) => typeName switch
    {
        "Grid" => "\uE80F",
        "StackPanel" => "\uE8C8",
        "Canvas" => "\uE8B7",
        "Border" => "\uE71D",
        "TextBlock" => "\uE8D2",
        "RichTextBlock" => "\uE8D3",
        "TextBox" => "\uE8A1",
        "Button" => "\uE8A0",
        "HyperlinkButton" => "\uE71B",
        "ToggleButton" => "\uE8FF",
        "CheckBox" => "\uE73A",
        "RadioButton" => "\uE915",
        "Slider" => "\uE9C9",
        "ToggleSwitch" => "\uE7E8",
        "Image" => "\uE8B9",
        "Rectangle" or "Ellipse" or "Line" => "\uE8C5",
        "ProgressRing" => "\uE9F5",
        "PersonPicture" => "\uE77B",
        "ScrollViewer" => "\uE8C9",
        "AutoSuggestBox" => "\uE82E",
        _ => "\uE8A5"
    };

    private void SyncSelection()
    {
        if (_vm?.SelectedElement is null) return;
        var match = FindNode(_vm.SelectedElement);
        if (match is not null) Tree.SelectedNode = match;
    }

    private TreeViewNode? FindNode(ElementModel target)
    {
        foreach (var root in Tree.RootNodes)
            if (MatchInNode(root, target, out var hit)) return hit;
        return null;
    }

    private static bool MatchInNode(TreeViewNode node, ElementModel target, out TreeViewNode hit)
    {
        hit = node;
        if (node.Content is TreeNodeWrapper w && ReferenceEquals(w.Element, target)) return true;
        foreach (var child in node.Children)
            if (MatchInNode(child, target, out hit)) return true;
        return false;
    }

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_vm is null) return;
        if (args.AddedItems.FirstOrDefault() is TreeNodeWrapper node)
            _vm.SelectedElement = node.Element;
    }
}

public sealed class TreeNodeWrapper
{
    public TreeNodeWrapper(ElementModel element, string label, string glyph)
    {
        Element = element;
        Label = label;
        Glyph = glyph;
        Children = new List<TreeNodeWrapper>();
    }
    public ElementModel Element { get; }
    public string Label { get; }
    public string Glyph { get; }
    public List<TreeNodeWrapper> Children { get; }
    public bool IsMatch { get; set; } = true;
}
