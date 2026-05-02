using PcmHacking;
using System.Diagnostics;
using System.Text;

namespace PcmHacking.CLI;

    public enum LogLevel {
        Unspecified,
        Info,
        Error
    }

    public class LogEntry {
        public DateTime TimeStamp;
        public string Message;
        public string Source;

        public LogEntry(string source, string message) {
            this.Source = source;
            this.Message = message;
            TimeStamp = DateTime.Now;
        }
    }

    public class LogMessageHandler : ILogger {
        private readonly LogEntryStreamHandler _logger;
        private readonly string _logPrefix;
        public LogMessageHandler(string prefix, bool logToFileEnabled) {
            _logPrefix = prefix;
            _logger = new LogEntryStreamHandler(prefix, logToFileEnabled);
        }

        public string GetUserMessageString() {
            string[] lines = _logger.GetUserLogs().Select(x => $"{x.TimeStamp:dddd, MMMM dd yyyy @hh:mm:ss:ff}-{x.Message}").ToArray();
            StringBuilder stringBuilder = new();
            foreach (string line in lines) {
                stringBuilder.AppendLine(line);
            }
            return stringBuilder.ToString();
        }

        public string GetDebugMessageString() {
            string[] lines = _logger.GetDebugLogs().Select(x => $"{x.TimeStamp:dddd, MMMM dd yyyy @hh:mm:ss:ff} - {x.Message}").ToArray();
            StringBuilder stringBuilder = new();
            foreach (string line in lines) {
                stringBuilder.AppendLine(line);
            }
            return stringBuilder.ToString();
        }
        public void AddDebugMessage(string message) {
            _logger.Write(LogLevel.Error, message, _logPrefix);
        }

        public void AddUserMessage(string message) {
            _logger.Write(LogLevel.Info, message, _logPrefix);
        }

        public void StatusUpdateActivity(string activity) {
        }

        public void StatusUpdateKbps(string Kbps) {
        }

        public void StatusUpdatePercentDone(string percent) {
        }

        public void StatusUpdateProgressBar(double completed, bool visible) {
        }

        public void StatusUpdateReset() {
        }

        public void StatusUpdateRetryCount(string retries) {
        }

        public void StatusUpdateTimeRemaining(string remaining) {
        }
    }

public class LogEntryStreamHandler : IDisposable
{
    private readonly Dictionary<LogLevel, List<LogEntry>> _logEntries;
    private readonly string _logPrefix;
    private TextWriter? _logWriter;
    private MemoryStream _backupLogStream;
    private System.Timers.Timer _flushTimer;
    private protected object _lock = new();
    private bool _fileError = false;

    public LogEntryStreamHandler(string logPrefix, bool logToFile)
    {
        _logEntries = [];
        _logPrefix = logPrefix;
        if (logToFile)
        {
            try
            {
                FileStream fs = File.Open(@$"{AppContext.BaseDirectory}/{_logPrefix}_{DateTime.Now.ToString("yyyyMMdd_HHmmssff")}.log", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
                _logWriter = new StreamWriter(fs);
                _flushTimer = new System.Timers.Timer(3000);
                _flushTimer.Elapsed += _flushTimer_Elapsed;
                _flushTimer.Start();
            }
            catch (Exception e)
            {
                _fileError = true;
                _logWriter = null;
            }
        }
        if (_fileError || !logToFile)
        {
            _backupLogStream = new MemoryStream();
            _logWriter = new StreamWriter(_backupLogStream);
            if (_fileError)
            {
            }
        }
    }

    private void _flushTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_logWriter != null)
        {
            Flush();
        }
    }

    public void Write(LogLevel level, string entry, string source)
    {
        if (_logWriter == null)
        {
            return;
        }
        if (!_logEntries.TryGetValue(level, out List<LogEntry>? value))
        {
            value = [];
            _logEntries.Add(level, value);
        }

        value.Add(new(source, entry));
        _logWriter.WriteLine($"{source}.{level}: {entry}");
        Console.WriteLine($"{source}.{level}: {entry}");
    }

    public void Flush()
    {
        if (_logWriter == null)
        {
            return;
        }
        lock (_lock)
        {
            try
            {
                _logWriter?.Flush();
            }
            catch { }
        }
    }

    public List<LogEntry> GetUserLogs() => _logEntries[LogLevel.Info];

    public List<LogEntry> GetDebugLogs()
    {
        List<LogEntry> result = [];
        if (_logEntries.ContainsKey(LogLevel.Info) && _logEntries[LogLevel.Info].Count > 0)
        {
            result.AddRange(_logEntries[LogLevel.Info]);
        }
        if (_logEntries.ContainsKey(LogLevel.Error) && _logEntries[LogLevel.Error].Count > 0)
        {
            result.AddRange(_logEntries[LogLevel.Error]);
        }
        result.Sort(new LogTimeStampComparer());
        return result;
    }

    public void Dispose()
    {
        _logWriter?.Flush();
        _logWriter?.Close();
        _logWriter?.Dispose();
        GC.SuppressFinalize(this);
    }

}

class LogTimeStampComparer : IComparer<LogEntry>
{
    public int Compare(LogEntry? x, LogEntry? y)
    {
        if (x == null || y == null) return 0;
        if (x.TimeStamp > y.TimeStamp) return 1;
        if (y.TimeStamp > x.TimeStamp) return -1;
        return 0;
    }
}
