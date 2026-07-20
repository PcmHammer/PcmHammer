// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class Vehicle
    {
        // What a scan probes for, and the buses it tries. Add more targets/buses here as supported.
        private static readonly Target[] DetectionTargets = { Target.Pcm };
        private static readonly BusProtocol[] DetectionBuses = { BusProtocol.Vpw, BusProtocol.Can500k };

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
                if (!await this.device.SetProtocol(bus)) continue;   // device can't do this bus
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
            foreach (BusProtocol bus in DetectionBuses)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!await this.device.SetProtocol(bus)) continue;   // device can't do this bus
                await this.device.SetTimeout(TimeoutScenario.Detect);

                this.SetTarget(Target.Pcm);
                Response<uint> osid = await this.ProbeOsid(bus, cancellationToken);
                if (osid.Status == ResponseStatus.Success)
                {
                    DetectedModule pcm = new DetectedModule(bus, Target.Pcm, osid.Value);
                    await this.SelectModule(pcm);
                    return pcm;
                }
            }

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
