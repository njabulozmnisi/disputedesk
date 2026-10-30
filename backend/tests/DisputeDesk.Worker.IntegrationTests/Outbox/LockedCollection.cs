namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>The exporters add from whichever thread ends the span or log, so writes and reads take a lock.</summary>
public sealed class LockedCollection<T> : ICollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _gate = new();

    public int Count { get { lock (_gate) { return _items.Count; } } }

    public bool IsReadOnly => false;

    public IReadOnlyList<T> Snapshot() { lock (_gate) { return [.. _items]; } }

    public void Add(T item) { lock (_gate) { _items.Add(item); } }

    public void Clear() { lock (_gate) { _items.Clear(); } }

    public bool Contains(T item) { lock (_gate) { return _items.Contains(item); } }

    public void CopyTo(T[] array, int arrayIndex) { lock (_gate) { _items.CopyTo(array, arrayIndex); } }

    public bool Remove(T item) { lock (_gate) { return _items.Remove(item); } }

    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
