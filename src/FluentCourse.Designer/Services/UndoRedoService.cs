using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentCourse.Designer.Services;

public interface IUndoCommand
{
    string Description { get; }
    void Undo();
    void Redo();
}

/// <summary>
/// Simple instruction-stack undo/redo. Commands are coarse-grained: a "page added" undo
/// removes the page; a "text changed" restores the previous value. Fine-grained per-keystroke
/// undo would multiply storage — for a WYSIWYG layout editor coarse is enough.
/// </summary>
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

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undo.Pop();
        cmd.Undo();
        _redo.Push(cmd);
        Notify();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redo.Pop();
        cmd.Redo();
        _undo.Push(cmd);
        Notify();
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

// ── concrete commands ──

internal sealed class PropertyChangeCommand<T> : IUndoCommand
{
    private readonly object _target;
    private readonly string _propName;
    private readonly T _oldVal, _newVal;
    private readonly Action<object, string, T> _setter;

    public string Description { get; }

    public PropertyChangeCommand(object target, string propName, T oldVal, T newVal,
        Action<object, string, T> setter, string? desc = null)
    {
        _target = target;
        _propName = propName;
        _oldVal = oldVal;
        _newVal = newVal;
        _setter = setter;
        Description = desc ?? $"修改 {propName}";
    }

    public void Undo() => _setter(_target, _propName, _oldVal);
    public void Redo() => _setter(_target, _propName, _newVal);
}

internal sealed class TextChangeCommand : IUndoCommand
{
    private readonly Func<string> _getter;
    private readonly Action<string> _setter;
    private readonly Action? _afterUndo;
    private readonly string _old, _new;

    public string Description { get; }

    public TextChangeCommand(Func<string> getter, Action<string> setter,
        string oldVal, string newVal, Action? afterUndo = null, string? desc = null)
    {
        _getter = getter;
        _setter = setter;
        _old = oldVal;
        _new = newVal;
        _afterUndo = afterUndo;
        Description = desc ?? "文本修改";
    }

    public void Undo() { _setter(_old); _afterUndo?.Invoke(); }
    public void Redo() { _setter(_new); _afterUndo?.Invoke(); }

    public string OldValue => _old;
    public string NewValue => _new;
}

internal sealed class CollectionInsertCommand<T> : IUndoCommand
{
    private readonly IList<T> _collection;
    private readonly T _item;
    private readonly int _index;
    public string Description { get; }

    public CollectionInsertCommand(IList<T> collection, T item, int index, string? desc = null)
    {
        _collection = collection;
        _item = item;
        _index = index;
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
        _collection = collection;
        _item = item;
        _index = index;
        Description = desc ?? $"删除 {typeof(T).Name}";
    }

    public void Undo() => _collection.Insert(_index, _item);
    public void Redo() => _collection.RemoveAt(_index);
}

internal sealed class CompositeCommand : IUndoCommand
{
    private readonly IUndoCommand[] _commands;
    public string Description { get; }

    public CompositeCommand(string description, params IUndoCommand[] commands)
    {
        Description = description;
        _commands = commands;
    }

    public void Undo() { for (int i = _commands.Length - 1; i >= 0; i--) _commands[i].Undo(); }
    public void Redo() { foreach (var c in _commands) c.Redo(); }
}
