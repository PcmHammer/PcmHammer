// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// What a boot-loader write will actually program: which master block groups and whether the slave.
    /// Built from a CRC comparison the read kernel has already run over the flash, so only the parts that
    /// differ from the file are written. Boot-loader PCMs (E92, E39, ...) program a whole block group at
    /// a time - the calibration group shares erase sectors, so it is written as one - which is why the
    /// plan is per group rather than per sector.
    /// </summary>
    public sealed class BootLoaderWritePlan
    {
        /// <summary>Master block groups to program (OperatingSystem and/or Calibration).</summary>
        public BlockType MasterBlocks { get; }

        /// <summary>Whether the slave CPU's modules are to be programmed.</summary>
        public bool WriteSlave { get; }

        public BootLoaderWritePlan(BlockType masterBlocks, bool writeSlave)
        {
            this.MasterBlocks = masterBlocks;
            this.WriteSlave = writeSlave;
        }

        /// <summary>True when nothing differs, so there is nothing to write.</summary>
        public bool NothingToWrite => this.MasterBlocks == BlockType.Invalid && !this.WriteSlave;

        /// <summary>
        /// A plan that programs every requested master group (and the slave when in scope) with no CRC
        /// comparison - the forced write. It writes even unchanged sectors, so it does not need the read
        /// kernel or a reboot: the write runs cold from the stock OS.
        /// </summary>
        public static BootLoaderWritePlan Forced(BlockType requestedBlocks, bool slaveInScope)
        {
            BlockType master = requestedBlocks & (BlockType.OperatingSystem | BlockType.Calibration);
            return new BootLoaderWritePlan(master, slaveInScope);
        }

        // The master groups a boot-loader write can touch, in the order the boot loader streams them:
        // operating system first (it arms the slave), then calibration. Boot is protected and never here.
        private static readonly BlockType[] MasterGroups = { BlockType.OperatingSystem, BlockType.Calibration };

        /// <summary>
        /// Build the plan from ranges the read kernel has CRC-compared (each range's <see cref="MemoryRange.ActualCrc"/>
        /// and <see cref="MemoryRange.DesiredCrc"/> already set). A master group is written when it is in
        /// <paramref name="requestedBlocks"/> and any of its ranges differs; with <paramref name="forceAll"/>
        /// every requested group is written regardless. The slave cannot be CRC-checked (its flash is not
        /// readable), so <paramref name="slaveDiffers"/> is decided from its module ids by the caller.
        /// </summary>
        public static BootLoaderWritePlan FromComparison(
            IEnumerable<MemoryRange> ranges,
            BlockType requestedBlocks,
            bool forceAll,
            bool slaveInScope,
            bool slaveDiffers)
        {
            if (ranges == null)
            {
                throw new ArgumentNullException(nameof(ranges));
            }

            var rangeList = new List<MemoryRange>(ranges);
            BlockType master = BlockType.Invalid;

            foreach (BlockType group in MasterGroups)
            {
                if ((requestedBlocks & group) == 0)
                {
                    continue;
                }

                bool anyInScope = false;
                bool anyDiffers = false;
                foreach (MemoryRange range in rangeList)
                {
                    if ((range.Type & group) == 0)
                    {
                        continue;
                    }

                    anyInScope = true;
                    if (range.ActualCrc != range.DesiredCrc)
                    {
                        anyDiffers = true;
                    }
                }

                if (anyInScope && (forceAll || anyDiffers))
                {
                    master |= group;
                }
            }

            bool writeSlave = slaveInScope && (forceAll || slaveDiffers);
            if (writeSlave)
            {
                // Arming the slave requires the full master programmed first, so include every
                // requested master group even where it is unchanged.
                master = requestedBlocks & (BlockType.OperatingSystem | BlockType.Calibration);
            }

            return new BootLoaderWritePlan(master, writeSlave);
        }

        /// <summary>One line naming what will be written, for the log.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if ((this.MasterBlocks & BlockType.OperatingSystem) != 0)
            {
                parts.Add("operating system");
            }

            if ((this.MasterBlocks & BlockType.Calibration) != 0)
            {
                parts.Add("calibration");
            }

            if (this.WriteSlave)
            {
                parts.Add("slave CPU");
            }

            return parts.Count == 0 ? "nothing (already up to date)" : string.Join(" + ", parts);
        }
    }
}
