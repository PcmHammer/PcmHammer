using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Services;

/// <summary>
/// Validates setting changes by testing the vehicle connection.
/// </summary>
public class ConnectionService
{
    private IVehicleService vehicle;
    private ISettingsService settingsService;  
    private PcmHacking.ILogger progressLogger;

    public ConnectionService(IVehicleService vehicle, ISettingsService settingsService, PcmHacking.ILogger progressLogger)
    {
        this.vehicle = vehicle;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;
    }   

}
