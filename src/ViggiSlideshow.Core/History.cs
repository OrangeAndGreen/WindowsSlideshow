namespace ViggiSlideshow.Core;

/// <summary>
/// What has been shown, so Back can return to it. Going back and then forward replays the
/// same items before new ones are drawn.
/// </summary>
public sealed class History
{
    private readonly List<string> _items = new();
    private readonly int _capacity;
    private int _cursor = -1;

    public History(int capacity = 1000)
    {
        _capacity = Math.Max(1, capacity);
    }

    public string? Current => _cursor >= 0 ? _items[_cursor] : null;
    public int Count => _items.Count;

    /// <summary>How many items lie ahead of the current one, from having gone back.</summary>
    public int StepsBack => _items.Count - 1 - _cursor;

    public bool CanGoBack => _cursor > 0;

    public string? Back()
    {
        if (_cursor > 0)
            _cursor--;
        return Current;
    }

    /// <summary>Moves to the next item already seen, or returns null at the newest one.</summary>
    public string? Forward()
    {
        if (_cursor >= _items.Count - 1)
            return null;
        _cursor++;
        return Current;
    }

    /// <summary>Adds a newly drawn item after the newest one and makes it current.</summary>
    public void Push(string item)
    {
        _items.Add(item);
        _cursor = _items.Count - 1;

        if (_items.Count > _capacity)
        {
            int excess = _items.Count - _capacity;
            _items.RemoveRange(0, excess);
            _cursor -= excess;
        }
    }

    /// <summary>Drops an item that turned out to be unreadable, keeping the cursor on a neighbour.</summary>
    public void Remove(string item)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_items[i], item, StringComparison.OrdinalIgnoreCase))
                continue;
            _items.RemoveAt(i);
            if (i <= _cursor)
                _cursor--;
        }
        if (_cursor < 0 && _items.Count > 0)
            _cursor = 0;
    }
}
