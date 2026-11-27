namespace VuSnap.Core;

/// <summary>Bounded, deterministic history of immutable editor states. UI owns state capture and restoration.</summary>
public sealed class CommandHistory<T>
{
    private readonly List<T> _states;
    private readonly int _capacity;
    private readonly IEqualityComparer<T> _comparer;
    private int _index;
    public CommandHistory(T initial, int capacity = 50, IEqualityComparer<T>? comparer = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _states = new List<T> { initial };
    }
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index < _states.Count - 1;
    public T Current => _states[_index];
    public bool Record(T state)
    {
        if (_comparer.Equals(Current, state)) return false;
        if (CanRedo) _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        _states.Add(state);
        _index++;
        if (_states.Count > _capacity + 1) { _states.RemoveAt(0); _index--; }
        return true;
    }
    public bool TryUndo(out T state)
    {
        if (!CanUndo) { state = Current; return false; }
        state = _states[--_index]; return true;
    }
    public bool TryRedo(out T state)
    {
        if (!CanRedo) { state = Current; return false; }
        state = _states[++_index]; return true;
    }
}
