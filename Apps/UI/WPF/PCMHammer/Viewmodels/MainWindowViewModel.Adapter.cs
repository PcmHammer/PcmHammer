using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using System.Windows.Media;

namespace PCMHammer.Viewmodels;
public partial class MainWindowViewModel
{
    private bool adapterConnectionVerified;
    private bool adapterCheckRunning;
    public bool CanCancelOperation=>IsOperationRunning&&!adapterCheckRunning;
    [ObservableProperty] private string adapterStatus="No OBD adapter connected";
    [ObservableProperty] private string adapterDetails="Select Device to connect. A CAN-capable adapter is needed for your E38.";
    [ObservableProperty] private Brush adapterIndicator=Brushes.SlateGray;
    [ObservableProperty] private string ecuConnectionStatus="ECU not checked";
    public bool CanCheckAdapterConnection=>SelectedDevice!=null&&IsDeviceControlEnabled;
    private void ResetAdapterStatus()
    {
        adapterConnectionVerified=false;
        AdapterStatus=SelectedDevice==null?"No OBD adapter connected":"Adapter selected · connection unverified";
        AdapterIndicator=Brushes.SlateGray;
        AdapterDetails=SelectedDevice==null?"Select Device to connect. Your E38 requires CAN.":SelectedDevice+" · Click Check to verify the adapter connection.";
        EcuConnectionStatus="ECU not checked";
    }
    private void SetAdapterConnectionStatus(bool connected)
    {
        if(SelectedDevice==null){ResetAdapterStatus();return;}
        adapterConnectionVerified=connected;
        bool can=SelectedDevice.SupportedProtocols.Contains(BusProtocol.Can500k);
        AdapterStatus=connected?(can?"Adapter connected · CAN available":"Adapter connected · no E38 CAN support"):"Adapter not responding";
        AdapterIndicator=connected?(can?new SolidColorBrush(Color.FromRgb(18,151,132)):Brushes.DarkOrange):Brushes.IndianRed;
        AdapterDetails=$"{SelectedDevice} · Last checked {DateTime.Now:HH:mm:ss}. Bus capability is reported by the adapter/driver, not a guarantee of successful programming. Click Check to refresh; no connection polling runs during reads, writes, logging or monitoring.";
        if(!connected)EcuConnectionStatus="ECU connection unverified";
    }
    [RelayCommand(CanExecute=nameof(CanCheckAdapterConnection))]
    public async Task CheckAdapterConnection()
    {
        if(!CanCheckAdapterConnection)return;
        Device device=SelectedDevice!;
        adapterCheckRunning=true;IsOperationRunning=true;AdapterStatus="Checking adapter…";AdapterIndicator=Brushes.DarkOrange;
        try
        {
            bool responds=await Task.Run(()=>device.CheckDeviceConnection());
            if(ReferenceEquals(device,SelectedDevice))SetAdapterConnectionStatus(responds);
        }
        catch(Exception error){if(ReferenceEquals(device,SelectedDevice))SetAdapterConnectionStatus(false);_logger.AddDebugMessage(error.ToString());}
        finally{adapterCheckRunning=false;IsOperationRunning=false;}
    }
}
