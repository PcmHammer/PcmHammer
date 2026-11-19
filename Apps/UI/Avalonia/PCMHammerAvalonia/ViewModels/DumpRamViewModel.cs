using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammerAvalonia.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCMHammerAvalonia.ViewModels;

/// <summary>
/// Dumps the PCM's RAM (0xFF0000-0xFFFFFF) to a file, four bytes per request. Ports the Uno Dump RAM
/// page. It only reads memory; nothing is written to the PCM.
/// </summary>
public partial class DumpRamViewModel : ObservableObject
{
    private const string DefaultPath = "No file selected.";

    private const int StartAddress = 0xFF0000;
    private const int EndAddress = 0xFFFFFF;
    private const int StepSize = 4; // GetRam returns four bytes at a time.
    private const int TotalReads = (EndAddress - StartAddress + 1) / StepSize;

    private readonly Vehicle _vehicle;
    private readonly ILogger _logger;
    private readonly IFileDialogService _fileDialogService;
    private IStorageFile? _file;
    private CancellationTokenSource? _cts;
    private bool _closeWhenDone;

    [ObservableProperty]
    public partial string PathText { get; set; } = DefaultPath;

    [ObservableProperty]
    public partial string LogText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string Activity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PercentDone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TimeRemaining { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Rate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorCount { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsRunning { get; set; }

    public event Action? RequestClose;

    public DumpRamViewModel(Vehicle vehicle, ILogger logger, IFileDialogService fileDialogService)
    {
        _vehicle = vehicle;
        _logger = logger;
        _fileDialogService = fileDialogService;
    }

    // Parameterless constructor for the XAML previewer.
    public DumpRamViewModel()
    {
        _vehicle = null!;
        _logger = null!;
        _fileDialogService = null!;
    }

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private async Task ChooseFile()
    {
        IStorageFile? file = await _fileDialogService.PickBinSaveFileAsync("PCM_RAM");
        if (file == null) return;

        _file = file;
        PathText = file.Name;
    }

    private bool CanChangeSettings() => !IsRunning;

    // Deliberately synchronous. An async command's button is disabled while its handler runs, and the
    // dump takes minutes, so the work is started without being awaited. RunAsync handles its own errors.
    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private void Start()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        if (_file == null)
        {
            await ChooseFile();
            if (_file == null)
            {
                AddMessage("No file selected.");
                return;
            }
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsRunning = true;
        IStorageFile file = _file;

        try
        {
            AddMessage("Starting RAM dump...");
            Activity = "Reading RAM from PCM...";

            await Task.Run(() => DumpRam(file, token));
        }
        catch (OperationCanceledException)
        {
            // Cancel can interrupt the file write itself, not just the check at the top of the loop.
            AddMessage("RAM dump cancelled by user.");
            Activity = "RAM dump cancelled";
        }
        catch (Exception exception)
        {
            AddMessage("RAM dump failed: " + exception.Message);
            _logger.AddDebugMessage(exception.ToString());
        }
        finally
        {
            try
            {
                // Leave the PCM the way we found it, as the Uno version does.
                await Task.Run(() => _vehicle.ExitKernel());
            }
            catch (Exception exception)
            {
                _logger.AddDebugMessage("Exit kernel after RAM dump failed: " + exception.Message);
            }

            _cts?.Dispose();
            _cts = null;
            IsRunning = false;

            if (_closeWhenDone) RequestClose?.Invoke();
        }
    }

    private async Task DumpRam(IStorageFile file, CancellationToken token)
    {
        int index = 0;
        int errors = 0;
        DateTime start = DateTime.Now;

        await using Stream stream = await file.OpenWriteAsync();

        for (int address = StartAddress; address <= EndAddress; address += StepSize)
        {
            if (token.IsCancellationRequested)
            {
                Post(() => AddMessage("RAM dump cancelled by user."));
                return;
            }

            byte[] bytes;
            try
            {
                Response<uint> response = await _vehicle.GetRam(address);
                if (response.Status == ResponseStatus.Success)
                {
                    // Little-endian, as Uno writes it. Illegal addresses read back as 0xEE.
                    uint value = response.Value;
                    bytes = new[]
                    {
                        (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF),
                        (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF),
                    };
                }
                else
                {
                    errors++;
                    bytes = new byte[4]; // Failed reads are filled with zeros.
                }
            }
            catch (Exception exception)
            {
                errors++;
                _logger.AddDebugMessage($"Error reading address 0x{address:X6}: {exception.Message}");
                bytes = new byte[4];
            }

            await stream.WriteAsync(bytes, 0, 4, token);
            index++;

            // Updating the screen for every four bytes would dwarf the read itself.
            if (index % 100 == 0 || index == TotalReads)
            {
                double percent = (double)index / TotalReads * 100.0;
                TimeSpan elapsed = DateTime.Now - start;
                TimeSpan remaining = TimeSpan.FromSeconds((TotalReads - index) * (elapsed.TotalSeconds / index));
                double bytesPerSecond = index * 4 / Math.Max(elapsed.TotalSeconds, 0.001);
                int errorsNow = errors;

                Post(() =>
                {
                    Progress = percent;
                    PercentDone = $"Progress: {percent:F1}%";
                    TimeRemaining = "Estimated Completion: " + remaining.ToString(@"mm\:ss");
                    Rate = $"Transfer Rate: {bytesPerSecond:F0} bytes/sec";
                    ErrorCount = $"Read errors: {errorsNow}";
                });
            }
        }

        int finalErrors = errors;
        Post(() =>
        {
            AddMessage("RAM dump completed successfully.");
            AddMessage($"Dumped {TotalReads * StepSize} bytes to {file.Name}");
            if (finalErrors > 0) AddMessage($"Encountered {finalErrors} read errors (filled with zeros).");
            Activity = "RAM dump completed";
        });
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        AddMessage("Cancelling.");
        _cts?.Cancel();
    }

    [RelayCommand]
    private void Close()
    {
        if (IsRunning)
        {
            // Let the dump stop and the device be released before the window goes away.
            _closeWhenDone = true;
            Stop();
            return;
        }

        RequestClose?.Invoke();
    }

    /// <summary>Asks any running dump to stop. Safe to call more than once.</summary>
    public void Stop()
    {
        if (IsRunning) Activity = "Stopping...";
        _cts?.Cancel();
    }

    private void AddMessage(string message)
    {
        LogText = LogText.Length == 0 ? message : LogText + Environment.NewLine + message;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
