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
            PcmType forcePcmType = PcmType.Undefined;

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
                    case "--force-pcm":
                        // Skip auto-detection and use the named profile. Needed when a PCM's OSID is
                        // not in the table, which otherwise aborts the read as unsupported.
                        if (i + 1 < args.Length)
                        {
                            string wanted = args[++i];
                            if (!Enum.TryParse(wanted, ignoreCase: true, out forcePcmType) ||
                                forcePcmType == PcmType.Undefined)
                            {
                                Console.Error.WriteLine($"Error: unknown PCM type \"{wanted}\".");
                                Console.Error.WriteLine("Known: " + string.Join(", ",
                                    Enum.GetNames(typeof(PcmType)).Where(n => n != nameof(PcmType.Undefined))));
                                return 1;
                            }
                        }
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
                                () => Task.FromResult(forcePcmType),
                                alert,
                                promptForYesNo,
                                cts.Token);
                            success = await readManager.Read(filePath, forcePcmType);
                            break;
                        }
                        case "test-read":
                        {
                            var readManager = new ReadManager(
                                logger,
                                vehicle,
                                invoke,
                                () => Task.FromResult<string?>(null),
                                () => Task.FromResult(forcePcmType),
                                alert,
                                promptForYesNo,
                                cts.Token);
                            var stream = await readManager.Read((IProgress<ProgressUpdate>?)null, forcePcmType);
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
            int index = 1;

            Console.WriteLine("Available serial devices:");
            if (serialPorts.Length == 0)
                Console.WriteLine("  (none found)");
            else
                foreach (var port in serialPorts)
                    Console.WriteLine($"  [{index++}] {port}");

#if !LINUX_CLI
            var j2534Devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);

            Console.WriteLine();

            Console.WriteLine("Available J2534 devices:");
            if (j2534Devices.Count == 0)
                Console.WriteLine("  (none found)");
            else
                foreach (var d in j2534Devices)
                    Console.WriteLine($"  [{index++}] {d.Name}");
#endif
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
#if !LINUX_CLI
            var j2534Devices = J2534DeviceFinder.FindInstalledJ2534DLLs(logger);
#endif

            if (deviceSpec == null)
            {
#if LINUX_CLI
                // Serial-only build: auto-select the sole serial port, if there is exactly one.
                if (serialPorts.Length == 0)
                {
                    Console.Error.WriteLine("Error: No serial devices found. Connect a device (e.g. /dev/ttyUSB0) and try again.");
                    return null;
                }
                if (serialPorts.Length > 1)
                {
                    Console.Error.WriteLine("Error: Multiple serial devices found. Use --device to select one.");
                    Console.Error.WriteLine("  Run --list-devices to see available options.");
                    return null;
                }
                logger.AddUserMessage("Auto-selected: " + serialPorts[0]);
                return DeviceFactory.AutoDetectSerialDevice(serialPorts[0], logger).GetAwaiter().GetResult();
#else
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
#endif
            }

            // Integer index into the --list-devices list
            if (int.TryParse(deviceSpec, out int index) && index >= 1)
            {
                if (index <= serialPorts.Length)
                {
                    string port = serialPorts[index - 1];
                    logger.AddUserMessage($"Selected [{index}] {port}");
                    return DeviceFactory.AutoDetectSerialDevice(port, logger).GetAwaiter().GetResult();
                }
#if !LINUX_CLI
                int j2534Index = index - serialPorts.Length - 1;
                if (j2534Index < j2534Devices.Count)
                {
                    string name = j2534Devices[j2534Index].Name;
                    logger.AddUserMessage($"Selected [{index}] {name}");
                    return DeviceFactory.CreateJ2534Device(name, logger);
                }
#endif
                Console.Error.WriteLine($"Error: Index {index} is out of range. Run --list-devices to see options.");
                return null;
            }

            // Serial port by name. An optional ":<type>" suffix forces a specific serial device type
            // and skips auto-detect (e.g. "COM24:slcan" or "/dev/ttyUSB0:slcan"), which is the way to
            // select a CAN-only adapter that does not answer the auto-detect probes.
            if (LooksLikeSerialPortName(deviceSpec))
            {
                int separator = deviceSpec.IndexOf(':');
                if (separator > 0)
                {
                    string portName = deviceSpec.Substring(0, separator);
                    string typeHint = deviceSpec.Substring(separator + 1);
                    string? serialType = ResolveSerialDeviceType(typeHint);
                    if (serialType == null)
                    {
                        Console.Error.WriteLine($"Error: unknown serial device type \"{typeHint}\". Known: avt, xpro, elm, slcan.");
                        return null;
                    }
                    logger.AddUserMessage($"Selected {serialType} on {portName}");
                    return DeviceFactory.CreateSerialDevice(portName, serialType, logger);
                }

                return DeviceFactory.AutoDetectSerialDevice(deviceSpec, logger).GetAwaiter().GetResult();
            }

#if LINUX_CLI
            Console.Error.WriteLine($"Error: No serial device matching \"{deviceSpec}\" found. Use a port path like /dev/ttyUSB0, or run --list-devices.");
            return null;
#else
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
#endif
        }

        // True when the device spec names a serial port rather than a J2534 device or list index.
        // Windows serial ports are "COMn"; on Linux they are device paths such as /dev/ttyUSB0.
        static bool LooksLikeSerialPortName(string deviceSpec)
        {
            if (deviceSpec.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return true;
#if LINUX_CLI
            if (deviceSpec.StartsWith("/dev/", StringComparison.Ordinal))
                return true;
#endif
            return false;
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

            // "xpro" is the short name these devices go by, mapped explicitly so it stays valid
            // regardless of how the full device type string is worded.
            if (string.Equals(hint, "xpro", StringComparison.OrdinalIgnoreCase))
                return OBDXProDevice.DeviceType;

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

        // The "Identify PCM" operation. One shared flow (Vehicle.ReadIdentity) detects the bus and reads
        // the identification for both VPW and CAN - the same implementation every UI (WinForms, WPF, Uno)
        // uses - so the CLI just prints the lines it returns.
        static async Task<bool> IdentifyPcm(Vehicle vehicle, ILogger logger, CancellationToken token)
        {
            PcmIdentity? identity = await vehicle.ReadIdentity(token);
            if (identity == null)
            {
                logger.AddUserMessage("No PCM detected on VPW or CAN.");
                return false;
            }

            foreach (string line in identity.Lines)
            {
                logger.AddUserMessage(line);
            }

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
#if LINUX_CLI
                // The Linux build embeds the kernels in the executable, so a directory with no
                // loose .bin files is normal - the embedded copies are used automatically.
                logger.AddDebugMessage($"No loose .bin kernel files in {dir}; using kernels embedded in the executable.");
#else
                logger.AddUserMessage(
                    $"Warning: no .bin kernel files found in {dir}. " +
                    "Place the Kernel-*.bin / Loader-*.bin files there or pass --kernel-dir <path>.");
#endif
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

            if (protocol == BusProtocol.Vpw)
            {
                // Same 4X rule as the GUIs. InitializeVehicle turns 4X on for every CLI run, so in
                // practice only the "device can't do 4X" warning can fire here.
                VpwMonitorReadiness readiness = BusMonitor.CheckVpwReadiness(vehicle, out string readinessMessage);
                if (readiness != VpwMonitorReadiness.Ready)
                {
                    logger.AddUserMessage(readinessMessage);
                }

                if (readiness == VpwMonitorReadiness.FourXDisabled)
                {
                    return false;
                }
            }

            IReadOnlyCollection<uint>? acceptIds = null;
            if (protocol == BusProtocol.Can500k && !canAll)
            {
                acceptIds = (canIds != null && canIds.Count > 0)
                    ? canIds
                    : BusMonitor.DefaultCanIds;
            }

            logger.AddUserMessage($"Monitoring {protocol}. Press Ctrl+C to stop.");
            if (protocol == BusProtocol.Can500k)
            {
                logger.AddUserMessage(acceptIds == null
                    ? "CAN filter: all ids."
                    : "CAN filter: " + BusMonitor.FormatCanIds(acceptIds));
            }

            BusMonitor monitor = vehicle.CreateBusMonitor();
            await monitor.RunAsync(protocol, acceptIds, line => Console.WriteLine(line), token);
            return true;
        }

        // Prompt for the 5-byte unlock key of a PCM with external security. The library shows the
        // seed too; we repeat it here so the prompt is self-contained. Returns null to abort.
        static byte[]? PromptForSecurityKey(PcmType pcmType, byte[] seed, CancellationToken cancellationToken, ILogger logger)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            Console.WriteLine();
            Console.WriteLine($"{pcmType} security seed: {seed.ToHex(string.Empty)}");
            Console.Write("Enter the 5-byte unlock key (10 hex digits), or blank to abort: ");
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line) || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            byte[]? key = Utility.TryParseHex(line);
            if (key == null || key.Length != 5)
            {
                logger.AddUserMessage("Invalid key: expected 5 hex bytes (10 hex digits).");
                return null;
            }
            return key;
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

            // External 40-bit security (e.g. E92): the tool shows the seed and the user types the
            // key computed by the external algorithm. A proven pair is cached by the library.
            vehicle.SecurityKeyProvider = (pcmType, seed, cancellationToken) =>
                PromptForSecurityKey(pcmType, seed, cancellationToken, logger);

            logger.AddUserMessage(AppName);
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

        // Same program, same assembly name ("pcmhammer-cli"); only the platform's executable
        // suffix differs. Used so the help text shows the command the user actually types.
#if LINUX_CLI
        private const string ExeName = "pcmhammer-cli";
        private const string AppName = "PCM Hammer Linux CLI";
#else
        private const string ExeName = "pcmhammer-cli.exe";
        private const string AppName = "PCM Hammer CLI";
#endif

        static void PrintHelp()
        {
            Console.WriteLine(AppName);
            Console.WriteLine(AppInfo.GetVersionOrBuildLine(Generated.BuildTime));
            Console.WriteLine();
            Console.WriteLine($"Usage:  {ExeName} <operation> [--device <id>] [--kernel-dir <path>] [--debug]");
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
            Console.WriteLine($"                            CAN: list hex ids to filter (default {BusMonitor.DefaultCanFilter}), or 'all'");
            Console.WriteLine("  --brute-force             Search the PCM security key (algo sweep, then numeric range)");
#if LINUX_CLI
            Console.WriteLine("  --list-devices            List available serial devices with index numbers");
#else
            Console.WriteLine("  --list-devices            List available serial and J2534 devices with index numbers");
#endif
            Console.WriteLine();
            Console.WriteLine("Brute force options:");
            Console.WriteLine("  --range <START-END>       Numeric key range in hex (default 0000-FFFF)");
            Console.WriteLine("  --no-algo-sweep           Skip the 256-algorithm sweep; try the numeric range only");
            Console.WriteLine("  --delay <1-12>            Search speed in seconds per attempt (default 2; omit for Auto)");
            Console.WriteLine();
            Console.WriteLine("Device selection:");
            Console.WriteLine("  --device <number>         Select by index shown in --list-devices");
#if LINUX_CLI
            Console.WriteLine("  --device /dev/ttyUSB0     Select a serial port by device path");
            Console.WriteLine("  --device /dev/ttyUSB0:dev Force a serial device type (avt, xpro, elm, slcan)");
#else
            Console.WriteLine("  --device COM3             Select a serial port by name");
            Console.WriteLine("  --device OBDX             Select a J2534 device by partial name (case-insensitive)");
#endif
            Console.WriteLine("  (omit --device)           Auto-selects when only one device is connected");
            Console.WriteLine();
            Console.WriteLine("Kernels:");
#if LINUX_CLI
            Console.WriteLine("  --kernel-dir <path>       Directory of loose Kernel-*.bin / Loader-*.bin (overrides embedded)");
            Console.WriteLine("  (omit --kernel-dir)       Uses the kernels embedded in this binary");
#else
            Console.WriteLine("  --kernel-dir <path>       Directory holding Kernel-*.bin / Loader-*.bin");
            Console.WriteLine("  (omit --kernel-dir)       Defaults to the current working directory");
#endif
            Console.WriteLine();
            Console.WriteLine("PCM selection:");
            Console.WriteLine("  --force-pcm <type>        Skip auto-detection and use this profile (e.g. E38, P01, P12).");
            Console.WriteLine("                            Use when a PCM's OSID is not recognised. The wrong profile");
            Console.WriteLine("                            uploads the wrong kernel, so only use it deliberately.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
#if LINUX_CLI
            Console.WriteLine($"  {ExeName} --list-devices");
            Console.WriteLine($"  {ExeName} --read");
            Console.WriteLine($"  {ExeName} --read backup.bin --device /dev/ttyUSB0");
            Console.WriteLine($"  {ExeName} --test-read --device 1");
            Console.WriteLine($"  {ExeName} --write newcal.bin --device /dev/ttyUSB0");
            Console.WriteLine($"  {ExeName} --test-write newcal.bin --device /dev/ttyACM0");
            Console.WriteLine($"  {ExeName} --identify-pcm --device /dev/ttyUSB0");
            Console.WriteLine($"  {ExeName} --brute-force --device /dev/ttyUSB0");
            Console.WriteLine($"  {ExeName} --brute-force --range 0000-00FF --no-algo-sweep --device /dev/ttyUSB0");
            Console.WriteLine($"  {ExeName} --monitor can all --device /dev/ttyUSB0:slcan");
            Console.WriteLine($"  {ExeName} --test-read --device /dev/ttyUSB0 --kernel-dir ./kernels");
#else
            Console.WriteLine($"  {ExeName} --list-devices");
            Console.WriteLine($"  {ExeName} --read");
            Console.WriteLine($"  {ExeName} --read backup.bin --device COM3");
            Console.WriteLine($"  {ExeName} --test-read --device 3");
            Console.WriteLine($"  {ExeName} --write newcal.bin --device OBDX");
            Console.WriteLine($"  {ExeName} --test-write newcal.bin --device Mongoose");
            Console.WriteLine($"  {ExeName} --identify-pcm --device COM5");
            Console.WriteLine($"  {ExeName} --brute-force --device COM3");
            Console.WriteLine($"  {ExeName} --brute-force --range 0000-00FF --no-algo-sweep --device OBDX");
            Console.WriteLine($"  {ExeName} --test-read --device COM6 --kernel-dir C:\\PcmHammer\\Kernels");
#endif
        }
    }
}
