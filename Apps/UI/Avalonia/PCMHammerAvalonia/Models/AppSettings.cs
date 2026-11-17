namespace PCMHammerAvalonia.Models;

public class AppSettings
{
    public bool RetainDeviceConfigurationOnExit { get; set; } = false;
    public string SavedDeviceType { get; set; } = string.Empty;
    public bool SaveResultsLogOnExit { get; set; } = false;
    public bool SaveDebugLogOnExit { get; set; } = false;
    public string SavedSerialPort { get; set; } = string.Empty;
    public string SavedSerialDevice { get; set; } = string.Empty;
    public string SavedJ2534Device { get; set; } = string.Empty;
    public bool SavedDevice4xCommunicationEnabled { get; set; } = false;
    public bool UseLogSaveAsDialog { get; set; } = false;
    public string LogDirectory { get; set; } = string.Empty;
    public string BinDirectory { get; set; } = string.Empty;
}
