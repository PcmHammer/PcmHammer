// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// A module found by a bus scan: which target it is, the bus it answered on, and the operating
    /// system id it reported. Returned by Vehicle.DetectModules for the CLI and GUI to present and
    /// to pick which module to work with.
    /// </summary>
    public class DetectedModule
    {
        /// <summary>The bus the module answered on.</summary>
        public BusProtocol Bus { get; }

        /// <summary>The target (module address) it answered as.</summary>
        public Target Target { get; }

        /// <summary>The operating system id the module reported.</summary>
        public uint Osid { get; }

        public DetectedModule(BusProtocol bus, Target target, uint osid)
        {
            this.Bus = bus;
            this.Target = target;
            this.Osid = osid;
        }
    }
}
