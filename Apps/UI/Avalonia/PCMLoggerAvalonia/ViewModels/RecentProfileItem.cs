using System.IO;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>One entry in the recent-profiles list: shown by file name, with the full path as its tooltip.</summary>
public sealed record RecentProfileItem(string Path)
{
    public string DisplayName => System.IO.Path.GetFileName(this.Path);
}
