using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Read-only, allocation-free view of a model's events for one frame. Loop with
    // for (int i = 0; i < events.Count; i++) or foreach (no garbage either way). The model reuses the same
    // list every frame, so the contents are only valid until its next Tick.
    public readonly struct EventList<T>
    {
        readonly List<T> list;

        public EventList(List<T> list)
        {
            this.list = list;
        }

        public int Count => list == null ? 0 : list.Count;
        public T this[int index] => list[index];

        public List<T>.Enumerator GetEnumerator()
        {
            return (list ?? EmptyList).GetEnumerator();
        }

        static readonly List<T> EmptyList = new List<T>();
    }
}
