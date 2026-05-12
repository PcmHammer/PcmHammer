using System.CommandLine;
using System.ComponentModel;
using System.Text;
using PcmHacking;
using PcmHacking.CLI;
using PcmHacking.ECU;

CancellationTokenSource cancellationSource = new CancellationTokenSource();
ControllerManager? controllerManager = null;
Vehicle? vehicle = null;
Device? device = null;
bool forced = false;

Console.CancelKeyPress += Console_CancelKeyPress;

async void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    cancellationSource.Cancel();
    if(controllerManager != null)
    {
        while (controllerManager.ActionActive)
        {
            Thread.Sleep(20);
        }
    }
}

static string GetWriteTypesString()
{
    List<string> writeNames = [.. Enum.GetNames<WriteType>()];
    writeNames.Remove("None");
    return string.Join(", ", writeNames);
}

static string GetPcmTypesString()
{
    List<string> writeNames = [.. Enum.GetNames<PcmType>()];
    writeNames.Remove("Undefined");
    writeNames.Remove("Unsupported");
    return string.Join(", ", writeNames);
}

Option<string> deviceTypeOption = new("--devType", "-dt")
{
    Description = "Selectable device categories: 'Serial', 'J2534', or 'Bluetooth'",
    DefaultValueFactory = _ => "Serial",
    Required = true,
};
deviceTypeOption.Validators.Add(v =>
{
    if (v.GetValueOrDefault<string>() == "Serial" || v.GetValueOrDefault<string>() == "J2534" || v.GetValueOrDefault<string>() == "Bluetooth")
    {
        return;
    }
    v.AddError(new("You must choose a valid action!"));
});

Option<string> deviceAddressOption = new("--devAddr", "-da")
{
    Description = "Device address: COM port => Serial; BT => address, hex, no spaces; J2534 => Driver's device name",
    Required = true
};
deviceAddressOption.Validators.Add(v =>
{
    if (!string.IsNullOrEmpty(v.GetValueOrDefault<string>()))
    {
        return;
    }
    v.AddError(new("You must enter device connection info!"));
});

Option<string> actionOption = new("--action", "-a")
{
    Description = "Selected action type allowed are 'Read' and 'Write'",
    DefaultValueFactory = _ => "Read",
    Required = true
};
actionOption.Validators.Add(v =>
{
    if (v.GetValueOrDefault<string>() == "Read" || v.GetValueOrDefault<string>() == "Write")
    {
        return;
    }
    v.AddError(new("You must choose a valid action!"));
});

Option<string> hardwareOption = new("--hardware", "-hw")
{
    Description = $"Supported types: {GetPcmTypesString()}",
};
hardwareOption.Validators.Add(v =>
{
    foreach (string name in Enum.GetNames<PcmType>())
    {
        if (v.GetValueOrDefault<string>() == name)
        {
            forced = true;
            return;
        }
    }
    v.AddError("Hardware override flag was set, but the requested hardware type was not in the list!");
});

Option<string> flashOption = new("--flash", "-fl")
{
    Description = $"Set the flash chip ID manually. ADVANCED USERS ONLY!",
};
flashOption.Validators.Add(v =>
{
    string byteString = v.GetValueOrDefault<string>();
    if(byteString.Length != 8)
    {
        v.AddError("ID sequence must contain 8 valid Hex characters!");
    }
});

Option<string> writeOption = new("--writeType", "-w")
{
    Description = "Selected action type allowed are 'Read' and 'Write'",
};
writeOption.Validators.Add(v =>
{
    foreach (string name in Enum.GetNames<WriteType>())
    {
        if (v.GetValueOrDefault<string>() == name)
        {
            return;
        }
    }
    v.AddError("The requested write type is invalid!");
});

Option<string> customKeyOption = new("--customKey", "-c")
{
    Description = "Optional custom key used for controller unlocking.",
};
customKeyOption.Validators.Add(v =>
{
    string value = v.GetValueOrDefault<string>();
    if (string.IsNullOrEmpty(value) || (uint.TryParse(value, out var key) && value.Length == 4))
    {
        return;
    }
    v.AddError("The custom key flag was set, but the key was not a valid unsigned integer or incorrect length!");
});

Option<string> inputOption = new("--input", "-i")
{
    Description = "Sets the file location of target bin for writing.",
};
inputOption.Validators.Add(v =>
{
    if (!File.Exists(v.GetValueOrDefault<string>()))
    {
        v.AddError("Input file could not be opened at the entered path!");
    }
});

Option<string> outputOption = new("--output", "-o")
{
    Description = $"Allowable actions: {GetWriteTypesString()}",
};
outputOption.Validators.Add(v =>
{
    FileInfo info = new(v.GetValueOrDefault<string>());
    if (info != null)
    {
        if (info.Directory != null && !info.Directory.Exists)
        {
            try
            {
                Directory.CreateDirectory(info.Directory.FullName);
            } catch
            {
                v.AddError("Could not create directory.");
            }
        }
        try
        {
            info.Create().Close();
        } catch 
        {
            v.AddError("Error creating file at the requested location!");
        }
    }
});

Option<string> logLevelOption = new("--logLevel", "-l")
{
    Description = "Sets the desired logging output. Available options: Info, Debug, Trace",
    DefaultValueFactory = _ => "Info"
};
logLevelOption.Validators.Add(v => { 
    string entry = v.GetValueOrDefault<string>();
    if (entry != null && Enum.GetNames<LogLevels>().Contains(entry))
    {
        return;
    }
    v.AddError("A custom logging level was set, but an invalid entry was detected!");
});

Option<bool> useHighSpeedOption = new("--highSpeed", "-hs")
{
    Description = "CLI will use slower baud rate to controller unless this is set. Not supported on all devices!"
};

Option<bool> skipPreChecksOption = new("--YES", "-Y")
{
    Description = "CLI will skip all pre-checks used to determine compatibility. Advanced users only!"
};

RootCommand root = new RootCommand("PCM Hammer CLI")
{
    deviceTypeOption,
    deviceAddressOption,
    actionOption,
    hardwareOption,
    writeOption,
    inputOption,
    outputOption,
    customKeyOption,
    logLevelOption,
    useHighSpeedOption,
    skipPreChecksOption,
    flashOption
};

Console.WriteLine($"{root.Name} has started with the following arguments:");
Console.WriteLine(string.Join(' ', args));

string deviceType = string.Empty;
string deviceAddress = string.Empty;
string selectedAction = string.Empty;
string selectedHardware = string.Empty;
string flashId = string.Empty;
string writeType = string.Empty;
string inputPath = string.Empty;
string outputPath = string.Empty;
string customKey = string.Empty;
string logLevel = string.Empty;
bool highSpeed = false;
bool isWrite = false;
bool skipChecks = false;

root.SetAction(parsed =>
{
    deviceType = parsed.GetValue(deviceTypeOption) ?? string.Empty;
    deviceAddress = parsed.GetValue(deviceAddressOption) ?? string.Empty;
    selectedAction = parsed.GetValue(actionOption) ?? string.Empty;
    selectedHardware = parsed.GetValue(hardwareOption) ?? string.Empty;
    flashId = parsed.GetValue(flashOption) ?? string.Empty;
    writeType = parsed.GetValue(writeOption) ?? string.Empty;
    inputPath = parsed.GetValue(inputOption) ?? string.Empty;
    customKey = parsed.GetValue(customKeyOption) ?? string.Empty;
    logLevel = parsed.GetValue(logLevelOption) ?? string.Empty;
    outputPath = parsed.GetValue(outputOption) ?? string.Empty;
    highSpeed = parsed.GetValue(useHighSpeedOption);
    skipChecks = parsed.GetValue(skipPreChecksOption);
});

var result = root.Parse(args).Invoke();

if(selectedAction == ControllerActions.Write.ToString())
{
    isWrite = true;
    if (string.IsNullOrEmpty(writeType))
    {
        Console.WriteLine("Error! Write action detected, but a write type was not set!");
        return 1;
    }
    if (string.IsNullOrEmpty(inputPath))
    {
        Console.WriteLine("Error! Write action detected, but a proper input file path was not set!");
    }
}
else
{
    if (string.IsNullOrEmpty(outputPath))
    {
        Console.WriteLine("Error! Read action detected, but a proper output file path was not set!");
    }
}

if (forced)
{
    if (string.IsNullOrEmpty(selectedHardware))
    {
        Console.WriteLine("Error! Forced flag was set, but a hardware selection was not!");
        return 1;
    }
}
uint keyOutput = 0;
if (!string.IsNullOrEmpty(customKey))
{
    if(!uint.TryParse(customKey, out keyOutput))
    {
        Console.WriteLine("Error! A custom key was set, but failed to parse to unsigned integer. Check command!");
        return 1;
    }
}

if (result == 0)
{
    ECUActionArguments actionArgs = new ECUActionArguments()
    {
        SelectedAction = Enum.Parse<ControllerActions>(selectedAction),
        WriteType = isWrite ? Enum.Parse<WriteType>(writeType) : WriteType.None,
        HardwareType = forced ? Enum.Parse<PcmType>(selectedHardware) : PcmType.Undefined,
        UseHighSpeed = highSpeed,
        CustomKey = keyOutput,
        ShowDebug = Enum.Parse<LogLevels>(logLevel) >= LogLevels.Debug
    };

    if (actionArgs.SelectedAction == ControllerActions.Write)
    {
        actionArgs.ContentStream = new();
        FileStream file = File.Open(inputPath, FileMode.Open, FileAccess.Read);
        await file.CopyToAsync(actionArgs.ContentStream);
        file.Close();
    }

    ILogger logger = new LogMessageHandler(Enum.Parse<LogLevels>(logLevel), $"CLI.{selectedAction}", true, true, true);


    if (!string.IsNullOrWhiteSpace(flashId))
    {

        if (Int32.TryParse(Encoding.ASCII.GetBytes(flashId), System.Globalization.NumberStyles.HexNumber, default, out Int32 value))
        {
            FlashChip chip = FlashChip.Create((uint)value, logger);
            logger.AddUserMessage($"Using override flash chip: {chip}");
            actionArgs.FlashChipId = (uint)value;
        }
    }

    if (deviceType == DeviceConstants.DeviceCategoryBT)
    {
        device = await BluetoothDeviceFactory.CreateBluetoothDevice(deviceAddress, logger);
    }
    else
    {
        device = DeviceFactory.CreateDevice(logger, deviceType, deviceAddress);
    }

    if (device == null)
    {
        Console.WriteLine("Could not create or locate device on system. Please check device settings!");
        return 1;
    }
    try
    {
        if (!await device.Initialize())
        {
            Console.WriteLine("Selected device did not initialize properly. Please check settings and connection, and try again!");
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An exception occurred: {ex.Message}");
    }
    Protocol protocol = new();
    vehicle = new(device, protocol, logger, new ToolPresentNotifier(device, protocol, logger), $@"{AppContext.BaseDirectory}\Kernels");

    ControllerPageObjects pageObjects = new();
    int lastPercent = -1;
    Progress<ProgressUpdate> progress = new Progress<ProgressUpdate>(progress =>
    {
        if (!string.IsNullOrEmpty(progress.Activity))
        {
            Console.WriteLine(progress.Activity);
            return;
        }
        int curProgress = (int)(progress.Percentage * 100);
        if (curProgress % 5 == 0 && curProgress > lastPercent)
        {
            lastPercent = curProgress;
            logger.AddUserMessage($"{curProgress}% completed @ {progress.Rate} Kb/s. ETR: {progress.TimeRemaining}");
        }
    });

    if (actionArgs.HardwareType != PcmType.Undefined)
    {
        vehicle.ConnectedECU = ECUFactory.GetControllerOverride(actionArgs.HardwareType);
    }
    else
    {
        await vehicle.DiscoverConnectedECU(cancellationSource.Token); // This new method universally handles discovery of connection hardware. The only thing left to do is per-state validation.
    }
    if (vehicle.ConnectedECU == null)
    {
        throw new Exception("ConnectedECU object was null! Call DiscoverConnectedECU first.");
    }

    controllerManager = new ControllerManager(vehicle, actionArgs, pageObjects, cancellationSource.Token, progress, logger);
    controllerManager.Initialize();

    PreFlightCheckResult checkResult = vehicle.ConnectedECU.GetPreCheckResults(actionArgs.SelectedAction, actionArgs.WriteType);
    if(skipChecks && checkResult.ShouldPrompt)
    {
        logger.AddUserMessage("Warning! Precheck conditions present were skipped due to flag -(Y)ES present in command.\r\n!! This can lead to bricking your ECU if this hardware is not compatible !!");
    }
    if (checkResult.ShouldPrompt && !skipChecks)
    {
        logger.AddUserMessage(checkResult.PromptMessage ?? string.Empty); // This will never be empty, just satisfy the null check.
        logger.AddUserMessage(checkResult.CanProceed ? "Type (Y)ES followed by 'Enter' to continue." : "Abort! Press 'Enter' to exit application.");
        string userInput = Console.ReadLine() ?? string.Empty;
        if(!checkResult.CanProceed)
        {
            return 1;
        }
        bool userResult = false;
        

        if (userInput?.ToUpper() == "Y" || userInput?.ToUpper() == "YES")
        {
            userResult = true;
        }
        if (!userResult)
        {
            logger.AddUserMessage("Abort! User chose to exit.");
            return 1;
        }
    }
    actionArgs.PreFlightChecksRequired = false;

    if (await controllerManager.BeginAction())
{
        if (actionArgs.SelectedAction == ControllerActions.Read)
        {
            if (actionArgs.ContentStream != null && actionArgs.ContentStream.Length > 0)
            {
                await File.WriteAllBytesAsync(outputPath, actionArgs.ContentStream.ToArray(), cancellationSource.Token);
                Console.WriteLine("File saved successfully!");
                return 0;
            }
        }
        else
        {
            return 0;
        }
    Console.WriteLine("Something has failed! Check the logs and try again!");
        return 1;
    }
}
return result;