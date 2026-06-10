// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.UnoUI.Services;
using Microsoft.UI.Dispatching;
using System.Text;
using Uno.Extensions.Reactive.Commands;
namespace PcmHacking.UnoUI.Presentation;


public partial record LogModel
{
    private readonly ISettingsService settingsService;
    private readonly IConnectionService connectionService;
    private readonly ILogBuffer LogBuffer;

    public IListState<LogEntry> LogEntries => ListState<LogEntry>.Empty(this);

    public LogModel(
        ISettingsService settingsService, 
        IConnectionService connectionService, 
        DispatcherQueue dispatcherQueue, 
        ILogBuffer logBuffer)
    {
        this.settingsService = settingsService;
        this.connectionService = connectionService;
        this.LogBuffer = logBuffer;
        dispatcherQueue.TryEnqueue(async () => await this.Load());
    }

    private async Task Load()
    {
        await this.LogEntries.Update(
            updater: existing => this.LogBuffer.LogEntries.ToImmutableList(),
            ct: CancellationToken.None);
    }

    [Command]
    public async Task Clear()
    {
        this.LogBuffer.Clear();
        await this.Load();
    }

    [Command]
    public async Task Save()
    {
        // In order to enumerate the log entries, we need to pause polling, so that the collection doesn't get modified.
        using (var lease = await this.connectionService.BeginActivity("Saving log"))
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<html>");
            foreach (LogEntry entry in this.LogBuffer.LogEntries)
            {
                builder.Append(string.Format("<br><span style=\"font-family:'Courier New'\">{0:yyyy-MM-dd HH:mm:ss}</span> ", entry.Time));
                switch(entry.Type)
                {
                    case LogType.User:
                        builder.Append("<b>");
                        builder.Append(entry.Message);
                        builder.Append("</b>");
                        break;

                    case LogType.Debug:
                        builder.Append(entry.Message);
                        break;
                }
                //string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} {1,5} {2}", entry.Time, entry.Type, entry.Message);
                //builder.AppendLine(entry.ToString());
            }
            builder.AppendLine("</html>");

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            string path = Path.Combine(this.settingsService.GetDataLogFolder(), $"pcm-hammer-log-{timestamp}.html");
            await File.WriteAllTextAsync(path, builder.ToString());
        }
    }
}    