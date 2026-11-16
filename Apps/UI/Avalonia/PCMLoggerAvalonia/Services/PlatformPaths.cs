using System;

namespace PCMLoggerAvalonia.Services;

/// <summary>
/// Where the app's bundled data files (parameter XMLs, DiagnosticCodes.txt, the default LogProfile)
/// live at runtime.
///
/// On desktop they are copied next to the executable, so the default - <see cref="AppContext.BaseDirectory"/>
/// - is correct. On Android there is no executable directory; the files are packed inside the APK as
/// assets, so the Android head copies them to a readable folder at startup and sets this to that folder
/// before the view model loads anything.
/// </summary>
public static class PlatformPaths
{
    public static string ResourceDirectory { get; set; } = AppContext.BaseDirectory;
}
