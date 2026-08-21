// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Outcome of <see cref="Vehicle.PrepareBusFor"/>.
    /// </summary>
    public enum BusPreparation
    {
        /// <summary>This PCM is not on CAN; the caller's VPW flow applies.</summary>
        NotRequired,

        /// <summary>The device is now on CAN and pointed at the PCM.</summary>
        Ready,

        /// <summary>The PCM needs CAN, but this device cannot do CAN.</summary>
        Unavailable,
    }

    public partial class Vehicle
    {
        // What a scan probes for, and the buses it tries. Add more targets/buses here as supported.
        private static readonly Target[] DetectionTargets = { Target.Pcm };
        private static readonly BusProtocol[] DetectionBuses = { BusProtocol.Vpw, BusProtocol.Can500k };

        /// <summary>
        /// The bus the PCM was last found on, or null if it has not been found yet.
        /// </summary>
        /// <remarks>
        /// Probed first next time, so a repeated detection (a connection poll, or one operation after
        /// another) does not pay for a probe of the bus that did not answer last time.
        /// </remarks>
        public BusProtocol? LastDetectedBus { get; private set; }

        // Buses this device has already been reported as unable to use. Detection runs repeatedly
        // (every connection poll), and the same limitation is only worth stating once.
        private readonly HashSet<BusProtocol> reportedUnusableBuses = new HashSet<BusProtocol>();

        /// <summary>
        /// Tell the user, once, that the interface cannot reach a bus. A VPW-only interface cannot
        /// see a CAN PCM at all, which is otherwise indistinguishable from an absent PCM.
        /// </summary>
        private void ReportUnusableBus(BusProtocol bus)
        {
            if (this.reportedUnusableBuses.Add(bus))
            {
                this.logger.AddUserMessage($"This device cannot use the {bus} bus, so a {bus} module cannot be found with it.");
            }
        }

        /// <summary>
        /// The detection buses, with the one that answered last time first.
        /// </summary>
        private IEnumerable<BusProtocol> BusesToProbe()
        {
            if (this.LastDetectedBus.HasValue)
            {
                yield return this.LastDetectedBus.Value;
            }

            foreach (BusProtocol bus in DetectionBuses)
            {
                if (bus != this.LastDetectedBus)
                {
                    yield return bus;
                }
            }
        }

        /// <summary>
        /// Scan every bus the device supports, OSID-probing each target, and return the modules that
        /// answer. Leaves the device on the last bus probed; callers that intend to operate should
        /// then select a module (see SelectModule / DetectAndSelectPcm).
        /// </summary>
        public async Task<List<DetectedModule>> DetectModules(CancellationToken cancellationToken)
        {
            List<DetectedModule> found = new List<DetectedModule>();

            foreach (BusProtocol bus in DetectionBuses)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!await this.device.SetProtocol(bus))
                {
                    this.ReportUnusableBus(bus);
                    continue;
                }

                await this.device.SetTimeout(TimeoutScenario.Detect);

                foreach (Target target in DetectionTargets)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    this.SetTarget(target);

                    Response<uint> osid = await this.ProbeOsid(bus, cancellationToken);
                    if (osid.Status == ResponseStatus.Success)
                    {
                        this.logger.AddDebugMessage($"Detected {target.Name} on {bus}, OSID {osid.Value}.");
                        found.Add(new DetectedModule(bus, target, osid.Value));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Scan the buses for modules and report what answered to the logger. Pure presentation over
        /// DetectModules so every UI shares one implementation; returns true if at least one module
        /// responded.
        /// </summary>
        public async Task<bool> DetectAndReportModules(CancellationToken cancellationToken)
        {
            this.logger.AddUserMessage("Scanning the bus for modules...");
            List<DetectedModule> found = await this.DetectModules(cancellationToken);
            if (found.Count == 0)
            {
                this.logger.AddUserMessage("No modules detected on any supported bus.");
                return false;
            }

            foreach (DetectedModule module in found)
            {
                OSIDInfo info = new OSIDInfo(module.Osid);
                this.logger.AddUserMessage($"Found {module.Target.Name} on {module.Bus}: OSID {module.Osid} ({info.Description})");
            }

            return true;
        }

        /// <summary>
        /// Find the PCM by scanning, select its bus and target, and return it; null if no PCM answers.
        /// </summary>
        public async Task<DetectedModule?> DetectAndSelectPcm(CancellationToken cancellationToken)
        {
            // Stop at the first bus the PCM answers on, so a VPW read is not slowed by a CAN probe.
            foreach (BusProtocol bus in this.BusesToProbe())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!await this.device.SetProtocol(bus))
                {
                    this.ReportUnusableBus(bus);
                    continue;
                }

                await this.device.SetTimeout(TimeoutScenario.Detect);

                this.SetTarget(Target.Pcm);
                Response<uint> osid = await this.ProbeOsid(bus, cancellationToken);
                if (osid.Status == ResponseStatus.Success)
                {
                    DetectedModule pcm = new DetectedModule(bus, Target.Pcm, osid.Value);
                    this.LastDetectedBus = bus;
                    await this.SelectModule(pcm);
                    return pcm;
                }
            }

            this.LastDetectedBus = null;
            return null;
        }

        /// <summary>Create a CanCommands bound to this vehicle's device.</summary>
        public CanCommands CreateCanCommands() => new CanCommands(this.device, this.logger, this.UserDefinedKey);

        /// <summary>
        /// Point the command layer at a specific module: set the VPW destination used by the
        /// protocol's message builders, and (on a CAN-capable device) the CAN request/response
        /// ids the device transmits to and filters on. Bus selection is separate (SetProtocol).
        /// </summary>
        public void SetTarget(Target target)
        {
            this.protocol.TargetVpwId = target.VpwId;

            if (this.device is ICanTarget canTarget)
            {
                canTarget.TxCanId = target.CanRequestId;
                canTarget.RxCanId = target.CanResponseId;
            }
        }

        /// <summary>
        /// Point the device and protocol at a detected module's bus and target, ready for normal
        /// operations (restoring a normal receive timeout after the fast detection probes).
        /// </summary>
        public async Task SelectModule(DetectedModule module)
        {
            await this.device.SetProtocol(module.Bus);
            this.SetTarget(module.Target);
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);
        }

        /// <summary>
        /// Put the device on the given bus (e.g. back to VPW after a scan found nothing on CAN).
        /// Returns false if the device cannot do that bus.
        /// </summary>
        public Task<bool> SelectBus(BusProtocol bus) => this.device.SetProtocol(bus);

        /// <summary>
        /// Put the device on whichever bus this PCM's profile says it lives on, and point it at the
        /// PCM. Call this once the PCM type is known, however it became known - detection, an OSID
        /// query, a type the user forced, or a type inferred from the file.
        /// </summary>
        /// <remarks>
        /// This exists so bus selection is decided in one place from <see cref="OSIDInfo.BusProtocol"/>
        /// rather than at each point a PCM type happens to be resolved. Missing it means the VPW
        /// unlock/kernel flow runs against a CAN PCM: nothing answers, no seed is ever parsed, and the
        /// operation grinds through its full retry budget before failing.
        /// </remarks>
        public async Task<BusPreparation> PrepareBusFor(OSIDInfo pcmInfo)
        {
            if (pcmInfo.BusProtocol != BusProtocol.Can500k)
            {
                return BusPreparation.NotRequired;
            }

            this.SetTarget(Target.Pcm);
            return await this.SelectBus(BusProtocol.Can500k)
                ? BusPreparation.Ready
                : BusPreparation.Unavailable;
        }

        /// <summary>
        /// Quick OSID probe on the current bus: VPW uses the block-read OSID request, CAN uses GMLAN
        /// ReadDataByIdentifier, trying each candidate DID (1A C9, then 1A C1) until one answers. One
        /// attempt per DID at the caller's fast Detect timeout.
        /// </summary>
        private async Task<Response<uint>> ProbeOsid(BusProtocol bus, CancellationToken cancellationToken)
        {
            if (bus == BusProtocol.Can500k)
            {
                // First DID that answers with a usable value is the OSID; 0xC1 is the reliable source
                // across families and 0xC9 is the fallback. A module that answers but leaves the slot
                // empty (all zeroes or all ones) is treated as no answer, so the next DID is tried.
                // A negative or absent DID returns fast, so this costs at most one extra probe.
                Response<uint> result = Response.Create(ResponseStatus.Timeout, 0u);
                foreach (byte did in Gmlan.OperatingSystemDids)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Query<uint> canQuery = this.CreateCanOsidQuery(did, cancellationToken);
                    canQuery.MaxTimeouts = 1;   // detection: fail fast on a bus with nothing there
                    Response<uint> candidate = await canQuery.Execute();
                    if (candidate.Status == ResponseStatus.Success && Gmlan.IsUsableOsid(candidate.Value))
                    {
                        return candidate;
                    }

                    // Keep the last response so an all-empty result still reports a sensible status.
                    result = candidate;
                }

                return result;
            }

            Query<uint> query = new Query<uint>(
                this.device,
                this.protocol.CreateOperatingSystemIdReadRequest,
                this.protocol.ParseUInt32FromBlockReadResponse,
                this.logger, cancellationToken, this.notifier);
            query.MaxTimeouts = 1;   // detection: fail fast on a bus with nothing there
            return await query.Execute();
        }

        /// <summary>
        /// Build a CAN OSID probe for one DID: GMLAN ReadDataByIdentifier (1A did) with the leading four
        /// data bytes read as a big-endian uint32. Drops the device's transmit-echo frame (leads with
        /// 0x00) the same way CanCommands.AcceptResponses does, so detection is no more permissive than
        /// the operations that follow it.
        /// </summary>
        private Query<uint> CreateCanOsidQuery(byte did, CancellationToken cancellationToken)
        {
            Gmlan gmlan = new Gmlan();
            return new Query<uint>(
                this.device,
                () => gmlan.CreateReadByIdRequest(did),
                (message) =>
                {
                    Response<byte[]> data = gmlan.ParseReadByIdResponse(message, did);
                    if (data.Status != ResponseStatus.Success || data.Value.Length < 4)
                    {
                        return Response.Create(data.Status, 0u);
                    }
                    uint value = (uint)((data.Value[0] << 24) | (data.Value[1] << 16) | (data.Value[2] << 8) | data.Value[3]);
                    return Response.Create(ResponseStatus.Success, value);
                },
                this.logger, cancellationToken, notifier: null,
                acceptInbound: _ => (m =>
                {
                    byte[] bytes = m?.GetBytes() ?? System.Array.Empty<byte>();
                    return bytes.Length > 0 && bytes[0] != 0x00;
                }));
        }
    }
}
