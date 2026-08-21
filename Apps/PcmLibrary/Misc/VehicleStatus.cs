// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// What a connection check found: the state the PCM is in, and - when it is running normally -
    /// the bus it answered on with the properties read from it. Returned by Vehicle.QueryStatus for
    /// every UI to present in its own way.
    /// </summary>
    public class VehicleStatus
    {
        /// <summary>The states a connection check can find the PCM in.</summary>
        public enum State
        {
            /// <summary>Running its operating system; Bus, Osid and Voltage are populated.</summary>
            OperatingSystem,

            /// <summary>Broadcasting a recovery request, so only a write can talk to it.</summary>
            Recovery,

            /// <summary>Running a kernel from an earlier operation; see KernelVersion.</summary>
            Kernel,
        }

        /// <summary>What the PCM is doing.</summary>
        public State PcmState { get; }

        /// <summary>The bus the PCM answered on.</summary>
        public BusProtocol Bus { get; }

        /// <summary>The operating system id, or zero when the PCM is in recovery or running a kernel.</summary>
        public uint Osid { get; }

        /// <summary>The kernel version, or zero when no kernel is running.</summary>
        public ulong KernelVersion { get; }

        /// <summary>Battery voltage, or empty when the bus in use has no voltage query.</summary>
        public string Voltage { get; }

        private VehicleStatus(State pcmState, BusProtocol bus, uint osid, ulong kernelVersion, string voltage)
        {
            this.PcmState = pcmState;
            this.Bus = bus;
            this.Osid = osid;
            this.KernelVersion = kernelVersion;
            this.Voltage = voltage;
        }

        public static VehicleStatus OperatingSystem(BusProtocol bus, uint osid, string voltage)
        {
            return new VehicleStatus(State.OperatingSystem, bus, osid, 0, voltage);
        }

        public static VehicleStatus Recovery()
        {
            return new VehicleStatus(State.Recovery, BusProtocol.Vpw, 0, 0, string.Empty);
        }

        public static VehicleStatus Kernel(ulong kernelVersion)
        {
            return new VehicleStatus(State.Kernel, BusProtocol.Vpw, 0, kernelVersion, string.Empty);
        }
    }
}
