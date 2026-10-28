namespace SPlayer;

public enum RepeatMode
{
    None,
    One,
    All,
}

public enum AdvanceKind
{
    Stop,
    Replay,
    Goto,
}

public readonly record struct Advance(AdvanceKind Kind, int Index);

/// <summary>The in-memory play queue. Shuffle order is rebuilt when the queue or the shuffle flag changes.</summary>
public sealed class Playlist
{
    private readonly List<string> _items = [];
    private readonly List<int> _shuffle = [];
    private readonly List<int> _history = [];
    private int _shuffleCursor;
    private readonly Random _random = new();

    public IReadOnlyList<string> Items => _items;
    public int Index { get; private set; } = -1;
    public RepeatMode Repeat { get; set; } = RepeatMode.None;
    public bool Shuffle { get; private set; }
    public string? Current => Index >= 0 && Index < _items.Count ? _items[Index] : null;

    public void Replace(IEnumerable<string> paths, int index = 0)
    {
        _items.Clear();
        _items.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)));
        Index = _items.Count == 0 ? -1 : Math.Clamp(index, 0, _items.Count - 1);
        _history.Clear();
        RebuildShuffle();
    }

    public void Add(IEnumerable<string> paths)
    {
        var any = false;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            _items.Add(path);
            any = true;
        }
        if (any && Index < 0 && _items.Count > 0) Index = 0;
        if (any) RebuildShuffle();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _items.RemoveAt(index);
        if (_items.Count == 0) Index = -1;
        else if (Index > index) Index--;
        else if (Index >= _items.Count) Index = _items.Count - 1;
        _history.Clear();
        RebuildShuffle();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _items.Count || to >= _items.Count || from == to) return;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        if (Index == from) Index = to;
        else if (from < Index && to >= Index) Index--;
        else if (from > Index && to <= Index) Index++;
        _history.Clear();
        RebuildShuffle();
    }

    /// <summary>Applies a drag reorder already performed by the list. The item that was current stays current.</summary>
    public void SetOrder(IReadOnlyList<string> paths)
    {
        var current = Current;
        _items.Clear();
        _items.AddRange(paths);
        if (_items.Count == 0) Index = -1;
        else if (current is null) Index = 0;
        else
        {
            var found = _items.FindIndex(p => MediaPaths.Same(p, current));
            Index = found >= 0 ? found : 0;
        }
        _history.Clear();
        RebuildShuffle();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        if (Index >= 0 && Index != index) _history.Add(Index);
        if (Index != index)
        {
            Index = index;
            RebuildShuffle();
        }
    }

    public void SetShuffle(bool enabled)
    {
        Shuffle = enabled;
        RebuildShuffle();
    }

    public Advance PlanOnEnd()
    {
        if (_items.Count == 0 || Index < 0) return new Advance(AdvanceKind.Stop, -1);
        if (Repeat == RepeatMode.One) return new Advance(AdvanceKind.Replay, Index);
        var next = TakeForward();
        return next is int index ? new Advance(AdvanceKind.Goto, index) : new Advance(AdvanceKind.Stop, -1);
    }

    public Advance PlanNext()
    {
        if (_items.Count == 0 || Index < 0) return new Advance(AdvanceKind.Stop, -1);
        var next = TakeForward();
        return next is int index ? new Advance(AdvanceKind.Goto, index) : new Advance(AdvanceKind.Stop, -1);
    }

    public Advance PlanPrevious(bool restartCurrent)
    {
        if (_items.Count == 0 || Index < 0) return new Advance(AdvanceKind.Stop, -1);
        if (restartCurrent) return new Advance(AdvanceKind.Replay, Index);
        if (_history.Count > 0)
        {
            var previous = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            if (previous >= 0 && previous < _items.Count)
            {
                if (Shuffle)
                {
                    _shuffle.Insert(_shuffleCursor, Index);
                }
                Index = previous;
                return new Advance(AdvanceKind.Goto, previous);
            }
        }
        if (!Shuffle && Index > 0)
        {
            Index--;
            return new Advance(AdvanceKind.Goto, Index);
        }
        if (!Shuffle && Repeat == RepeatMode.All)
        {
            Index = _items.Count - 1;
            return new Advance(AdvanceKind.Goto, Index);
        }
        return new Advance(AdvanceKind.Replay, Index);
    }

    public void NoteAdvancedTo(int index) => Index = index;

    private int? TakeForward()
    {
        if (Shuffle)
        {
            if (_shuffleCursor >= _shuffle.Count)
            {
                if (Repeat != RepeatMode.All) return null;
                if (_items.Count <= 1) return Index;
                RebuildShuffle();
            }
            if (_shuffleCursor >= _shuffle.Count) return Index;
            var index = _shuffle[_shuffleCursor++];
            if (Index >= 0) _history.Add(Index);
            Index = index;
            return index;
        }
        var next = Index + 1;
        if (next >= _items.Count)
        {
            if (Repeat != RepeatMode.All) return null;
            next = 0;
        }
        if (Index >= 0) _history.Add(Index);
        Index = next;
        return next;
    }

    private void RebuildShuffle()
    {
        _shuffle.Clear();
        _shuffleCursor = 0;
        if (!Shuffle) return;
        for (var i = 0; i < _items.Count; i++)
        {
            if (i != Index) _shuffle.Add(i);
        }
        for (var i = _shuffle.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_shuffle[i], _shuffle[j]) = (_shuffle[j], _shuffle[i]);
        }
    }
}

public static class PlaylistFile
{
    public static IReadOnlyList<string> Read(string path)
    {
        var text = File.ReadAllText(path);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(text) ?? [];
            return list.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        }
        var folder = Path.GetDirectoryName(path) ?? "";
        var items = new List<string>();
        foreach (var raw in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!Path.IsPathRooted(line) && !MediaPaths.IsHttp(line)) line = Path.GetFullPath(Path.Combine(folder, line));
            items.Add(line);
        }
        return items;
    }

    public static void Write(string path, IReadOnlyList<string> items)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            AtomicFile.Write(path, System.Text.Json.JsonSerializer.Serialize(items, JsonOptions.Indented));
            return;
        }
        var body = "#EXTM3U" + Environment.NewLine + string.Join(Environment.NewLine, items) + Environment.NewLine;
        AtomicFile.Write(path, body);
    }
}
