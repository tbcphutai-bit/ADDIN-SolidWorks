using System;
using System.Collections.Generic;
using System.Threading;

namespace ADDIN.Commands.BendAssembly
{
    // Session-owned, bounded LRU and single-flight. Values/guards MUST contain
    // managed snapshots/leases only. No lock is held while producing a value.
    internal sealed class AssemblyManagedCache<T>
    {
        private sealed class Entry
        {
            internal T Value;
            internal Func<bool> Current;
            internal long Bytes;
            internal bool Pending;
            internal LinkedListNode<string> Node;
        }
        private readonly object gate = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly LinkedList<string> order = new LinkedList<string>();
        private readonly int maximumCount;
        private readonly long maximumBytes;
        private long bytes, hits, misses, waits, evictions;
        internal AssemblyManagedCache(int maximumCount, long maximumBytes)
        {
            if (maximumCount < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException();
            this.maximumCount = maximumCount; this.maximumBytes = maximumBytes;
        }
        internal long Hits => Interlocked.Read(ref hits);
        internal long Misses => Interlocked.Read(ref misses);
        internal long Waits => Interlocked.Read(ref waits);
        internal long Evictions => Interlocked.Read(ref evictions);
        internal long Bytes { get { lock (gate) return bytes; } }
        internal int Count { get { lock (gate) return entries.Count; } }
        internal T Get(string key, Func<T> produce, Func<T, bool> valid, Func<T, long> weight, CancellationToken token, out bool hit)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Verified cache key required");
            Entry mine;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    Entry found;
                    if (entries.TryGetValue(key, out found))
                    {
                        if (found.Pending) { Interlocked.Increment(ref waits); Monitor.Wait(gate, 10); continue; }
                        bool current; try { current = found.Current(); } catch { current = false; }
                        if (current)
                        {
                            order.Remove(found.Node); order.AddLast(found.Node); Interlocked.Increment(ref hits); hit = true; return found.Value;
                        }
                        Remove(key, found);
                    }
                    // Pending work also consumes an entry; bound memory under
                    // concurrent distinct misses rather than evicting a flight.
                    while (entries.Count >= maximumCount && order.First != null) Remove(order.First.Value, entries[order.First.Value]);
                    if (entries.Count >= maximumCount) { Monitor.Wait(gate, 10); continue; }
                    mine = new Entry { Pending = true }; entries.Add(key, mine); Interlocked.Increment(ref misses); break;
                }
            }
            T value; long size; bool acceptable;
            try { value = produce(); size = Math.Max(1, weight(value)); acceptable = valid(value); }
            catch { lock (gate) { entries.Remove(key); Monitor.PulseAll(gate); } throw; }
            lock (gate)
            {
                mine.Pending = false;
                if (!token.IsCancellationRequested && acceptable && size <= maximumBytes)
                {
                    while (bytes + size > maximumBytes && order.First != null) Remove(order.First.Value, entries[order.First.Value]);
                    mine.Value = value; mine.Current = () => valid(value); mine.Bytes = size; mine.Node = order.AddLast(key); bytes += size;
                }
                else entries.Remove(key);
                Monitor.PulseAll(gate);
            }
            hit = false; return value;
        }
        private void Remove(string key, Entry entry)
        {
            entries.Remove(key); if (entry.Node != null) order.Remove(entry.Node); bytes -= entry.Bytes; Interlocked.Increment(ref evictions);
        }
        // In-flight producers may finish, but cannot be published by a canceled
        // coordinator. Clear completed entries only; do not lose flight ownership.
        internal void Clear()
        { lock (gate) { foreach (string key in new List<string>(order)) Remove(key, entries[key]); Monitor.PulseAll(gate); } }
    }
}
