// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using PcmHacking;

namespace PCMHammer.Helpers;

/// <summary>
/// Binds a <see cref="LogLineBuffer"/> (the shared, UI-agnostic line store) to a WPF items control.
/// A background producer <see cref="Append"/>s lines from any thread; a UI-thread timer drains them in
/// batches and raises one collection-changed notification per batch, so a virtualized ListBox realizes
/// only the visible rows and its cost stays flat however large the log grows.
/// <para>
/// It implements <see cref="IList"/> deliberately: WPF only UI-virtualizes a source it can index, so an
/// IEnumerable/IReadOnlyList source would be copied wholesale on every reset - the very O(n)-per-tick
/// cost this design removes. The list is read-only; the mutators exist only to satisfy the interface.
/// </para>
/// <para>
/// Backs the Results, Debug and Bus Monitor panes, exposing the same Start/Stop/Flush/Append/Clear/
/// Snapshot surface the old LogTextBuffer did, so the panes changed type, not shape.
/// </para>
/// </summary>
public sealed class LogLinesSource :
    IList<string>, IList, IReadOnlyList<string>, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
{
    // Bounds memory only; virtualization makes display cost independent of it. The Bus Monitor raises
    // this to hold a whole PCM read; Results and Debug keep this generous default.
    private const int DefaultMaxLines = 200_000;

    private readonly LogLineBuffer _buffer = new();
    private readonly DispatcherTimer _flushTimer;
    private bool _isDisposed;

    public LogLinesSource(int maxLines = DefaultMaxLines)
    {
        _buffer.MaxLines = maxLines;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _flushTimer.Tick += (s, e) => Flush();
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Any thread, non-blocking. Queues a line for the next drain.</summary>
    public void Append(string line) => _buffer.Append(line);

    /// <summary>UI thread. Only runs while a producer is active; the Bus Monitor tab is usually idle.</summary>
    public void Start() => _flushTimer.Start();

    /// <summary>UI thread. Flush() first to publish what is still queued.</summary>
    public void Stop() => _flushTimer.Stop();

    /// <summary>UI thread. Drain queued lines now and notify, instead of waiting for the next tick.</summary>
    public void Flush()
    {
        if (!_buffer.HasPending)
        {
            return;
        }

        if (_buffer.Drain().Changed)
        {
            Notify();
        }
    }

    /// <summary>UI thread. Discard the whole log.</summary>
    public void Clear()
    {
        _buffer.Clear();
        Notify();
    }

    /// <summary>UI thread. Allocates the whole log; for saving. Drains first so the tail is included.</summary>
    public string Snapshot() => _buffer.Snapshot();

    // One Reset per batch rather than an event per line: a virtualized, index-backed list re-reads only
    // the visible rows, so a Reset is cheap, and range Add is not supported by WPF's item collection.
    private void Notify()
    {
        PropertyChanged?.Invoke(this, CountChangedArgs);
        PropertyChanged?.Invoke(this, IndexerChangedArgs);
        CollectionChanged?.Invoke(this, ResetArgs);
    }

    private static readonly PropertyChangedEventArgs CountChangedArgs = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChangedArgs = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetArgs = new(NotifyCollectionChangedAction.Reset);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _flushTimer.Stop();
    }

    #region Read-only list surface (consumed by the WPF binding; writes are not supported)

    private IReadOnlyList<string> Lines => _buffer.Lines;

    public int Count => _buffer.Count;

    public bool IsReadOnly => true;

    bool IList.IsFixedSize => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    public string this[int index]
    {
        get => Lines[index];
        set => throw new NotSupportedException();
    }

    object? IList.this[int index]
    {
        get => Lines[index];
        set => throw new NotSupportedException();
    }

    public int IndexOf(string item)
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i] == item)
            {
                return i;
            }
        }

        return -1;
    }

    int IList.IndexOf(object? value) => value is string s ? IndexOf(s) : -1;

    public bool Contains(string item) => IndexOf(item) >= 0;

    bool IList.Contains(object? value) => value is string s && Contains(s);

    public void CopyTo(string[] array, int arrayIndex)
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            array[arrayIndex + i] = Lines[i];
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            array.SetValue(Lines[i], index + i);
        }
    }

    public IEnumerator<string> GetEnumerator() => Lines.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Mutators: present only to satisfy IList/IList<string>. The log is append-only through Append and
    // cleared through Clear; the collection is otherwise read-only to its consumer.
    void ICollection<string>.Add(string item) => throw new NotSupportedException();

    int IList.Add(object? value) => throw new NotSupportedException();

    void ICollection<string>.Clear() => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    void IList<string>.Insert(int index, string item) => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    bool ICollection<string>.Remove(string item) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList<string>.RemoveAt(int index) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    #endregion
}
