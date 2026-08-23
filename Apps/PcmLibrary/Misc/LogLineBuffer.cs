// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking;

/// <summary>
/// Thread-safe store of log lines, shared by the WinForms LogListView and the WPF results / debug /
/// bus-monitor panes. Producers on any thread <see cref="Append"/> lines into a queue; the UI thread
/// <see cref="Drain"/>s them into the committed list on its own schedule (a timer). A busy bus emitting
/// thousands of lines a second therefore never blocks the producer and never touches UI state
/// off-thread.
/// <para>
/// This is the model only: it holds the lines and bounds their number. Rendering - GDI paint in
/// WinForms, a virtualized ItemsControl in WPF - stays in each front end, reading <see cref="Lines"/>
/// by index so its cost is independent of how many lines are held.
/// </para>
/// </summary>
public sealed class LogLineBuffer
{
    private readonly ConcurrentQueue<string> pending = new();

    // The committed log. Only touched on the UI thread (Drain, Lines, Count, Clear, Snapshot).
    private readonly List<string> committed = new();

    /// <summary>What a <see cref="Drain"/> changed, so a view can update without rescanning.</summary>
    public readonly struct DrainResult
    {
        public DrainResult(int addedCount, int trimmedFromStart)
        {
            this.AddedCount = addedCount;
            this.TrimmedFromStart = trimmedFromStart;
        }

        /// <summary>Lines appended to the end this drain (after any trim).</summary>
        public int AddedCount { get; }

        /// <summary>Oldest lines removed from the front to honour <see cref="MaxLines"/>.</summary>
        public int TrimmedFromStart { get; }

        /// <summary>True when the committed list changed at all.</summary>
        public bool Changed => this.AddedCount > 0 || this.TrimmedFromStart > 0;
    }

    /// <summary>Cap on committed lines; the oldest are dropped past it. 0 (the default) is unlimited.</summary>
    public int MaxLines { get; set; }

    /// <summary>UI thread. Random-access committed lines, for a virtualized view or paint.</summary>
    public IReadOnlyList<string> Lines => this.committed;

    /// <summary>UI thread. Committed line count.</summary>
    public int Count => this.committed.Count;

    /// <summary>Any thread. True when a drain would move something.</summary>
    public bool HasPending => !this.pending.IsEmpty;

    /// <summary>Any thread, non-blocking. Queues a line for the next drain.</summary>
    public void Append(string line) => this.pending.Enqueue(line ?? string.Empty);

    /// <summary>
    /// UI thread. Moves queued lines into the committed list and enforces <see cref="MaxLines"/> in one
    /// pass. Returns what changed so the caller can update a view incrementally.
    /// </summary>
    public DrainResult Drain()
    {
        int added = 0;
        while (this.pending.TryDequeue(out string? line))
        {
            this.committed.Add(line);
            added++;
        }

        int trimmed = 0;
        if (this.MaxLines > 0 && this.committed.Count > this.MaxLines)
        {
            trimmed = this.committed.Count - this.MaxLines;
            this.committed.RemoveRange(0, trimmed);
        }

        return new DrainResult(added, trimmed);
    }

    /// <summary>UI thread. Discards pending and committed lines.</summary>
    public void Clear()
    {
        while (this.pending.TryDequeue(out _))
        {
        }

        this.committed.Clear();
    }

    /// <summary>
    /// UI thread. The whole log as one newline-separated string, for saving. Drains first so the most
    /// recent lines - the tail of a capture - are never stranded in the queue.
    /// </summary>
    public string Snapshot()
    {
        this.Drain();

        StringBuilder builder = new();
        foreach (string line in this.committed)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
