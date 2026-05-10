using PcmHacking;
using System.Diagnostics;
using System.Text;

namespace PcmHacking.CLI;

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
        private readonly LogLevels _logLevel;

        public LogMessageHandler(LogLevels desiredLogLevel, string prefix, bool logToFileEnabled, bool displayTimestamps, bool isConsole) {
            _logPrefix = prefix;
            _logger = new LogEntryStreamHandler(desiredLogLevel, prefix, logToFileEnabled, displayTimestamps, isConsole);
            _logLevel = desiredLogLevel;
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
            string[] lines = _logger.GetLogs(_logLevel).Select(x => $"{x.TimeStamp:dddd, MMMM dd yyyy @hh:mm:ss:ff} - {x.Message}").ToArray();
            StringBuilder stringBuilder = new();
            foreach (string line in lines) {
                stringBuilder.AppendLine(line);
            }
            return stringBuilder.ToString();
        }
        public void AddDebugMessage(string message) {
            _logger.Write(LogLevels.Debug, message, _logPrefix);
        }

        public void AddUserMessage(string message, LogLevels desiredLevel = LogLevels.Info) {
            _logger.Write(desiredLevel, message, _logPrefix);
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
    private readonly Dictionary<LogLevels, CircularBuffer<LogEntry>> _logEntries;
    private readonly string _logPrefix;
    private readonly bool _displayTimestamps;
    private readonly bool _isConsole;
    private readonly LogLevels _logLevel;
    private readonly TextWriter? _logWriter;
    private protected object _lock = new();

    public LogEntryStreamHandler(LogLevels logLevel, string logPrefix, bool logToFile, bool displayTimestamps, bool isConsole)
    {
        _logEntries = [];
        _logLevel = logLevel;
        _logPrefix = logPrefix;
        if (logToFile)
        {
            try
            {
                FileStream fs = File.Open(@$"{AppContext.BaseDirectory}/{_logPrefix}_{DateTime.Now.ToString("yyyyMMdd_HHmmssff")}.log", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
                _logWriter = new StreamWriter(fs);
            }
            catch
            {
                _logWriter = null;
            }
        }
        _displayTimestamps = displayTimestamps;
        _isConsole = isConsole;
    }

    public void Write(LogLevels level, string entry, string source)
    {
        if (!_logEntries.TryGetValue(level, out CircularBuffer<LogEntry>? value))
        {
            value = new CircularBuffer<LogEntry>(2000);
            _logEntries.Add(level, value);
        }
        LogEntry logEntry = new(source, entry);
        value.Add(logEntry);
        string timestampPrefix = _displayTimestamps ? $"{logEntry.TimeStamp.ToString("MMMM-dd-yyyy@hh:mm:ss:fftt")} - " : string.Empty; 
        string msg = $"{timestampPrefix}{source}.{level}: {entry}";
        _logWriter?.WriteLine(msg); // Always write everything to file - Excel or the likes of can sort/filter by level.
        if (_logLevel >= level)
        {
            if (_isConsole)
            {
                Console.WriteLine(msg); // Only print desired to console. TODO: We can add a flag to override this for GUI applications, should this logger be used there!
            }
            else
            {
                Debug.WriteLine(msg);
            }
        }
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

    public List<LogEntry> GetUserLogs() => [.. _logEntries[LogLevels.Info]];

    public List<LogEntry> GetLogs(LogLevels threshold)
    {
        var result = _logEntries.Where(x => x.Key <= threshold).Select(x => x.Value).ToList();
        List<LogEntry> logCollection = [];
        result.ForEach(x => logCollection.AddRange(x));
        logCollection.Sort(new LogTimeStampComparer());
        return logCollection;
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
