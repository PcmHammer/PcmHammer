using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System.Globalization;
using System.Runtime.CompilerServices;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;
using Windows.Storage;
using System.IO;

namespace PcmHacking.UnoUI.Presentation;

public partial record DumpRamModel : IAsyncLogger
{
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;
    private readonly IDispatcher dispatcher;

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Dump RAM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    public IState<string> Path => State<string>.Value(this, () => defaultPath);
    public IState<string> UserLog => State<string>.Value(this, () => String.Empty);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);

    public DumpRamModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService,
        IDispatcher dispatcher)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        var _1 = this.EnableControls(false);
    }

    private async Task EnableControls(bool busy)
    {
        await this.StartEnabled.SetAsync(!busy);
        await this.CancelEnabled.SetAsync(busy);
    }

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
        await this.EnableControls(true);

        string? path = await this.Path.Value();
        if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
        {
            path = await this.PromptForFileSavePath();
            if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
            {
                await this.AddUserMessage("No file selected.");
                await this.StartEnabled.SetAsync(true);
                await this.CancelEnabled.SetAsync(false);
                return;
            }
            await this.Path.SetAsync(path);
        }

        // TODO: Review the scenarios in which the given cancellationToken
        // can get signaled, and review how the read process handles those
        // signals.
        this.tokenSource = new CancellationTokenSource();
        CancellationToken dumpCancellationToken = this.tokenSource.Token;
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Dumping RAM", false))
            using (new LogInterceptor(this.loggerAdapter, this))
            {
                using (new AwayMode())
                {
                    await this.DumpRam(lease.Vehicle, path, dumpCancellationToken);
                    await lease.Vehicle.ExitKernel();
                }
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("RAM dump failed: ");
            await this.AddUserMessage(exception.Message);
            await Task.Delay(1000);
            await this.AddDebugMessage(exception.ToString());
        }
        finally
        {
            this.tokenSource = null;
            await this.EnableControls(false);
        }
    }

    private async Task DumpRam(Vehicle vehicle, string path, CancellationToken cancellationToken)
    {
        await this.AddUserMessage("Starting RAM dump...");
        await this.StatusUpdateActivity("Reading RAM from PCM...");

        const int startAddress = 0xFF0000;
        const int endAddress = 0xFFFFFF;
        const int stepSize = 4; // Read 4 bytes at a time
        const int totalAddresses = (endAddress - startAddress + 1) / stepSize;
        
        int currentIndex = 0;
        int errorCount = 0;
        var startTime = DateTime.Now;

        try
        {
            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                for (int address = startAddress; address <= endAddress; address += stepSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await this.AddUserMessage("RAM dump cancelled by user.");
                        return;
                    }

                    try
                    {
                        Response<uint> response = await vehicle.GetRam(address);
                        
                        if (response.Status == ResponseStatus.Success)
                        {
                            // Vehicle.GetRam returns 4 bytes as a uint, we need to write them as bytes
                            uint value = response.Value;
                            
                            // Write the 4 bytes in little-endian format
                            // Note that "illegal" addresses will read as 0xEE.
                            byte[] bytes = new byte[4];
                            bytes[0] = (byte)(value & 0xFF);
                            bytes[1] = (byte)((value >> 8) & 0xFF);
                            bytes[2] = (byte)((value >> 16) & 0xFF);
                            bytes[3] = (byte)((value >> 24) & 0xFF);
                            fileStream.Write(bytes, 0, 4);
                        }
                        else
                        {
                            errorCount++;
                            // Write zeros for failed reads
                            fileStream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        await this.AddDebugMessage($"Error reading address 0x{address:X6}: {ex.Message}");
                        // Write zeros for exceptions
                        fileStream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4);
                    }

                    currentIndex++;

                    // Update progress every 100 addresses to avoid too frequent UI updates
                    if (currentIndex % 100 == 0 || currentIndex == totalAddresses)
                    {
                        double progressPercent = (double)currentIndex / totalAddresses * 100.0;
                        await this.StatusUpdateProgressBar(progressPercent / 100.0, true);
                        await this.StatusUpdatePercentDone($"{progressPercent:F1}%");

                        // Calculate time estimates
                        var elapsed = DateTime.Now - startTime;
                        if (currentIndex > 0)
                        {
                            var averageTimePerAddress = elapsed.TotalSeconds / currentIndex;
                            var remainingAddresses = totalAddresses - currentIndex;
                            var estimatedTimeRemaining = TimeSpan.FromSeconds(remainingAddresses * averageTimePerAddress);
                            await this.StatusUpdateTimeRemaining(estimatedTimeRemaining.ToString(@"mm\:ss"));

                            // Calculate transfer rate
                            var bytesPerSecond = (currentIndex * 4) / elapsed.TotalSeconds;
                            await this.StatusUpdateKbps($"{bytesPerSecond:F0} bytes/sec");
                        }

                        await this.StatusUpdateRetryCount($"{errorCount}");
                    }
                }
            }

            await this.AddUserMessage($"RAM dump completed successfully.");
            await this.AddUserMessage($"Dumped {totalAddresses * 4} bytes to {path}");
            if (errorCount > 0)
            {
                await this.AddUserMessage($"Encountered {errorCount} read errors (filled with zeros).");
            }
        }
        catch (Exception ex)
        {
            await this.AddUserMessage($"File I/O error: {ex.Message}");
            throw;
        }
        finally
        {
            await this.StatusUpdateActivity("RAM dump completed");
        }
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.AddUserMessage("Cancelling.");
        this.tokenSource?.Cancel();
        this.tokenSource = null;
    }

    [Command]
    public async Task ChooseFile()
    {
        try
        {
            await this.StartEnabled.SetAsync(false);
            string path = await this.PromptForFileSavePath();
            await this.Path.SetAsync(path);
        }
        finally
        {
            await this.StartEnabled.SetAsync(true);
        }
    }

    private async Task Invoke(Action action)
    {
        var tcs = new TaskCompletionSource();
        await this.dispatcher.ExecuteAsync(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
    }

    private async Task<string> PromptForFileSavePath()
    {
        // Open a Save-As dialog to get the file path
        FileSavePicker savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("RAM Binary", new List<string>() { ".ram.bin" });
        savePicker.SuggestedFileName = "Untitled.ram.bin";
        StorageFile file = await savePicker.PickSaveFileAsync();
        if (file == null)
        {
            return null; // TODO: change the return-type to Task<string?> in the refactoring branch.
        }

        return file.Path;
    }

    private Task<uint> PromptForOperatingSystemId()
    {
        // TODO: OS ID dialog box
        return Task.FromResult(12587603u);
    }

    private Task<bool> PromptForYesNo(string message, string title)
    {
        // TODO: Yes/No dialog box
        return Task.FromResult(true);
    }

    private Task Alert(string message, string title)
    {
        // TODO: Alert popup
        return Task.CompletedTask;
    }
    
    public async Task AddUserMessage(string message, LogLevels level = 0)
    {
        await this.UserLog.SetAsync(this.UserLog.Value() + Environment.NewLine + message);
    }
    
    public Task AddDebugMessage(string message)
    {
        // TODO: Debug message logging
        return Task.CompletedTask;
    }

    public async Task StatusUpdateActivity(string activity)
    {
        await this.Activity.SetAsync(activity);
    }

    public async Task StatusUpdateTimeRemaining(string remaining)
    {
        await this.TimeRemaining.SetAsync("Estimated Completion: " + remaining);
    }

    public async Task StatusUpdatePercentDone(string percent)
    {
        await this.PercentDone.SetAsync("Progress: " + percent);
    }

    public async Task StatusUpdateRetryCount(string retries)
    {
        await this.RetryCount.SetAsync("Read errors: " + retries);
    }

    public async Task StatusUpdateProgressBar(double completed, bool visible)
    {
        await this.Progress.SetAsync(completed * 100);
    }

    public async Task StatusUpdateKbps(string Kbps)
    {
        await this.Kbps.SetAsync("Transfer Rate: " + Kbps);
    }

    public async Task StatusUpdateReset()
    {
        await this.Activity.SetAsync(String.Empty);
        await this.TimeRemaining.SetAsync(String.Empty);
        await this.PercentDone.SetAsync(String.Empty);
        await this.RetryCount.SetAsync(String.Empty);
        await this.Progress.SetAsync(0.0);
        await this.Kbps.SetAsync(String.Empty);
    }
}
