using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PcmHacking;

namespace PcmHacking.UnoUI.Services;

/// <summary>
/// This class receives log messages from the PcmHacking library.
/// </summary>
/// <remarks>
/// Currently it just writes them to the console. Before the Uno app gets
/// released, this needs to store the logs and make them available to the UI.
/// </remarks>
public class ProgressLogger : PcmHacking.ILogger
{
    public void AddDebugMessage(string message)
    {
        Console.WriteLine(message);
    }
    public void AddUserMessage(string message)
    {
        Console.WriteLine(message);
    }
    public void StatusUpdateActivity(string activity)
    {
        Console.WriteLine(activity);
    }
    public void StatusUpdateTimeRemaining(string remaining)
    {
        Console.WriteLine(remaining);
    }
    public void StatusUpdatePercentDone(string percent)
    {
        Console.WriteLine(percent);
    }
    public void StatusUpdateRetryCount(string retries)
    {
        Console.WriteLine(retries);
    }
    public void StatusUpdateProgressBar(double completed, bool visible)
    {
        Console.WriteLine(completed);
    }
    public void StatusUpdateKbps(string Kbps)
    {
        Console.WriteLine(Kbps);
    }
    public void StatusUpdateReset()
    {
        Console.WriteLine("Reset");
    }
    public void ResetLogs()
    {
        Console.WriteLine("Logs reset");
    }
    public string GetAppNameAndVersion()
    {
        return "TroubleshootingLogger";
    }
}
