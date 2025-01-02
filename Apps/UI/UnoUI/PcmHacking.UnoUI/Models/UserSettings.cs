using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Models;

public enum DeviceTypes
{
    Serial,
    J2534
}

internal class UserSettings
{
    public const string NotSet = "NotSet";

    public string DeviceType { get; set; } = UserSettings.NotSet;
    public string SerialPort { get; set; } = UserSettings.NotSet;
    public string J2534DeviceName { get; set; } = UserSettings.NotSet;
    public string CanPort { get; set; } = UserSettings.NotSet;
}
