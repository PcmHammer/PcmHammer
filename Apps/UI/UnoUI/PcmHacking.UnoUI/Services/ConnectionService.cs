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

    public bool InUse => this.vehicle.State == VehicleServiceState.InUse;

    public async void Connect(CurrentSettings settings)
    {
        // if the connection is in use, ignore this change
        if (this.InUse)
        {
            this.progressLogger.AddUserMessage("Connection is in use. Please wait.");
            return;
        }

        // try to connect to the vehicle
        bool connected = await this.vehicle.TryConnect(settings);

        // if connection works, update saved configuration and begin polling
        //if (connected)
        {
            this.settingsService.SettingsChanged(settings);
            //this.vehicle.StartPolling();
        }
    }
}
