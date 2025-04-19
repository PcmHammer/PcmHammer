using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Services
{
    public enum LogType
    {
        Debug,
        User
    }

    public class LogEntry
    {
        public LogType Type { get; set; }
        public DateTime Time { get; set; }
        public required string Message { get; set; }
    }

    public interface ILogBuffer
    {
        public bool Enabled { get; set; }
        public IList<LogEntry> LogEntries { get; }
        void Add(LogType type, string message);
        void Clear();
    }

    public class LogBuffer : ILogBuffer
    {
        private readonly CircularBuffer<LogEntry> entries = new CircularBuffer<LogEntry>(5000);

        public IList<LogEntry> LogEntries => this.entries.ToArray();

        public bool Enabled { get; set; }

        public void Add(LogType type, string message)
        {
            if (!this.Enabled)
            {
                return;
            }

            this.entries.Add(new LogEntry
            {
                Type = type,
                Time = DateTime.Now,
                Message = message
            });
        }

        public void Clear()
        {
            this.entries.Clear();
        }
    }
}
