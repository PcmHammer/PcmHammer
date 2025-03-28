using PcmHacking.UnoUI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record LogModel
{
    private readonly IConnectionService connectionService;
    private readonly ILogBuffer LogBuffer;

    public IState<IEnumerable<LogEntry>> LogEntries => State<IEnumerable<LogEntry>>.Value(this, () => this.LogBuffer.LogEntries);

    public LogModel(IConnectionService connectionService, ILogBuffer logBuffer)
    {
        this.connectionService = connectionService;
        this.LogBuffer = logBuffer;
    }

    [Command]
    public void Clear()
    {
        this.LogBuffer.Clear();
    }

    [Command]
    public async Task Save()
    {
        // In order to enumerate the log entries, we need to pause polling, so that the collection doesn't get modified.
        using (var lease = await this.connectionService.BeginActivity("Saving log"))
        {
            StringBuilder builder = new StringBuilder();
            foreach (LogEntry entry in this.LogBuffer.LogEntries)
            {
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} {1,5} {2}", entry.Time, entry.Type, entry.Message);
                builder.AppendLine(entry.ToString());
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            await File.WriteAllTextAsync($"pcm-hammer-log-{timestamp}.txt", builder.ToString());
        }
    }
}    