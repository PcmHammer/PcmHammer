// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// Tracks which interfaces this process currently has open, so one piece of hardware is never
    /// handed to two devices at once.
    /// </summary>
    /// <remarks>
    /// The UI refuses a duplicate before it gets this far, which is the better experience. This is
    /// the guarantee behind it: a saved setting, the CLI, or a front end that forgets to check would
    /// otherwise walk straight into it, and the consequences are not graceful. Two serial devices on
    /// one port fail with an access denial; two J2534 devices on one driver are worse, because the
    /// second one's teardown can free the library while the first is still using it.
    ///
    /// Claims are process-wide because the conflict is: the hardware does not care which object
    /// opened it.
    /// </remarks>
    public static class DeviceRegistry
    {
        private static readonly object Sync = new object();

        private static readonly List<DeviceSelection> InUse = new List<DeviceSelection>();

        /// <summary>
        /// Take a claim on an interface. Returns null when it is already claimed, in which case
        /// nothing was reserved and the caller must not open it.
        /// </summary>
        public static IDisposable? TryClaim(DeviceSelection? selection)
        {
            if (selection == null || !selection.IsSelected)
            {
                // Nothing to reserve, and nothing to collide with.
                return new Claim(null);
            }

            lock (Sync)
            {
                if (InUse.Any(s => s.Equals(selection)))
                {
                    return null;
                }

                InUse.Add(selection);
                return new Claim(selection);
            }
        }

        /// <summary>Whether an interface is already open in this process.</summary>
        public static bool IsInUse(DeviceSelection? selection)
        {
            if (selection == null || !selection.IsSelected)
            {
                return false;
            }

            lock (Sync)
            {
                return InUse.Any(s => s.Equals(selection));
            }
        }

        private static void Release(DeviceSelection selection)
        {
            lock (Sync)
            {
                InUse.RemoveAll(s => s.Equals(selection));
            }
        }

        /// <summary>Holds a claim for as long as the caller keeps it.</summary>
        private sealed class Claim : IDisposable
        {
            private DeviceSelection? selection;

            public Claim(DeviceSelection? selection)
            {
                this.selection = selection;
            }

            public void Dispose()
            {
                if (this.selection == null)
                {
                    return;
                }

                Release(this.selection);
                this.selection = null;
            }
        }
    }
}
