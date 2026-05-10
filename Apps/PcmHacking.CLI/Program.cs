using System.CommandLine;
using PcmHacking;
using PcmHacking.CLI;
using PcmHacking.ECU;

CancellationTokenSource cancellationSource = new CancellationTokenSource();

static string GetBooleanAnswer(bool state)
{
    return state ? "Yes" : "No";
        while (controllerManager.ActionActive)
        {
            Thread.Sleep(20);
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

foreach (var arg in args)
{
    Console.WriteLine(arg);
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
deviceTypeOption.Validators.Add(v =>
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
            return;
        }
    }
    v.AddError("Hardware override flag was set, but the requested hardware type was not in the list!");
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
writeOption.Validators.Add(v =>
{
    string value = v.GetValueOrDefault<string>();
    if (uint.TryParse(value, out var key) && value.Length == 4)
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

Option<bool> forceOption = new("--force", "-f")
{
    Description = "This will disable OSID => hardware match and attempt with value set by -hw",
    DefaultValueFactory = _ => false
};

Option<bool> verboseOption = new("--verbose", "-v")
{
    Description = "Adding this flag will log debug messages to console.",
    DefaultValueFactory = _ => false
};

Option<bool> useHighSpeedOption = new("--highSpeed", "-hs")
{
    Description = "CLI will use slower baud rate to controller unless this is set. Not supported on all devices!"
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
    forceOption,
    verboseOption,
    useHighSpeedOption
};

string deviceType = string.Empty;
string deviceAddress = string.Empty;
string selectedAction = string.Empty;
string selectedHardware = string.Empty;
string writeType = string.Empty;
string inputPath = string.Empty;
string outputPath = string.Empty;
bool forced = false;
bool verbose = false;
bool highSpeed = false;
bool isWrite = false;

root.SetAction(parsed =>
{
    deviceType = parsed.GetValue(deviceTypeOption) ?? string.Empty;
    deviceAddress = parsed.GetValue(deviceAddressOption) ?? string.Empty;
    selectedAction = parsed.GetValue(actionOption) ?? string.Empty;
    selectedHardware = parsed.GetValue(hardwareOption) ?? string.Empty;
    writeType = parsed.GetValue(writeOption) ?? string.Empty;
    inputPath = parsed.GetValue(inputOption) ?? string.Empty;
    outputPath = parsed.GetValue(outputOption) ?? string.Empty;
    forced = parsed.GetValue(forceOption);
    verbose = parsed.GetValue(verboseOption);
    highSpeed = parsed.GetValue(useHighSpeedOption);
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
else // It was read...
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
        ShowDebug = verbose
    };

    ILogger logger = new LogMessageHandler($"CLI.{selectedAction}", true);
    Device device = null;
    Vehicle vehicle = null;

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
            Console.WriteLine("Selected device did not proper initialize. Please check settings and connection, and try again!");
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An exception occurred: {ex.Message}");
    }
    Protocol protocol = new();
    vehicle = new(device, protocol, logger, new ToolPresentNotifier(device, protocol, logger), $@"{AppContext.BaseDirectory}\Kernels");

    ControllerPageObjects pageObjects = new()
    {
        Invoke = async (action) => action.Invoke(),
        ShowAlert = async (t, m) =>
        {
        await vehicle.DiscoverConnectedECU(cancellationSource.Token); // This new method universally handles discovery of connection hardware. The only thing left to do is per-state validation.
            Console.ReadLine();
        },
        PromptYesOrNo = async (t, m) =>
        {
            Console.WriteLine(m);
            string? response = Console.ReadLine();
            if (response?.ToUpper() == "Y" ||  response?.ToUpper() == "YES")
            {
                return true;
            }
            return false;
        }
    };

    ControllerManager manager = new ControllerManager(vehicle, actionArgs, pageObjects, cancellationSource.Token, null, logger);
    manager.Initialize();

    if (await manager.BeginAction())
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