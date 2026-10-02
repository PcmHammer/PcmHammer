// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// A discovered J2534 driver as offered in the device picker: its name (the value the factory and
    /// saved settings key on) and, queried from the driver's registration, which of the app's two
    /// buses it supports. The display name appends those in brackets, matching the serial picker.
    /// </summary>
    public sealed class J2534DeviceDescriptor
    {
        public J2534DeviceDescriptor(J2534DotNet.J2534Device device)
        {
            this.Name = device.Name;
            this.Protocols = SupportedBuses(device);
        }

        /// <summary>Driver name; the key the factory matches and settings persist.</summary>
        public string Name { get; }

        /// <summary>The app's buses the driver advertises (VPW and/or CAN), in picker order.</summary>
        public IReadOnlyList<BusProtocol> Protocols { get; }

        /// <summary>Name plus protocols in brackets, e.g. "OBDX Pro VT (VPW, CAN)".</summary>
        public string DisplayName =>
            this.Protocols.Count > 0 ? this.Name + " " + DeviceCatalog.FormatProtocols(this.Protocols) : this.Name;

        public override string ToString() => this.DisplayName;

        /// <summary>
        /// The buses (of the two the app uses) a J2534 driver advertises: VPW via its J1850VPW channel,
        /// CAN via its ISO15765 channel (the mode these PCMs are talked to in). Every other J2534
        /// protocol is ignored. This is the single source the picker label and the J2534Device runtime
        /// gate share.
        /// </summary>
        public static IReadOnlyList<BusProtocol> SupportedBuses(J2534DotNet.J2534Device device)
        {
            List<BusProtocol> buses = new List<BusProtocol>();
            if (device.IsJ1850VPWSupported)
            {
                buses.Add(BusProtocol.VPW);
            }
            if (device.IsISO15765Supported)
            {
                buses.Add(BusProtocol.Can500k);
            }
            return buses;
        }
    }
}
