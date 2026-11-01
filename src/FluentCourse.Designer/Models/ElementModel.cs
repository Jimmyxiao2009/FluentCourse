using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentCourse.Designer.Models;

public sealed partial class ElementModel : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private double _rotation;
    [ObservableProperty] private double _scale = 1;   // uniform RenderTransform scale (1 = 100%)
    [ObservableProperty] private int _zIndex;
    [ObservableProperty] private string _type = "TextBlock";
    [ObservableProperty] private string? _name;

    public Dictionary<string, string> Props { get; } = new();
    public ObservableCollection<ElementModel> Children { get; } = new();

    public ElementModel Clone()
    {
        var clone = new ElementModel
        {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Rotation = Rotation,
            Scale = Scale,
            ZIndex = ZIndex,
            Type = Type,
            Name = Name
        };
        foreach (var kv in Props) clone.Props[kv.Key] = kv.Value;
        foreach (var child in Children) clone.Children.Add(child.Clone());
        return clone;
    }

    public static double GetDefaultWidth(string type) => type switch
    {
        "TextBlock" => 160,
        "Image" => 200,
        "Rectangle" => 120,
        "Ellipse" => 80,
        "Button" => 120,
        "MediaPlayerElement" => 320,
        _ => 120
    };

    public static double GetDefaultHeight(string type) => type switch
    {
        "TextBlock" => 40,
        "Image" => 140,
        "Rectangle" => 80,
        "Ellipse" => 80,
        "Button" => 36,
        "MediaPlayerElement" => 180,
        _ => 40
    };
}
