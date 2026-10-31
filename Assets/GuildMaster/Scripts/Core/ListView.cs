using System.Collections;
using System.Collections.Generic;

namespace GuildMaster.Core
{
    /// <summary>
    /// Read-only view of a list that <c>foreach</c> walks without allocating (P5-2). A property typed
    /// <see cref="IReadOnlyList{T}"/> boxes the list's enumerator on every <c>foreach</c>; lists walked
    /// every frame by every unit are exposed as this instead. It still is an <see cref="IReadOnlyList{T}"/>,
    /// so LINQ and existing <c>IReadOnlyList</c> variables work as before (those box, as they did).
    /// </summary>
    public readonly struct ListView<T> : IReadOnlyList<T>
    {
        readonly List<T> _list;

        public ListView(List<T> list) => _list = list;

        public int Count => _list.Count;
        public T this[int index] => _list[index];
        public bool Contains(T item) => _list.Contains(item);
        public int IndexOf(T item) => _list.IndexOf(item);

        public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
