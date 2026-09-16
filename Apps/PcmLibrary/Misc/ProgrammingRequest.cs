// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// A PCM asking to be programmed, and the bus it asked on. Finding one is also how recovery
    /// settles which bus to work over: a PCM that is asking is reachable where it asked.
    /// </summary>
    public sealed class ProgrammingRequest
    {
        public ProgrammingRequest(BusProtocol bus, byte state)
        {
            this.Bus = bus;
            this.State = state;
        }

        /// <summary>The bus the request was heard on.</summary>
        public BusProtocol Bus { get; }

        /// <summary>
        /// What the PCM says about its programmed state. See
        /// <see cref="RecoveryMode.DescribeProgrammedState"/> for what the values mean, and for why
        /// zero does not mean what the standard says it does.
        /// </summary>
        public byte State { get; }
    }
}
