// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    class Program
    {
        static Vehicle? activeVehicle;
        static bool operationInProgress;

        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => activeVehicle?.Dispose();
            return RunAsync(args).GetAwaiter().GetResult();
        }

        static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args.Any(a => a == "--help" || a == "/?"))
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            string? operation = null;
            string? filePath = null;
            string? deviceSpec = null;
            bool listDevices = false;
            bool debug = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--read":
                        operation = "read";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) filePath = args[++i];
                        break;
                    case "--write":
                        operation = "write";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) filePath = args[++i];
                        break;
                    case "--test-write":
                        operation = "test-write";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) filePath = args[++i];
                        break;
                    case "--test-read":
                        operation = "test-read";
                        break;
                    case "--get-properties":
                        operation = "get-properties";
                        break;
                    case "--device":
                        if (i + 1 < args.Length) deviceSpec = args[++i];
                        break;
                    case "--list-devices":
                        listDevices = true;
                        break;
                    case "--debug":
                        debug = true;
                        break;
                    case "--help":
                    case "/?":
                        PrintHelp();
                        return 0;
                }
            }

            var logger = new ConsoleLogger(debug);

            if (listDevices)
            {
                ListDevices(logger);
                return 0;
            }

            if (operation == null)
            {
                Console.Error.WriteLine("Error: No operation specified.");
                PrintHelp();
                return 1;
            }

            if (filePath == null && operation != "read" && operation != "test-read" && operation != "get-properties")
            {
                Console.Error.WriteLine($"Error: No file path specified for --{operation}.");
                return 1;
            }

            string kernelDir = ExtractKernels();

            Device? device = ResolveDevice(deviceSpec, logger);
            if (device == null)
                return 1;

            using (new AwayMode())
            {
                Vehicle? vehicle = null;
                try
                {
                    vehicle = await InitializeVehicle(device, logger, kernelDir);
                    activeVehicle = vehicle;

                    var cts = new CancellationTokenSource();
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        if (operationInProgress)
                            Console.Error.WriteLine("\nOperation in progress — waiting for clean shutdown. Press Ctrl+C again to force quit.");
                        else
                            Console.Error.WriteLine("\nCancellation requested.");
                        cts.Cancel();
                    };

                    Func<Action, Task> invoke = (action) => { action(); return Task.CompletedTask; };
                    Func<string, string, Task> alert = (msg, title) =>
                    {
                        logger.AddUserMessage($"[{title}] {msg}");
                        return Task.CompletedTask;
                    };
                    Func<string, string, Task<bool>> promptForYesNo = (msg, title) =>
                    {
                        logger.AddUserMessage($"[{title}] {msg}");
                        logger.AddUserMessage("Auto-proceeding.");
                        return Task.FromResult(true);
                    };

                    operationInProgress = true;
                    bool success = false;
                    switch (operation)
                    {
                        case "read":
                        {
                            if (filePath == null)
                            {
                                filePath = $"pcm_read_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
                                logger.AddUserMessage("No filename specified, saving to: " + filePath);
                            }
                            var readManager = new ReadManager(
                                logger,
                                vehicle,
                                invoke,
                                () => Task.FromResult<string?>(null),
                                () => Task.FromResult(0u),
                                alert,
                                promptForYesNo,
                                cts.Token);
                            success = await readManager.Read(filePath);
                            break;
                        }
                        case "test-read":
                        {
                            var readManager = new ReadManager(
                                logger,
                                vehicle,
                                invoke,
                                () => Task.FromResult<string?>(null),
                                () => Task.FromResult(0u),
                                alert,
                                promptForYesNo,
                                cts.Token);
                            var stream = await readManager.Read();
                            success = stream != null;
                            if (success) logger.AddUserMessage("Test read complete. Data not saved.");
                            break;
                        }
                        case "write":
                        {
                            var writeManager = new WriteManager(
                                logger,
                                vehicle,
                                WriteType.Full,
                                alert,
                                promptForYesNo,
                                cts.Token);
                            success = await writeManager.Write(filePath!);
                            break;
                        }
                        case "test-write":
                        {
                            var writeManager = new WriteManager(
                                logger,
                                vehicle,
                                WriteType.TestWrite,
                                alert,
                                promptForYesNo,
                                cts.Token);
                            success = await writeManager.Write(filePath!);
                            break;
                        }
                        case "get-properties":
                        {
                            success = await GetProperties(vehicle, logger, cts.Token);
                            break;
                        }
                    }

                    operationInProgress = false;
                    return success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Fatal error: " + ex.Message);
                    if (debug)
                        Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
                finally
                {
                    operationInProgress = false;
                    activeVehicle = null;
                    vehicle?.Dispose();
                }
            }
        }

        // Lists all devices with sequential indices shared across both sections.
        // Indices from this output can be passed directly to --device.
        static void ListDevices(ILogger logger)
        {
            var serialPorts = SerialPort.GetPortNames();
            var j2534Devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);
            int index = 1;

            Console.WriteLine("Available serial devices:");
            if (serialPorts.Length == 0)
                Console.WriteLine("  (none found)");
            else
                foreach (var port in serialPorts)
                    Console.WriteLine($"  [{index++}] {port}");

            Console.WriteLine();

            Console.WriteLine("Available J2534 devices:");
            if (j2534Devices.Count == 0)
                Console.WriteLine("  (none found)");
            else
                foreach (var d in j2534Devices)
                    Console.WriteLine($"  [{index++}] {d.Name}");
        }

        // Resolves --device <spec> to a Device instance.
        //
        // Resolution order:
        //   null          → auto-select when exactly one device is present
        //   integer       → index from --list-devices output
        //   COMn          → exact serial port name (case-insensitive)
        //   anything else → case-insensitive substring match against J2534 device names
        static Device? ResolveDevice(string? deviceSpec, ILogger logger)
        {
            var serialPorts = SerialPort.GetPortNames();
            var j2534Devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);

            if (deviceSpec == null)
            {
                int total = serialPorts.Length + j2534Devices.Count;
                if (total == 0)
                {
                    Console.Error.WriteLine("Error: No devices found. Connect a device and try again.");
                    return null;
                }
                if (total > 1)
                {
                    Console.Error.WriteLine("Error: Multiple devices found. Use --device to select one.");
                    Console.Error.WriteLine("  Run --list-devices to see available options.");
                    return null;
                }
                if (serialPorts.Length == 1)
                {
                    logger.AddUserMessage("Auto-selected: " + serialPorts[0]);
                    return DeviceFactory.AutoDetectSerialDevice(serialPorts[0], logger).GetAwaiter().GetResult();
                }
                logger.AddUserMessage("Auto-selected: " + j2534Devices[0].Name);
                return DeviceFactory.CreateJ2534Device(j2534Devices[0].Name, logger);
            }

            // Integer index into the combined --list-devices list
            if (int.TryParse(deviceSpec, out int index) && index >= 1)
            {
                if (index <= serialPorts.Length)
                {
                    string port = serialPorts[index - 1];
                    logger.AddUserMessage($"Selected [{index}] {port}");
                    return DeviceFactory.AutoDetectSerialDevice(port, logger).GetAwaiter().GetResult();
                }
                int j2534Index = index - serialPorts.Length - 1;
                if (j2534Index < j2534Devices.Count)
                {
                    string name = j2534Devices[j2534Index].Name;
                    logger.AddUserMessage($"Selected [{index}] {name}");
                    return DeviceFactory.CreateJ2534Device(name, logger);
                }
                Console.Error.WriteLine($"Error: Index {index} is out of range. Run --list-devices to see options.");
                return null;
            }

            // Serial port — exact name match (case-insensitive)
            if (deviceSpec.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return DeviceFactory.AutoDetectSerialDevice(deviceSpec, logger).GetAwaiter().GetResult();

            // J2534 — case-insensitive substring match
            var matches = j2534Devices
                .Where(d => d.Name.IndexOf(deviceSpec, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matches.Count == 1)
            {
                logger.AddUserMessage("Selected: " + matches[0].Name);
                return DeviceFactory.CreateJ2534Device(matches[0].Name, logger);
            }

            if (matches.Count > 1)
            {
                Console.Error.WriteLine($"Error: \"{deviceSpec}\" matches multiple devices:");
                foreach (var m in matches)
                    Console.Error.WriteLine("  " + m.Name);
                Console.Error.WriteLine("Use a more specific name, or run --list-devices and pick by index.");
                return null;
            }

            Console.Error.WriteLine($"Error: No device matching \"{deviceSpec}\" found. Run --list-devices to see options.");
            return null;
        }

        // Mirrors the WinForms "Read Properties" button: queries VIN, OSID, calibration,
        // hardware ID, serial number, BCC, MEC, and voltage. Conditional queries follow
        // the same hardware-type rules as the WinForms implementation.
        static async Task<bool> GetProperties(Vehicle vehicle, ILogger logger, CancellationToken token)
        {
            OSIDInfo? pcmInfo = null;

            var vinResponse = await vehicle.QueryVin();
            if (vinResponse.Status != ResponseStatus.Success)
            {
                logger.AddUserMessage("VIN query failed: " + vinResponse.Status);
                return false;
            }
            logger.AddUserMessage("VIN: " + vinResponse.Value);

            var osResponse = await vehicle.QueryOperatingSystemId(token);
            if (osResponse.Status == ResponseStatus.Success)
            {
                logger.AddUserMessage("OSID: " + osResponse.Value);
                pcmInfo = new OSIDInfo(osResponse.Value);
                logger.AddUserMessage("Description: " + pcmInfo.Description);
            }
            else
            {
                logger.AddUserMessage("OS ID query failed: " + osResponse.Status);
            }

            if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
            {
                var calResponse = await vehicle.QueryCalibrationId();
                if (calResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Calibration ID: " + calResponse.Value);
                else
                    logger.AddUserMessage("Calibration ID query failed: " + calResponse.Status);
            }

            if (pcmInfo != null &&
                pcmInfo.HardwareType != PcmType.P05 &&
                pcmInfo.HardwareType != PcmType.P05b &&
                pcmInfo.HardwareType != PcmType.P10 &&
                pcmInfo.HardwareType != PcmType.P12 &&
                pcmInfo.HardwareType != PcmType.E54)
            {
                var hwResponse = await vehicle.QueryHardwareId();
                if (hwResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Hardware ID: " + hwResponse.Value);
                else
                    logger.AddUserMessage("Hardware ID query failed: " + hwResponse.Status);
            }

            if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
            {
                var serialResponse = await vehicle.QuerySerial();
                if (serialResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Serial Number: " + serialResponse.Value);
                else
                    logger.AddUserMessage("Serial Number query failed: " + serialResponse.Status);
            }

            if (pcmInfo != null &&
                pcmInfo.HardwareType != PcmType.P04 &&
                pcmInfo.HardwareType != PcmType.P04_Early &&
                pcmInfo.HardwareType != PcmType.P08)
            {
                var bccResponse = await vehicle.QueryBCC();
                if (bccResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Broad Cast Code: " + bccResponse.Value);
                else
                    logger.AddUserMessage("BCC query failed: " + bccResponse.Status);
            }

            var mecResponse = await vehicle.QueryMEC();
            if (mecResponse.Status == ResponseStatus.Success)
                logger.AddUserMessage("MEC: " + mecResponse.Value);
            else
                logger.AddUserMessage("MEC query failed: " + mecResponse.Status);

            var voltageResponse = await vehicle.QueryVoltage();
            if (voltageResponse.Status == ResponseStatus.Success)
                logger.AddUserMessage("Voltage: " + voltageResponse.Value);
            else
                logger.AddUserMessage("Voltage query failed: " + voltageResponse.Status);

            return true;
        }

        static string ExtractKernels()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pcmhammer-cli-kernels");
            Directory.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = name.Split('.');
                string fileName = parts.Length >= 2
                    ? parts[parts.Length - 2] + "." + parts[parts.Length - 1]
                    : name;
                string dest = Path.Combine(dir, fileName);
                using (var s = asm.GetManifestResourceStream(name))
                using (var f = File.Create(dest))
                    s.CopyTo(f);
            }
            return dir;
        }

        static async Task<Vehicle> InitializeVehicle(Device device, ILogger logger, string kernelDir)
        {
            Protocol protocol = new Protocol();
            var vehicle = new Vehicle(
                device,
                protocol,
                logger,
                new ToolPresentNotifier(device, protocol, logger),
                kernelDir);

            logger.AddUserMessage("PCM Hammer CLI");
            logger.AddUserMessage(AppInfo.GetVersionOrBuildLine(Generated.BuildTime));
            logger.AddUserMessage(AppInfo.GetRunningAtMessage());
            logger.AddUserMessage(AppInfo.CopyrightNotice);
            logger.AddUserMessage("Initializing device: " + vehicle.DeviceDescription);

            Task<bool> initTask = vehicle.ResetConnection();
            bool completed = await initTask.AwaitWithTimeout(TimeSpan.FromSeconds(10));
            if (!completed)
            {
                vehicle.Dispose();
                throw new TimeoutException("Timeout initializing " + vehicle.DeviceDescription);
            }
            if (!initTask.Result)
            {
                vehicle.Dispose();
                throw new Exception("Unable to initialize " + vehicle.DeviceDescription);
            }

            vehicle.Enable4xReadWrite = true;
            logger.AddUserMessage("Device ready: " + vehicle.DeviceDescription);
            return vehicle;
        }

        static void PrintHelp()
        {
            Console.WriteLine("PCM Hammer CLI");
            Console.WriteLine();
            Console.WriteLine("Usage:  pcmhammer-cli.exe <operation> [--device <id>] [--debug]");
            Console.WriteLine();
            Console.WriteLine("Operations:");
            Console.WriteLine("  --read [file]             Read entire PCM to file (auto-names if omitted)");
            Console.WriteLine("  --test-read               Read entire PCM without saving");
            Console.WriteLine("  --write <file>            Write entire PCM from file");
            Console.WriteLine("  --test-write <file>       Test write (no permanent changes)");
            Console.WriteLine("  --get-properties          Read VIN, OSID, calibration, serial, voltage");
            Console.WriteLine("  --list-devices            List available serial and J2534 devices with index numbers");
            Console.WriteLine();
            Console.WriteLine("Device selection:");
            Console.WriteLine("  --device <number>         Select by index shown in --list-devices");
            Console.WriteLine("  --device COM3             Select a serial port by name");
            Console.WriteLine("  --device OBDX             Select a J2534 device by partial name (case-insensitive)");
            Console.WriteLine("  (omit --device)           Auto-selects when only one device is connected");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  pcmhammer-cli.exe --list-devices");
            Console.WriteLine("  pcmhammer-cli.exe --read");
            Console.WriteLine("  pcmhammer-cli.exe --read backup.bin --device COM3");
            Console.WriteLine("  pcmhammer-cli.exe --test-read --device 3");
            Console.WriteLine("  pcmhammer-cli.exe --write newcal.bin --device OBDX");
            Console.WriteLine("  pcmhammer-cli.exe --test-write newcal.bin --device Mongoose");
            Console.WriteLine("  pcmhammer-cli.exe --get-properties --device COM5");
        }
    }
}
