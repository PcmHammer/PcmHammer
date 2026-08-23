// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PcmHacking
{
    public class FlashChip
    {
        /// <summary>
        /// Flash chip ID discovered by the kernel.
        /// </summary>
        public UInt32 ChipId { get; private set; }

        /// <summary>
        /// Flash chip size (512kb or 1mb).
        /// </summary>
        public UInt32 Size { get; private set; }

        /// <summary>
        /// Memory ranges for erasing and rewriting.
        /// </summary>
        public ICollection<MemoryRange> MemoryRanges { get; private set; }

        /// <summary>
        /// Flash chip description (manufacturer, model, size).
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Constructor. Just stores data, all of the interesting stuff is in the factory method.
        /// </summary>
        protected FlashChip(UInt32 chipId, string description, UInt32 size, ICollection<MemoryRange> memoryRanges)
        {
            this.ChipId = chipId;
            this.Description = description;
            this.Size = size;
            this.MemoryRanges = memoryRanges;
        }

        /// <summary>
        /// Returns the chip description.
        /// </summary>
        public override string ToString()
        {
            return this.Description;
        }

        /// <summary>
        /// Factory method. Selects the memory configuration, size, and description.
        /// </summary>
        public static FlashChip Create(UInt32 chipId, ILogger logger)
        {
            IList<MemoryRange>? memoryRanges = null;
            string description;
            UInt32 size;

            switch (chipId)
            {
                // This is only here as a warning to anyone adding ranges for another chip.
                // Please read the comments carefully. See case 0x00894471 for the real data.
                case 0xFFFF4471:
                    var unused = new MemoryRange[]
                    {
                        // These addresses are for a bottom fill chip (B) in byte mode (not word).
                        // Be careful which notation datasheets are using when adding more devices.
                        new MemoryRange(0x60000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x40000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x20000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration), //  96kb main block 
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    throw new InvalidOperationException("This flash chip ID was not supposed to exist in the wild.");

                // This is not a real chip, its used as a default value, then overwritten with real data if the kernel supprot flashchipid.
                case 0x12345678:
                    size = 512 * 1024;
                    description = "Default 512KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        // Used by CKernelReader to initialise FlashChip default value
                        new MemoryRange(0x60000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x40000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x20000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration), //  96kb main block 
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // Intel 28F200BX
                case 0x00892274:
                case 0x00892275:
                    size = 256 * 1024;
                    description = "Intel 28F200BX, 256KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        // These addresses are for a bottom fill chip (B) in byte mode (not word)
                        new MemoryRange(0x20000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration), //  96kb main block 
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // Intel 28F400B
                case 0x00894471:
                    size = 512 * 1024;
                    description = "Intel 28F400B, 512KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        // These addresses are for a bottom fill chip (B) in byte mode (not word)
                        new MemoryRange(0x60000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x40000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x20000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration), //  96kb main block 
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // Intel 28F800B
                case 0x0089889D:
                    size = 1024 * 1024;
                    description = "Intel 28F800B, 1024KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        // These addresses are for a bottom fill chip (B) in byte mode (not word)
                        new MemoryRange(0xE0000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0xC0000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0xA0000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x80000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x60000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x40000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x20000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration), //  96kb main block
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // Intel 28F800F3
                case 0x008988F2:
                    size = 1024 * 1024;
                    description = "Intel 28F800F3, Fast Boot Block (120ns) 1024KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        // These addresses are for a bottom fill chip (B) in byte mode (not word)
                        new MemoryRange(0xF0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 22
                        new MemoryRange(0xE0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 21
                        new MemoryRange(0xD0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 20
                        new MemoryRange(0xC0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 19
                        new MemoryRange(0xB0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 18
                        new MemoryRange(0xA0000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 17
                        new MemoryRange(0x90000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 16
                        new MemoryRange(0x80000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 15
                        new MemoryRange(0x70000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 14
                        new MemoryRange(0x60000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 13
                        new MemoryRange(0x50000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 12
                        new MemoryRange(0x40000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 11
                        new MemoryRange(0x30000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 10
                        new MemoryRange(0x20000, 0x10000, BlockType.OperatingSystem),   //  32kb operating system block 09
                        new MemoryRange(0x10000, 0x10000, BlockType.Calibration),       //  32kb calibration block 08
                        new MemoryRange(0x0E000, 0x02000, BlockType.Calibration),       //   4kb calibration block 07
                        new MemoryRange(0x0C000, 0x02000, BlockType.Calibration),       //   4kb calibration block 06
                        new MemoryRange(0x0A000, 0x02000, BlockType.Calibration),       //   4kb calibration block 05
                        new MemoryRange(0x08000, 0x02000, BlockType.Calibration),       //   4kb calibration block 04
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter),         //   4kb param block 03
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter),         //   4kb param block 02
                        new MemoryRange(0x02000, 0x02000, BlockType.OperatingSystem),   //   4kb operating system block 01
                        new MemoryRange(0x00000, 0x02000, BlockType.Boot),              //   4kb boot  block 00
                    };
                    break;

                // AM29F400BB
                case 0x000122AB:
                    size = 512 * 1024;
                    description = "AMD AM29F400BB, 512KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        new MemoryRange(0x70000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x60000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x50000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x40000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x30000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x20000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x10000, 0x10000, BlockType.Calibration), //  64kb calibration block
                        new MemoryRange(0x08000, 0x08000, BlockType.Calibration), //  32kb calibration block
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //  8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //  8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // AM29F800BB   
                case 0x00012258:
                    size = 1024 * 1024;
                    description = "AMD AM29F800BB, 1024KiB";
                    memoryRanges = new MemoryRange[]
                    {
                        new MemoryRange(0xF0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0xE0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0xD0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0xC0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0xB0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0xA0000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x90000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x80000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x70000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x60000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x50000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x40000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x30000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x20000, 0x10000, BlockType.OperatingSystem), //  64kb main block
                        new MemoryRange(0x10000, 0x10000, BlockType.Calibration), //  64kb calibration block
                        new MemoryRange(0x08000, 0x08000, BlockType.Calibration), //  32kb calibration block
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter), //  8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter), //  8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot), //  16kb boot block
                    };
                    break;

                // AM29BL802C
                case 0x00012281:
                    size = 1024 * 1024;
                    description = "AMD AM29BL802C, 1024KiB";
                    memoryRanges = new MemoryRange[]
                    {          // Start address, Size in Bytes
                        new MemoryRange(0xC0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x80000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x60000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x40000, 0x20000, BlockType.OperatingSystem), // 128kb main block
                        new MemoryRange(0x20000, 0x20000, BlockType.Calibration),     // 128kb calibration block
                        new MemoryRange(0x08000, 0x18000, BlockType.Calibration),     //  96kb calibration block
                        new MemoryRange(0x06000, 0x02000, BlockType.Parameter),       //   8kb parameter block
                        new MemoryRange(0x04000, 0x02000, BlockType.Parameter),       //   8kb parameter block
                        new MemoryRange(0x00000, 0x04000, BlockType.Boot),            //  16kb boot block
                    };
                    break;

                // AM29BL162C
                case 0x00012203:
                    size = 2048 * 1024;
                    description = "AMD AM29BL162C, 2048KiB";
                    memoryRanges = new MemoryRange[]
                    {           // Start address, Size in Bytes
                        new MemoryRange(0x1C0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x180000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x140000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x100000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange( 0xC0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange( 0x80000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange( 0x40000, 0x40000, BlockType.Calibration),     // 256kb calibration block
                        new MemoryRange( 0x08000, 0x38000, BlockType.Calibration),     // 229kb calibration block
                        new MemoryRange( 0x06000, 0x02000, BlockType.Parameter),       //   8kb parameter block
                        new MemoryRange( 0x04000, 0x02000, BlockType.Parameter),       //   8kb parameter block
                        new MemoryRange( 0x00000, 0x04000, BlockType.Boot),            //  16kb boot block
                    };
                    break;

                // AMD 29BDD160G 2MiB
                case 0x0001007E:
                    size = 2048 * 1024;
                    description = "AMD 29BDD160G, 2048KiB";
                    memoryRanges = new MemoryRange[]
                    {           // Start address, Size in Bytes
                        new MemoryRange(0x1FE000, 0x02000, BlockType.Calibration),     // top: eight 8kb calibration sectors
                        new MemoryRange(0x1FC000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1FA000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1F8000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1F6000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1F4000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1F2000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1F0000, 0x02000, BlockType.Calibration),
                        new MemoryRange(0x1E0000, 0x10000, BlockType.Calibration),     // three 64kb calibration sectors
                        new MemoryRange(0x1D0000, 0x10000, BlockType.Calibration),
                        new MemoryRange(0x1C0000, 0x10000, BlockType.Calibration),
                        new MemoryRange(0x1B0000, 0x10000, BlockType.OperatingSystem), // twenty-seven 64kb operating system sectors
                        new MemoryRange(0x1A0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x190000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x180000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x170000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x160000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x150000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x140000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x130000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x120000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x110000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x100000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0F0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0E0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0D0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0C0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0B0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x0A0000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x090000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x080000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x070000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x060000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x050000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x040000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x030000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x020000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x010000, 0x10000, BlockType.OperatingSystem),
                        new MemoryRange(0x00E000, 0x02000, BlockType.Parameter),       // bottom: two 8kb parameter sectors
                        new MemoryRange(0x00C000, 0x02000, BlockType.Parameter),
                        new MemoryRange(0x00A000, 0x02000, BlockType.OperatingSystem), // five 8kb operating system sectors
                        new MemoryRange(0x008000, 0x02000, BlockType.OperatingSystem),
                        new MemoryRange(0x006000, 0x02000, BlockType.OperatingSystem),
                        new MemoryRange(0x004000, 0x02000, BlockType.OperatingSystem),
                        new MemoryRange(0x002000, 0x02000, BlockType.OperatingSystem),
                        new MemoryRange(0x000000, 0x02000, BlockType.Boot),            // one 8kb boot sector
                    };
                    break;

                // Freescale MPC5674F on-chip flash, 4 MiB. The chip id is the MCU ID register (SIU_MIDR):
                // high half 0x5674 = part number, low half = mask/revision. Unlike the external Intel/AMD
                // parts above (whose datasheets list x16 WORD addresses), this is embedded C90LC flash
                // addressed as BYTES, so the sizes below are byte counts matching the image directly.
                //
                //   Low address space  0x000000..0x040000: 16K x4, 64K x2, 16K x4  -> Boot (protected)
                //   Mid address space  0x040000..0x080000: 128K x2                 -> Calibration
                //   High address space 0x080000..0x400000: 256K x14                -> Calibration, then OS
                //
                // The calibration/OS split falls exactly on erase-sector boundaries and matches the
                // segment map: calibration 0x040000..0x0C0000, operating system 0x0C0000..0x400000.
                case 0x56746020:
                    size = 4096 * 1024;
                    description = "Freescale MPC5674F on-chip flash, 4096KiB";
                    memoryRanges = new MemoryRange[]
                    {           // Start address, Size in bytes
                        // High address space: thirteen 256 KiB operating-system sectors ...
                        new MemoryRange(0x3C0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x380000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x340000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x300000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x2C0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x280000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x240000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x200000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x1C0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x180000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x140000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x100000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        new MemoryRange(0x0C0000, 0x40000, BlockType.OperatingSystem), // 256kb main block
                        // ... then the first 256 KiB high sector is calibration.
                        new MemoryRange(0x080000, 0x40000, BlockType.Calibration),     // 256kb calibration block
                        // Mid address space: two 128 KiB calibration sectors.
                        new MemoryRange(0x060000, 0x20000, BlockType.Calibration),     // 128kb calibration block
                        new MemoryRange(0x040000, 0x20000, BlockType.Calibration),     // 128kb calibration block
                        // Low address space: protected boot (256 KiB): 16K x4, 64K x2, 16K x4.
                        new MemoryRange(0x03C000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x038000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x034000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x030000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x020000, 0x10000, BlockType.Boot),            //  64kb boot block
                        new MemoryRange(0x010000, 0x10000, BlockType.Boot),            //  64kb boot block
                        new MemoryRange(0x00C000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x008000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x004000, 0x04000, BlockType.Boot),            //  16kb boot block
                        new MemoryRange(0x000000, 0x04000, BlockType.Boot),            //  16kb boot block
                    };
                    break;

                // Both of these have eight 8kb blocks at the low end, the rest are
                // 64kb. Not sure if they're actually used in any PCMs though.
                case 0x00898893: // Intel 2F008B3
                case 0x008988C1: // Intel 2F800C3
                default:
                    string manufacturer;

                    switch ((chipId >> 16))
                    {
                        case 0x0001:
                            manufacturer = "AMD";
                            break;

                        case 0x0089:
                            manufacturer = "Intel";
                            break;
                        default:
                            manufacturer = "Unknown";
                            break;
                    }

                    logger.AddUserMessage(
                        "Unsupported flash chip ID " + chipId.ToString("X8") + ". Manufacturer: " + manufacturer +
                        Environment.NewLine +
                        "The flash memory in this PCM is not supported by this version of PCM Hammer." +
                        Environment.NewLine +
                        "Please look for a thread about this at pcmhacking.net, or create one if necessary." +
                        Environment.NewLine +
                        "We do aim to add support for all flash chips eventually.");
                    throw new ApplicationException();
            }

            // Sanity check the memory ranges;
            UInt32 lastStart = UInt32.MaxValue;
            string chipIdString = chipId.ToString("X8");
            for (int index = 0; index < memoryRanges.Count; index++)
            {
                if (index == 0)
                {
                    UInt32 top = memoryRanges[index].Address + memoryRanges[index].Size;
                    if ((top != 256 * 1024) && (top != 512 * 1024) && (top != 1024 * 1024) && (top != 2048 * 1024) && (top != 4096 * 1024))
                    {
                        throw new InvalidOperationException(chipIdString + " - Upper end of memory range must be 256KiB, 512KiB, 1024KiB, 2048KiB or 4096KiB, is " + (top / 1024).ToString() + "KiB");
                    }

                    if (size != top)
                    {
                        throw new InvalidOperationException(chipIdString + " - Size does not match upper memory block.");
                    }
                }

                if (index == memoryRanges.Count - 1)
                {
                    if (memoryRanges[index].Address != 0)
                    {
                        throw new InvalidOperationException(chipIdString + " - Memory ranges must start at zero.");
                    }
                }

                if (lastStart != UInt32.MaxValue)
                {
                    if (lastStart != memoryRanges[index].Address + memoryRanges[index].Size)
                    {
                        throw new InvalidDataException(chipIdString + " - Top of range " + index + " must match base of range above.");
                    }
                }

                lastStart = memoryRanges[index].Address;
            }
            
            return new FlashChip(chipId, description, size, memoryRanges);
        }
    }
}
