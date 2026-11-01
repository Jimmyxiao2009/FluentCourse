using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentCourse.Designer.Models;

namespace FluentCourse.Designer.Services;

public interface IUndoCommand
{
    string Description { get; }
    void Undo();
    void Redo();
}

public sealed class UndoRedoService : INotifyPropertyChanged
{
    private readonly Stack<IUndoCommand> _undo = new();
    private readonly Stack<IUndoCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoCommand cmd)
    {
        _undo.Push(cmd);
        _redo.Clear();
        Notify();
    }

    public IUndoCommand? Undo()
    {
        if (!CanUndo) return null;
        var cmd = _undo.Pop();
        cmd.Undo();
        _redo.Push(cmd);
        Notify();
        return cmd;
    }

    public IUndoCommand? Redo()
    {
        if (!CanRedo) return null;
        var cmd = _redo.Pop();
        cmd.Redo();
        _undo.Push(cmd);
        Notify();
        return cmd;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Notify();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
    }
}

// ── editor-specific commands ──

internal sealed class MoveCommand : IUndoCommand
{
    private readonly ElementModel _em;
    private readonly double _oldX, _oldY, _newX, _newY;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public MoveCommand(ElementModel em, double oldX, double oldY, double newX, double newY,
        System.Action? onChange = null, string? desc = null)
    {
        _em = em;
        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;
        _onChange = onChange;
        Description = desc ?? $"移动 {em.Type}";
    }

    public void Undo() { _em.X = _oldX; _em.Y = _oldY; _onChange?.Invoke(); }
    public void Redo() { _em.X = _newX; _em.Y = _newY; _onChange?.Invoke(); }
}

internal sealed class ResizeCommand : IUndoCommand
{
    private readonly ElementModel _em;
    private readonly double _oldX, _oldY, _oldW, _oldH, _newX, _newY, _newW, _newH;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public ResizeCommand(ElementModel em,
        double oldX, double oldY, double oldW, double oldH,
        double newX, double newY, double newW, double newH,
        System.Action? onChange = null, string? desc = null)
    {
        _em = em;
        _oldX = oldX; _oldY = oldY; _oldW = oldW; _oldH = oldH;
        _newX = newX; _newY = newY; _newW = newW; _newH = newH;
        _onChange = onChange;
        Description = desc ?? $"缩放 {_em.Type}";
    }

    public void Undo() { _em.X = _oldX; _em.Y = _oldY; _em.Width = _oldW; _em.Height = _oldH; _onChange?.Invoke(); }
    public void Redo() { _em.X = _newX; _em.Y = _newY; _em.Width = _newW; _em.Height = _newH; _onChange?.Invoke(); }
}

internal sealed class PropertyChangeCommand : IUndoCommand
{
    private readonly ElementModel _em;
    private readonly string _propKey;
    private readonly string? _oldValue, _newValue;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public PropertyChangeCommand(ElementModel em, string propKey, string? oldValue, string? newValue,
        System.Action? onChange = null, string? desc = null)
    {
        _em = em; _propKey = propKey;
        _oldValue = oldValue; _newValue = newValue;
        _onChange = onChange;
        Description = desc ?? $"修改 {propKey}";
    }

    public void Undo()
    {
        SetProp(_oldValue);
        _onChange?.Invoke();
    }

    public void Redo()
    {
        SetProp(_newValue);
        _onChange?.Invoke();
    }

    private void SetProp(string? value)
    {
        if (value is null) _em.Props.Remove(_propKey);
        else _em.Props[_propKey] = value;
    }
}

internal sealed class InsertElementCommand : IUndoCommand
{
    private readonly System.Collections.ObjectModel.ObservableCollection<ElementModel> _collection;
    private readonly ElementModel _em;
    private readonly int _index;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public InsertElementCommand(
        System.Collections.ObjectModel.ObservableCollection<ElementModel> collection,
        ElementModel em, int index, System.Action? onChange = null, string? desc = null)
    {
        _collection = collection; _em = em; _index = index;
        _onChange = onChange;
        Description = desc ?? $"插入 {em.Type}";
    }

    public void Undo() { _collection.RemoveAt(_index); _onChange?.Invoke(); }
    public void Redo() { _collection.Insert(_index, _em); _onChange?.Invoke(); }
}

internal sealed class DeleteElementCommand : IUndoCommand
{
    private readonly System.Collections.ObjectModel.ObservableCollection<ElementModel> _collection;
    private readonly ElementModel _em;
    private readonly int _index;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public DeleteElementCommand(
        System.Collections.ObjectModel.ObservableCollection<ElementModel> collection,
        ElementModel em, int index, System.Action? onChange = null, string? desc = null)
    {
        _collection = collection; _em = em; _index = index;
        _onChange = onChange;
        Description = desc ?? $"删除 {em.Type}";
    }

    public void Undo() { _collection.Insert(_index, _em); _onChange?.Invoke(); }
    public void Redo() { _collection.RemoveAt(_index); _onChange?.Invoke(); }
}

internal sealed class RotateCommand : IUndoCommand
{
    private readonly ElementModel _em;
    private readonly double _oldAngle, _newAngle;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public RotateCommand(ElementModel em, double oldAngle, double newAngle,
        System.Action? onChange = null, string? desc = null)
    {
        _em = em; _oldAngle = oldAngle; _newAngle = newAngle;
        _onChange = onChange;
        Description = desc ?? $"旋转 {em.Type}";
    }

    public void Undo() { _em.Rotation = _oldAngle; _onChange?.Invoke(); }
    public void Redo() { _em.Rotation = _newAngle; _onChange?.Invoke(); }
}

// ── legacy commands kept for existing VM compatibility ──

internal sealed class CollectionInsertCommand<T> : IUndoCommand
{
    private readonly IList<T> _collection;
    private readonly T _item;
    private readonly int _index;
    public string Description { get; }

    public CollectionInsertCommand(IList<T> collection, T item, int index, string? desc = null)
    {
        _collection = collection; _item = item; _index = index;
        Description = desc ?? $"添加 {typeof(T).Name}";
    }

    public void Undo() => _collection.RemoveAt(_index);
    public void Redo() => _collection.Insert(_index, _item);
}

internal sealed class CollectionRemoveCommand<T> : IUndoCommand
{
    private readonly IList<T> _collection;
    private readonly T _item;
    private readonly int _index;
    public string Description { get; }

    public CollectionRemoveCommand(IList<T> collection, T item, int index, string? desc = null)
    {
        _collection = collection; _item = item; _index = index;
        Description = desc ?? $"删除 {typeof(T).Name}";
    }

    public void Undo() => _collection.Insert(_index, _item);
    public void Redo() => _collection.RemoveAt(_index);
}

/// <summary>Move an item inside an ObservableCollection (drag-reorder, 层级调整). Undo = reverse move.</summary>
internal sealed class CollectionMoveCommand<T> : IUndoCommand
{
    private readonly System.Collections.ObjectModel.ObservableCollection<T> _collection;
    private readonly int _from, _to;
    private readonly System.Action? _onChange;

    public string Description { get; }

    public CollectionMoveCommand(System.Collections.ObjectModel.ObservableCollection<T> collection,
        int from, int to, System.Action? onChange = null, string? desc = null)
    {
        _collection = collection; _from = from; _to = to;
        _onChange = onChange;
        Description = desc ?? "移动顺序";
    }

    public void Undo()
    {
        if (_to >= 0 && _to < _collection.Count && _from >= 0 && _from < _collection.Count)
            _collection.Move(_to, _from);
        _onChange?.Invoke();
    }

    public void Redo()
    {
        if (_from >= 0 && _from < _collection.Count && _to >= 0 && _to < _collection.Count)
            _collection.Move(_from, _to);
        _onChange?.Invoke();
    }
}

/// <summary>Composite/free-form command for multi-part edits (e.g. 表格加行 = 改定义 + 加单元格).</summary>
internal sealed class DelegateUndoCommand : IUndoCommand
{
    private readonly System.Action _undo, _redo;

    public string Description { get; }

    public DelegateUndoCommand(System.Action undo, System.Action redo, string desc)
    {
        _undo = undo; _redo = redo;
        Description = desc;
    }

    public void Undo() => _undo();
    public void Redo() => _redo();
}

internal sealed class TextChangeCommand : IUndoCommand
{
    private readonly System.Func<string> _getter;
    private readonly System.Action<string> _setter;
    private readonly System.Action? _afterUndo;
    private readonly string _old, _new;

    public string Description { get; }

    public TextChangeCommand(System.Func<string> getter, System.Action<string> setter,
        string oldVal, string newVal, System.Action? afterUndo = null, string? desc = null)
    {
        _getter = getter; _setter = setter;
        _old = oldVal; _new = newVal;
        _afterUndo = afterUndo;
        Description = desc ?? "文本修改";
    }

    public void Undo() { _setter(_old); _afterUndo?.Invoke(); }
    public void Redo() { _setter(_new); _afterUndo?.Invoke(); }
}
