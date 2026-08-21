// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// Everything a "read properties" / "identify" request found about the connected PCM: which bus
    /// it answered on, its operating system and description, and each property either as a value, as
    /// not-applicable, or as unavailable. Produced by Vehicle.ReadIdentity so every UI shares one
    /// implementation of the VPW and CAN identification flows and just displays the result.
    /// </summary>
    public class PcmIdentity
    {
        /// <summary>Shown for a property the PCM's type does not provide.</summary>
        public const string NotApplicable = "Not Applicable";

        /// <summary>Shown for a property that applies but did not answer.</summary>
        public const string Unavailable = "Unavailable";

        /// <summary>The bus the PCM answered on.</summary>
        public BusProtocol Bus { get; }

        /// <summary>The operating system id, or zero if it could not be read.</summary>
        public uint Osid { get; }

        /// <summary>The OS description, or <see cref="Unavailable"/>.</summary>
        public string Description { get; }

        public string Vin { get; }
        public string CalibrationId { get; }
        public string HardwareId { get; }
        public string SerialNumber { get; }
        public string BroadcastCode { get; }
        public string Mec { get; }
        public string Voltage { get; }

        /// <summary>The software module identifiers a CAN PCM reports; empty for a VPW PCM.</summary>
        public IReadOnlyList<CanIdentification.Item> SoftwareModules { get; }

        /// <summary>
        /// The whole result formatted for a log, in display order, starting with the bus it was
        /// found on. A UI can show its own fields and still log this verbatim.
        /// </summary>
        public IReadOnlyList<string> Lines { get; }

        public PcmIdentity(
            BusProtocol bus,
            uint osid,
            string description,
            string vin,
            string calibrationId,
            string hardwareId,
            string serialNumber,
            string broadcastCode,
            string mec,
            string voltage,
            IReadOnlyList<CanIdentification.Item> softwareModules,
            IReadOnlyList<string> lines)
        {
            this.Bus = bus;
            this.Osid = osid;
            this.Description = description;
            this.Vin = vin;
            this.CalibrationId = calibrationId;
            this.HardwareId = hardwareId;
            this.SerialNumber = serialNumber;
            this.BroadcastCode = broadcastCode;
            this.Mec = mec;
            this.Voltage = voltage;
            this.SoftwareModules = softwareModules;
            this.Lines = lines;
        }
    }
}
