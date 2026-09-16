// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class Vehicle
    {
        /// <summary>
        /// Detect the PCM, then read and format its identification. Handles both buses: a VPW PCM is
        /// read with the block-read property queries, a CAN PCM with GMLAN ReadDataByIdentifier. Null
        /// when no PCM answered. On success the device is left on the PCM's bus.
        /// </summary>
        /// <remarks>
        /// This is the one identification flow, shared by every UI. The VPW branch mirrors the order
        /// and per-PCM-type gating that the WinForms Identify has always used; the UI only displays
        /// what is returned (fields, a software-module list, and a ready-to-log line list).
        /// </remarks>
        public async Task<PcmIdentity?> ReadIdentity(CancellationToken cancellationToken)
        {
            DetectedModule? pcm = await this.DetectAndSelectPcm(cancellationToken);
            if (pcm == null)
            {
                return null;
            }

            return pcm.Bus == BusProtocol.Can500k
                ? await this.ReadCanIdentity(pcm, cancellationToken)
                : await this.ReadVpwIdentity(pcm, cancellationToken);
        }

        private async Task<PcmIdentity> ReadVpwIdentity(DetectedModule pcm, CancellationToken cancellationToken)
        {
            OSIDInfo info = pcm.Info;
            PcmType type = info.HardwareType;

            List<string> lines = new List<string>
            {
                "Detected PCM on " + pcm.Bus,
                "OSID: " + pcm.Osid,
                "Type: " + type,
                "Description: " + info.Description,
            };

            string vin = await ReadProperty(lines, "VIN", true, () => this.QueryVin());

            // Property availability by PCM type, matching the long-standing VPW Identify behavior.
            string calibrationId = await ReadProperty(lines, "Calibration ID",
                type != PcmType.BlackBox,
                () => this.QueryCalibrationId());

            string hardwareId = await ReadProperty(lines, "Hardware ID",
                type != PcmType.P05 && type != PcmType.P05b && type != PcmType.P10 && type != PcmType.P12 && type != PcmType.P12b && type != PcmType.E54,
                () => this.QueryHardwareId());

            string serialNumber = await ReadProperty(lines, "Serial Number",
                type != PcmType.BlackBox,
                () => this.QuerySerial());

            string broadcastCode = await ReadProperty(lines, "Broad Cast Code",
                type != PcmType.P04 && type != PcmType.P04_Early && type != PcmType.P08,
                () => this.QueryBCC());

            string mec = await ReadProperty(lines, "MEC", true, () => this.QueryMEC());
            string voltage = await ReadProperty(lines, "Voltage", true, () => this.QueryVoltage());

            // Per-segment calibration IDs.
            List<CanIdentification.Item> modules = await this.ReadSegmentIds(lines);

            return new PcmIdentity(
                pcm.Bus, pcm.Osid, info.Description,
                vin, calibrationId, hardwareId, serialNumber, broadcastCode, mec, voltage,
                modules,
                lines);
        }

        // The 0x3C blocks that hold a segment's part number. A PCM answers the ones it has (a P10 has
        // five, a P12 eight), so unsupported blocks are simply skipped rather than gated by type.
        private static readonly byte[] SegmentIdBlocks =
        {
            BlockId.OperatingSystemID, BlockId.EngineCalID, BlockId.EngineDiagCalID,
            BlockId.TransCalID, BlockId.TransDiagID, BlockId.FuelCalID,
            BlockId.SystemCalID, BlockId.SpeedCalID,
        };

        /// <summary>
        /// Read each segment's part number and return the ones the PCM reports. A block that is not
        /// supported answers with a negative response and is left out; so is an empty slot (0 or
        /// 0xFFFFFFFF). Every reported segment is also appended to <paramref name="lines"/>.
        /// </summary>
        private async Task<List<CanIdentification.Item>> ReadSegmentIds(List<string> lines)
        {
            var modules = new List<CanIdentification.Item>();
            foreach (byte block in SegmentIdBlocks)
            {
                Response<UInt32> response = await this.QueryUnsignedValue(
                    () => this.protocol.CreateReadRequest(block), CancellationToken.None);

                if (response.Status != ResponseStatus.Success
                    || response.Value == 0
                    || response.Value == 0xFFFFFFFF)
                {
                    continue;
                }

                string name = BlockId.Names.TryGetValue(block, out string blockName) ? blockName : $"Block 0x{block:X2}";
                lines.Add(name + ": " + response.Value);
                modules.Add(new CanIdentification.Item(name, response.Value.ToString()));
            }

            return modules;
        }

        private async Task<PcmIdentity> ReadCanIdentity(DetectedModule pcm, CancellationToken cancellationToken)
        {
            CanIdentification.Identity identity = await CanIdentification.Query(this.CreateCanCommands(), cancellationToken);

            List<string> lines = new List<string> { "Detected PCM on " + pcm.Bus, "PCM Identification:" };

            // Resolve the OSID/Type/Description header here - the same place the VPW path resolves it -
            // so CanIdentification stays a raw DID formatter and the type database is consulted once.
            OSIDInfo? osidInfo = identity.Osid == 0 ? null : new OSIDInfo(identity.Osid);
            if (osidInfo != null)
            {
                lines.Add("OSID: " + identity.Osid);
                lines.Add("Type: " + osidInfo.HardwareType);
            }

            lines.AddRange(identity.Lines);

            string description = osidInfo?.Description ?? PcmIdentity.Unavailable;
            string vin = string.IsNullOrEmpty(identity.Vin) ? PcmIdentity.Unavailable : identity.Vin;

            // The VIN has its own field; the rest of the identifiers are the software module list. The
            // VPW-only properties do not exist on a CAN PCM.
            List<CanIdentification.Item> modules = identity.Items
                .Where(item => item.Name != CanIdentification.VinName)
                .ToList();

            return new PcmIdentity(
                pcm.Bus, identity.Osid, description,
                vin,
                PcmIdentity.NotApplicable, PcmIdentity.NotApplicable, PcmIdentity.NotApplicable,
                PcmIdentity.NotApplicable, PcmIdentity.NotApplicable, PcmIdentity.NotApplicable,
                modules,
                lines);
        }

        /// <summary>
        /// Run one property query, append its log line, and return the display value: the value on
        /// success, <see cref="PcmIdentity.NotApplicable"/> when the PCM type does not provide it (no
        /// query run, no line), or <see cref="PcmIdentity.Unavailable"/> when it applied but failed.
        /// </summary>
        private static async Task<string> ReadProperty<T>(List<string> lines, string name, bool applicable, Func<Task<Response<T>>> query)
        {
            if (!applicable)
            {
                return PcmIdentity.NotApplicable;
            }

            Response<T> response = await query();
            if (response.Status == ResponseStatus.Success)
            {
                string value = response.Value?.ToString() ?? string.Empty;
                lines.Add(name + ": " + value);
                return value;
            }

            lines.Add(name + " query failed: " + response.Status);
            return PcmIdentity.Unavailable;
        }
    }
}
