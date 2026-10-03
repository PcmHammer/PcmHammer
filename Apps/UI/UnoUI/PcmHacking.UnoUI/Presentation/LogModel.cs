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

    /// <summary>Where the log was written, or why it could not be. Empty until Save is used.</summary>
    public IState<string> SaveStatus => State<string>.Value(this, () => string.Empty);

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
        // No connection lease here. Saving a file needs no vehicle, and taking one made the log
        // impossible to save in exactly the situation it is needed: after a failed operation, when
        // the connection is gone, acquiring it throws and the button is left greyed out with no
        // explanation. LogEntries hands back a fresh array, so nothing can modify what is enumerated.
        try
        {
            IList<LogEntry> entries = this.LogBuffer.LogEntries;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<html>");
            foreach (LogEntry entry in entries)
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
            }
            builder.AppendLine("</html>");

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            string path = Path.Combine(ResolveLogFolder(), $"pcm-hammer-log-{timestamp}.html");

            // Previously unguarded: with no folder configured this combined the display placeholder
            // into a relative path, the write threw, and the button silently did nothing.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, builder.ToString());
            await this.SaveStatus.SetAsync($"Saved {entries.Count} lines to {path}");
        }
        catch (Exception exception)
        {
            await this.SaveStatus.SetAsync("Could not save the log: " + exception.Message);
        }
    }

    /// <summary>
    /// Where to write the log. The configured folder when one is set and usable, otherwise the same
    /// user-visible PCMHammer folder the kernels live in, so Save always has somewhere real to go.
    /// </summary>
    private string ResolveLogFolder()
    {
        string configured = this.settingsService.GetDataLogFolder();
        if (!string.IsNullOrWhiteSpace(configured)
            && configured != SettingsService.NoDataLogFolder
            && Path.IsPathRooted(configured))
        {
            return configured;
        }

#if ANDROID
        return Platforms.Android.PermissionMethods.PcmHammerDirectory;
#else
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCMHammer");
#endif
    }
}    