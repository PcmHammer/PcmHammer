// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
            string? kernelDirArg = null;
            string? rangeSpec = null;
            bool algoSweep = true;
            int delaySeconds = BruteForcer.DefaultSecurityDelaySeconds;
            bool listDevices = false;
            bool debug = false;
            BusProtocol monitorProtocol = BusProtocol.Vpw;
            List<uint>? monitorCanIds = null;
            bool monitorCanAll = false;

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
                    case "--verify":
                        operation = "verify";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) filePath = args[++i];
                        break;
                    case "--test-read":
                        operation = "test-read";
                        break;
                    case "--identify-pcm":
                        operation = "identify-pcm";
                        break;
                    case "--detect":
                        operation = "detect";
                        break;
                    case "--brute-force":
                        operation = "brute-force";
                        break;
                    case "--monitor":
                        operation = "monitor";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            string proto = args[++i].ToLowerInvariant();
                            monitorProtocol = (proto == "can" || proto == "can500k" || proto == "500k")
                                ? BusProtocol.Can500k
                                : BusProtocol.Vpw;
                        }
                        // Optional CAN id list (hex), or "all" for the whole bus. Ignored for VPW.
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            if (args[i + 1].Equals("all", StringComparison.OrdinalIgnoreCase))
                            {
                                monitorCanAll = true;
                                i++;
                            }
                            else if (uint.TryParse(args[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint canId))
                            {
                                (monitorCanIds ??= new List<uint>()).Add(canId);
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        break;
                    case "--range":
                        if (i + 1 < args.Length) rangeSpec = args[++i];
                        break;
                    case "--no-algo-sweep":
                        algoSweep = false;
                        break;
                    case "--delay":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedDelay))
                        {
                            delaySeconds = parsedDelay;
                            i++;
                        }
                        break;
                    case "--device":
                        if (i + 1 < args.Length) deviceSpec = args[++i];
                        break;
                    case "--kernel-dir":
                        if (i + 1 < args.Length) kernelDirArg = args[++i];
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

            if (filePath == null && operation != "read" && operation != "test-read" && operation != "identify-pcm" && operation != "brute-force" && operation != "detect" && operation != "monitor")
            {
                Console.Error.WriteLine($"Error: No file path specified for --{operation}.");
                return 1;
            }

            string? kernelDir = ResolveKernelDir(kernelDirArg, logger);
            if (kernelDir == null)
                return 1;

            // Resolving a serial device probes the port, which throws (e.g. TimeoutException) when the
            // port exists but nothing is connected. Catch it so a dead port reports cleanly instead of
            // crashing the process with an unhandled exception.
            Device? device;
            try
            {
                device = ResolveDevice(deviceSpec, logger);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: could not open the selected device: {ex.Message}");
                if (debug)
                    Console.Error.WriteLine(ex.ToString());
                return 1;
            }
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
                            Console.Error.WriteLine("\nOperation in progress - waiting for clean shutdown. Press Ctrl+C again to force quit.");
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
                                () => Task.FromResult(PcmType.Undefined),
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
                                () => Task.FromResult(PcmType.Undefined),
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
                        case "verify":
                        {
                            // CRC-compare the file against the PCM (no erase/write). Triggers the
                            // kernel's ProcessCRC (mode 3D02) over each range, which is what we
                            // need to exercise the RX-FIFO-during-CRC behaviour on the bench.
                            var writeManager = new WriteManager(
                                logger,
                                vehicle,
                                WriteType.Compare,
                                alert,
                                promptForYesNo,
                                cts.Token);
                            success = await writeManager.Write(filePath!);
                            break;
                        }
                        case "identify-pcm":
                        {
                            success = await IdentifyPcm(vehicle, logger, cts.Token);
                            break;
                        }
                        case "detect":
                        {
                            success = await vehicle.DetectAndReportModules(cts.Token);
                            break;
                        }
                        case "brute-force":
                        {
                            success = await BruteForceUnlock(vehicle, logger, rangeSpec, algoSweep, delaySeconds, cts.Token);
                            break;
                        }
                        case "monitor":
                        {
                            success = await RunMonitor(vehicle, logger, monitorProtocol, monitorCanIds, monitorCanAll, cts.Token);
                            break;
                        }
                    }

                    operationInProgress = false;
                    return success ? 0 : 1;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TimeoutException)
                {
                    // A missing, in-use, or unresponsive port (e.g. the selected COM port is gone)
                    // is an expected condition, not a crash - report it concisely. The full detail
                    // is available with --debug.
                    Console.Error.WriteLine($"Error: could not connect to the selected device: {ex.Message}");
                    if (debug)
                        Console.Error.WriteLine(ex.ToString());
                    return 1;
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

            // Serial port - exact name match (case-insensitive). An optional ":<type>" suffix forces
            // a specific serial device type and skips auto-detect (e.g. "COM24:slcan"), which is the
            // way to select a CAN-only adapter that does not answer the auto-detect probes.
            if (deviceSpec.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                int separator = deviceSpec.IndexOf(':');
                if (separator > 0)
                {
                    string portName = deviceSpec.Substring(0, separator);
                    string typeHint = deviceSpec.Substring(separator + 1);
                    string? serialType = ResolveSerialDeviceType(typeHint);
                    if (serialType == null)
                    {
                        Console.Error.WriteLine($"Error: unknown serial device type \"{typeHint}\". Known: avt, obdx, elm, slcan.");
                        return null;
                    }
                    logger.AddUserMessage($"Selected {serialType} on {portName}");
                    return DeviceFactory.CreateSerialDevice(portName, serialType, logger);
                }

                return DeviceFactory.AutoDetectSerialDevice(deviceSpec, logger).GetAwaiter().GetResult();
            }

            // J2534 - case-insensitive substring match
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

        // Map a short serial-device-type hint (from a "COMx:<type>" spec) to a known device type,
        // by exact or unique case-insensitive substring match. Returns null if unknown/ambiguous.
        static string? ResolveSerialDeviceType(string hint)
        {
            string[] knownTypes =
            {
                SlcanDevice.DeviceType,
                AvtDevice.DeviceType,
                OBDXProDevice.DeviceType,
                ElmDevice.DeviceType,
            };

            foreach (string type in knownTypes)
            {
                if (string.Equals(type, hint, StringComparison.OrdinalIgnoreCase))
                    return type;
            }

            var matches = knownTypes
                .Where(t => t.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        // Mirrors the WinForms "Identify PCM" button. First detects what is on the bus and
        // selects its protocol (the shared first step of every operation), then reads the rest of
        // the identification on the selected bus. The OSID comes from detection; VIN, calibration,
        // hardware ID, serial number, BCC, MEC, and voltage follow over VPW, with the same
        // hardware-type rules as the WinForms implementation. On CAN the GMLAN DID set is read and
        // formatted via CanIdentification (VIN, traceability code, OSID, module ids).
        static async Task<bool> IdentifyPcm(Vehicle vehicle, ILogger logger, CancellationToken token)
        {
            DetectedModule? pcm = await vehicle.DetectAndSelectPcm(token);
            if (pcm == null)
            {
                logger.AddUserMessage("No PCM detected on VPW or CAN.");
                return false;
            }

            logger.AddUserMessage("Detected PCM on " + pcm.Bus);

            if (pcm.Bus != BusProtocol.Vpw)
            {
                logger.AddUserMessage("PCM Identification:");
                foreach (string line in await CanIdentification.Read(vehicle.CreateCanCommands(), token))
                {
                    logger.AddUserMessage(line);
                }
                return true;
            }

            OSIDInfo pcmInfo = new OSIDInfo(pcm.Osid);
            logger.AddUserMessage("OSID: " + pcm.Osid);
            logger.AddUserMessage("Description: " + pcmInfo.Description);

            var vinResponse = await vehicle.QueryVin();
            if (vinResponse.Status == ResponseStatus.Success)
                logger.AddUserMessage("VIN: " + vinResponse.Value);
            else
                logger.AddUserMessage("VIN query failed: " + vinResponse.Status);

            if (pcmInfo.HardwareType != PcmType.BlackBox)
            {
                var calResponse = await vehicle.QueryCalibrationId();
                if (calResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Calibration ID: " + calResponse.Value);
                else
                    logger.AddUserMessage("Calibration ID query failed: " + calResponse.Status);
            }

            if (pcmInfo.HardwareType != PcmType.P05 &&
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

            if (pcmInfo.HardwareType != PcmType.BlackBox)
            {
                var serialResponse = await vehicle.QuerySerial();
                if (serialResponse.Status == ResponseStatus.Success)
                    logger.AddUserMessage("Serial Number: " + serialResponse.Value);
                else
                    logger.AddUserMessage("Serial Number query failed: " + serialResponse.Status);
            }

            if (pcmInfo.HardwareType != PcmType.P04 &&
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

        // Brute-forces the PCM's security access: optionally sweeps the 256 known GM key algorithms
        // first, then tries the numeric key range. All the search/timing logic lives in PcmLibrary's
        // BruteForcer; this just parses the CLI options, selects the bus, and surfaces the outcome.
        static async Task<bool> BruteForceUnlock(Vehicle vehicle, ILogger logger, string? rangeSpec, bool algoSweep, int delaySeconds, CancellationToken token)
        {
            int start = 0x0000;
            int end = 0xFFFF;
            if (!string.IsNullOrWhiteSpace(rangeSpec) && !TryParseHexRange(rangeSpec!, out start, out end))
            {
                Console.Error.WriteLine("Error: --range must be START-END in hex (1-4 digits each), e.g. 0000-FFFF.");
                return false;
            }

            // Detect the bus and pick the matching security-access provider (the brute forcer and its
            // timing are shared; only the seed/key transport and key table differ by bus).
            DetectedModule? pcm = await vehicle.DetectAndSelectPcm(token);
            if (pcm == null)
            {
                logger.AddUserMessage("No PCM detected on VPW or CAN.");
                return false;
            }
            ISecurityAccess access = pcm.Bus == BusProtocol.Can500k ? vehicle.CreateCanCommands() : (ISecurityAccess)vehicle;

            // The brute forcer logs one "Sweeping/Trying <key>" line per key, which is enough to show
            // progress on the console. The countdown timer is a GUI affordance, so the CLI needs none.
            var bruteForcer = new BruteForcer(access, logger);
            BruteForceResult result = await bruteForcer.BruteForce(start, end, algoSweep, delaySeconds, token);

            // BruteForce logs the detailed outcome itself; map it to a process success result.
            switch (result.Outcome)
            {
                case BruteForceOutcome.Found:
                case BruteForceOutcome.AlreadyUnlocked:
                case BruteForceOutcome.UnlockNotRequired:
                    return true;
                default:
                    return false;
            }
        }

        // Parses a "START-END" hex range into two 16-bit values. Hex parsing stays in the front
        // end (PcmLibrary is not called to parse UI input), using the same UInt16.TryParse +
        // NumberStyles.HexNumber idiom as the WinForms dialogs.
        static bool TryParseHexRange(string spec, out int start, out int end)
        {
            start = 0x0000;
            end = 0xFFFF;

            string[] parts = spec.Split('-');
            if (parts.Length != 2)
                return false;

            if (!TryParseHex16(parts[0], out start) || !TryParseHex16(parts[1], out end))
                return false;

            return end >= start;
        }

        static bool TryParseHex16(string text, out int value)
        {
            if (UInt16.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt16 parsed))
            {
                value = parsed;
                return true;
            }

            value = 0;
            return false;
        }

        // Resolves the directory the kernel/loader .bin files are loaded from.
        // Kernels are external (not embedded): use --kernel-dir if given, otherwise the
        // current working directory. Returns null (with an error printed) if an explicit
        // --kernel-dir does not exist.
        static string? ResolveKernelDir(string? kernelDirArg, ILogger logger)
        {
            string dir = string.IsNullOrWhiteSpace(kernelDirArg)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(kernelDirArg);

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Error: Kernel directory not found: {dir}");
                return null;
            }

            logger.AddDebugMessage("Using kernel directory: " + dir);

            if (Directory.GetFiles(dir, "*.bin").Length == 0)
            {
                logger.AddUserMessage(
                    $"Warning: no .bin kernel files found in {dir}. " +
                    "Place the Kernel-*.bin / Loader-*.bin files there or pass --kernel-dir <path>.");
            }

            return dir;
        }

        /// <summary>
        /// Passively display bus traffic until cancelled. CAN defaults to the 7E0/7E8/101 ids unless
        /// specific ids or "all" were given; VPW shows everything and follows 4X automatically.
        /// </summary>
        static async Task<bool> RunMonitor(Vehicle vehicle, ILogger logger, BusProtocol protocol, List<uint>? canIds, bool canAll, CancellationToken token)
        {
            if (!vehicle.MonitorableProtocols.Contains(protocol))
            {
                logger.AddUserMessage($"The selected device cannot monitor {protocol}.");
                return false;
            }

            if (protocol == BusProtocol.Vpw && !vehicle.Supports4X)
            {
                logger.AddUserMessage("This device can't switch to 4X; 4X reads will not be captured.");
            }

            IReadOnlyCollection<uint>? acceptIds = null;
            if (protocol == BusProtocol.Can500k && !canAll)
            {
                acceptIds = (canIds != null && canIds.Count > 0)
                    ? canIds
                    : new List<uint> { 0x7E0, 0x7E8, 0x101 };
            }

            logger.AddUserMessage($"Monitoring {protocol}. Press Ctrl+C to stop.");
            if (protocol == BusProtocol.Can500k)
            {
                logger.AddUserMessage(acceptIds == null
                    ? "CAN filter: all ids."
                    : "CAN filter: " + string.Join(" ", acceptIds.Select(id => id.ToString("X3"))));
            }

            BusMonitor monitor = vehicle.CreateBusMonitor();
            await monitor.RunAsync(protocol, acceptIds, line => Console.WriteLine(line), token);
            return true;
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
            Console.WriteLine("Usage:  pcmhammer-cli.exe <operation> [--device <id>] [--kernel-dir <path>] [--debug]");
            Console.WriteLine();
            Console.WriteLine("Operations:");
            Console.WriteLine("  --read [file]             Read entire PCM to file (auto-names if omitted)");
            Console.WriteLine("  --test-read               Read entire PCM (auto-detects VPW or CAN) without saving");
            Console.WriteLine("  --write <file>            Write entire PCM from file");
            Console.WriteLine("  --test-write <file>       Test write (no permanent changes)");
            Console.WriteLine("  --verify <file>           CRC-compare file against PCM (no erase/write)");
            Console.WriteLine("  --identify-pcm            Read VIN, OSID, calibration, serial, voltage");
            Console.WriteLine("  --detect                  Scan the buses (VPW, CAN) and list the modules that respond");
            Console.WriteLine("  --monitor [vpw|can] [ids] Passively display bus traffic until Ctrl+C (default vpw)");
            Console.WriteLine("                            CAN: list hex ids to filter (default 7E0 7E8 101), or 'all'");
            Console.WriteLine("  --brute-force             Search the PCM security key (algo sweep, then numeric range)");
            Console.WriteLine("  --list-devices            List available serial and J2534 devices with index numbers");
            Console.WriteLine();
            Console.WriteLine("Brute force options:");
            Console.WriteLine("  --range <START-END>       Numeric key range in hex (default 0000-FFFF)");
            Console.WriteLine("  --no-algo-sweep           Skip the 256-algorithm sweep; try the numeric range only");
            Console.WriteLine("  --delay <1-12>            Search speed in seconds per attempt (default 2; omit for Auto)");
            Console.WriteLine();
            Console.WriteLine("Device selection:");
            Console.WriteLine("  --device <number>         Select by index shown in --list-devices");
            Console.WriteLine("  --device COM3             Select a serial port by name");
            Console.WriteLine("  --device OBDX             Select a J2534 device by partial name (case-insensitive)");
            Console.WriteLine("  (omit --device)           Auto-selects when only one device is connected");
            Console.WriteLine();
            Console.WriteLine("Kernels:");
            Console.WriteLine("  --kernel-dir <path>       Directory holding Kernel-*.bin / Loader-*.bin");
            Console.WriteLine("  (omit --kernel-dir)       Defaults to the current working directory");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  pcmhammer-cli.exe --list-devices");
            Console.WriteLine("  pcmhammer-cli.exe --read");
            Console.WriteLine("  pcmhammer-cli.exe --read backup.bin --device COM3");
            Console.WriteLine("  pcmhammer-cli.exe --test-read --device 3");
            Console.WriteLine("  pcmhammer-cli.exe --write newcal.bin --device OBDX");
            Console.WriteLine("  pcmhammer-cli.exe --test-write newcal.bin --device Mongoose");
            Console.WriteLine("  pcmhammer-cli.exe --identify-pcm --device COM5");
            Console.WriteLine("  pcmhammer-cli.exe --brute-force --device COM3");
            Console.WriteLine("  pcmhammer-cli.exe --brute-force --range 0000-00FF --no-algo-sweep --device OBDX");
            Console.WriteLine("  pcmhammer-cli.exe --test-read --device COM6 --kernel-dir C:\\PcmHammer\\Kernels");
        }
    }
}
