// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Points a device at a different CAN target (ids and ISO-TP addressing) for the duration of a
    /// block, restoring the previous target on dispose - including after a failure, so a functional
    /// broadcast can never leak into the physical exchanges that follow it.
    /// </summary>
    public sealed class CanTargetScope : IDisposable
    {
        private readonly ICanTarget? target;
        private readonly uint txCanId;
        private readonly uint rxCanId;
        private readonly IsoTpAddressing txAddressing;
        private readonly IsoTpAddressing rxAddressing;

        /// <summary>A scope that changes nothing, for devices that are not CAN-capable.</summary>
        public static readonly CanTargetScope None = new CanTargetScope();

        private CanTargetScope()
        {
        }

        private CanTargetScope(ICanTarget target)
        {
            this.target = target;
            this.txCanId = target.TxCanId;
            this.rxCanId = target.RxCanId;
            this.txAddressing = target.TxAddressing;
            this.rxAddressing = target.RxAddressing;
        }

        /// <summary>
        /// Retarget transmissions to <paramref name="txCanId"/> with the given addressing, leaving
        /// the response id alone. A null target yields an inert scope.
        /// </summary>
        public static CanTargetScope Retarget(ICanTarget? target, uint txCanId, IsoTpAddressing txAddressing = default)
        {
            if (target == null)
            {
                return None;
            }

            CanTargetScope scope = new CanTargetScope(target);
            target.TxCanId = txCanId;
            target.TxAddressing = txAddressing;
            return scope;
        }

        public void Dispose()
        {
            if (this.target == null)
            {
                return;
            }

            this.target.TxCanId = this.txCanId;
            this.target.RxCanId = this.rxCanId;
            this.target.TxAddressing = this.txAddressing;
            this.target.RxAddressing = this.rxAddressing;
        }
    }
}
