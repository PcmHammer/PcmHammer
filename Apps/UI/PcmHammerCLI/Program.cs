// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
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
            string? deviceName = null;
            string? deviceCategory = null;
            bool listDevices = false;
            bool debug = false;
            int crcPollDelayMs = 500;

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
                    case "--device":
                        if (i + 1 < args.Length) deviceName = args[++i];
                        break;
                    case "--j2534":
                        deviceCategory = DeviceConfiguration.Constants.DeviceCategoryJ2534;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) deviceName = args[++i];
                        break;
                    case "--serial":
                        deviceCategory = DeviceConfiguration.Constants.DeviceCategorySerial;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) deviceName = args[++i];
                        break;
                    case "--list-devices":
                        listDevices = true;
                        break;
                    case "--debug":
                        debug = true;
                        break;
                    case "--crc-poll-delay":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int parsedDelay))
                            crcPollDelayMs = parsedDelay;
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
                ListJ2534Devices(logger);
                return 0;
            }

            if (operation == null)
            {
                Console.Error.WriteLine("Error: No operation specified.");
                PrintHelp();
                return 1;
            }

            if (filePath == null && operation != "read" && operation != "test-read")
            {
                Console.Error.WriteLine($"Error: No file path specified for --{operation}.");
                return 1;
            }

            string kernelDir = ExtractKernels();

            Device? device = CreateDevice(deviceCategory, deviceName, logger);
            if (device == null)
            {
                Console.Error.WriteLine("Error: No device found.");
                Console.Error.WriteLine("Use --list-devices to see available J2534 devices, or --device to specify one.");
                return 1;
            }

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
                            readManager.CrcPollingDelayMs = crcPollDelayMs;
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
                            readManager.CrcPollingDelayMs = crcPollDelayMs;
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

        static void ListJ2534Devices(ILogger logger)
        {
            Console.WriteLine("Available J2534 devices:");
            var devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);
            if (devices.Count == 0)
            {
                Console.WriteLine("  (none found)");
            }
            else
            {
                foreach (var d in devices)
                {
                    Console.WriteLine("  " + d.Name);
                }
            }
        }

        static Device? CreateDevice(string? deviceCategory, string? deviceName, ILogger logger)
        {
            // Explicit J2534 by name
            if (deviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534 && deviceName != null)
            {
                return DeviceFactory.CreateJ2534Device(deviceName, logger);
            }

            // Explicit serial port
            if (deviceCategory == DeviceConfiguration.Constants.DeviceCategorySerial && deviceName != null)
            {
                return DeviceFactory.AutoDetectSerialDevice(deviceName, logger).GetAwaiter().GetResult();
            }

            // Name given without category: try J2534 first, then serial
            if (deviceName != null)
            {
                var j2534 = DeviceFactory.CreateJ2534Device(deviceName, logger);
                if (j2534 != null) return j2534;
                return DeviceFactory.AutoDetectSerialDevice(deviceName, logger).GetAwaiter().GetResult();
            }

            // Auto-detect: first available J2534
            var j2534Devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);
            if (j2534Devices.Count > 0)
            {
                logger.AddUserMessage("Auto-selected J2534 device: " + j2534Devices[0].Name);
                return DeviceFactory.CreateJ2534Device(j2534Devices[0].Name, logger);
            }

            return null;
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
            Console.WriteLine("PCM Hammer CLI - Read, write, and test-write PCMs");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  pcmhammer-cli.exe --read [filename]       Read entire PCM to file");
            Console.WriteLine("  pcmhammer-cli.exe --test-read             Read entire PCM without saving");
            Console.WriteLine("  pcmhammer-cli.exe --write <filename>      Write entire PCM from file");
            Console.WriteLine("  pcmhammer-cli.exe --test-write <filename> Test write (no permanent changes)");
            Console.WriteLine("  pcmhammer-cli.exe --list-devices          List available J2534 devices");
            Console.WriteLine("  pcmhammer-cli.exe --help  or  /?          Show this help");
            Console.WriteLine();
            Console.WriteLine("Device options (auto-detects first J2534 if not specified):");
            Console.WriteLine("  --device <name>           J2534 device name or serial port");
            Console.WriteLine("  --j2534 <name>            Specify a J2534 device by name");
            Console.WriteLine("  --serial <port>           Specify a serial port (e.g. COM3)");
            Console.WriteLine();
            Console.WriteLine("Other options:");
            Console.WriteLine("  --debug                   Show full debug log stream (includes all user messages)");
            Console.WriteLine("  --crc-poll-delay <ms>     Delay between CRC verification polls (default: 500)");
            Console.WriteLine("                            Increase if the PCM kernel crashes during verification");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  pcmhammer-cli.exe --read mypcm.bin");
            Console.WriteLine("  pcmhammer-cli.exe --read mypcm.bin --device \"OBD XPRO GT\"");
            Console.WriteLine("  pcmhammer-cli.exe --read --crc-poll-delay 500 mypcm.bin");
            Console.WriteLine("  pcmhammer-cli.exe --write newcal.bin --j2534 \"OBD XPRO GT\"");
        }
    }
}
