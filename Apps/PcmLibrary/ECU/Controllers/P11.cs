using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers 
{
    public class P11 : ECUBase
    {
        public P11() {
            Manufacturer = "GM";
            Description = "P11";
            HardwareType = PcmType.P11;
            BaseHardwareType = HardwareType;
            IsUnderDevelopment = true;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = true;
            IsSupportedWriteBootSector = true;
            LoaderRequired = false;
            KernelBaseAddress = 0xFFC000;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 0x0D;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                new("GM", 10384528, 0, 0x0D),
                new("GM", 12215090, 0, 0x0D),
                new("GM", 12215092, 0, 0x0D),
                new("GM", 12217996, 0, 0x0D),
                new("GM", 12218876, 0, 0x0D),
                new("GM", 12218880, 0, 0x0D),
                new("GM", 12218881, 0, 0x0D),
                new("GM", 12218882, 0, 0x0D),
                new("GM", 12218890, 0, 0x0D),
                new("GM", 12218892, 0, 0x0D),
                new("GM", 12219185, 0, 0x0D),
                new("GM", 12219186, 0, 0x0D),
                new("GM", 12224985, 0, 0x0D),
                new("GM", 12226745, 0, 0x0D),
                new("GM", 12229535, 0, 0x0D),
                new("GM", 12235939, 0, 0x0D),
                new("GM", 12243363, 0, 0x0D),
                new("GM", 12571654, 0, 0x0D),
                new("GM", 12571657, 0, 0x0D),
                new("GM", 12578583, 0, 0x0D),
                new("GM", 12578585, 0, 0x0D),
                new("GM", 12579663, 0, 0x0D),
                new("GM", 12579665, 0, 0x0D),
                new("GM", 12579668, 0, 0x0D),
                new("GM", 12579673, 0, 0x0D),
                new("GM", 12580049, 0, 0x0D),
                new("GM", 12580051, 0, 0x0D),
                new("GM", 12582997, 0, 0x0D),
                new("GM", 12583591, 0, 0x0D),
                new("GM", 12583592, 0, 0x0D),
                new("GM", 12583753, 0, 0x0D),
                new("GM", 12583755, 0, 0x0D),
                new("GM", 12583756, 0, 0x0D),
                new("GM", 12583758, 0, 0x0D),
                new("GM", 12583762, 0, 0x0D),
                new("GM", 12583763, 0, 0x0D),
                new("GM", 12583780, 0, 0x0D),
                new("GM", 12583782, 0, 0x0D),
                new("GM", 12583784, 0, 0x0D),
                new("GM", 12583785, 0, 0x0D),
                new("GM", 12583786, 0, 0x0D),
                new("GM", 12584713, 0, 0x0D),
                new("GM", 12584715, 0, 0x0D),
                new("GM", 12584717, 0, 0x0D),
                new("GM", 12584719, 0, 0x0D),
                new("GM", 12585894, 0, 0x0D),
                new("GM", 12587615, 0, 0x0D),
                new("GM", 12587617, 0, 0x0D),
                new("GM", 12593509, 0, 0x0D),
                new("GM", 12593510, 0, 0x0D),
                new("GM", 12593525, 0, 0x0D),
                new("GM", 12593527, 0, 0x0D),
                new("GM", 12593529, 0, 0x0D),
                new("GM", 12594548, 0, 0x0D),
                new("GM", 12594550, 0, 0x0D),
                new("GM", 12596603, 0, 0x0D),
                new("GM", 12596936, 0, 0x0D),
                new("GM", 12597689, 0, 0x0D),
                new("GM", 12597690, 0, 0x0D),
                new("GM", 12598564, 0, 0x0D),
                new("GM", 12598565, 0, 0x0D),
                new("GM", 12598583, 0, 0x0D),
                new("GM", 12598584, 0, 0x0D),
                new("GM", 12598585, 0, 0x0D),
                new("GM", 93802334, 0, 0x0D),
                // Service No 12210553
                new("GM", 12218878, 0, 0x0D),
                new("GM", 12593523, 0, 0x0D),
                // Service No 12576162
                new("GM", 12586586, 0, 0x0D),
                new("GM", 12218878, 12210553, 0x0D),
                new("GM", 12593523, 12210553, 0x0D),

                new("GM", 12586586, 12576162, 0x0D),
            };
        }

        public P11(P11 original)
        {
            BaseHardwareType = original.BaseHardwareType;
            ChecksumSupport = original.ChecksumSupport;
            Description = original.Description;
            FlashCRCSupport = original.FlashCRCSupport;
            FlashIDSupport = original.FlashIDSupport;
            HardwareSlaveCPU = original.HardwareSlaveCPU;
            HardwareType = original.HardwareType;
            HardwareTypeOverridden = original.HardwareTypeOverridden;
            ImageBaseAddress = original.ImageBaseAddress;
            ImageSize = original.ImageSize;
            IsSupported = original.IsSupported;
            IsSupportedRead = original.IsSupportedRead;
            IsSupportedWrite = original.IsSupportedWrite;
            IsSupportedWriteBootSector = original.IsSupportedWriteBootSector;
            IsSupportedWriteBySegment = original.IsSupportedWriteBySegment;
            IsSupportedWriteSlaveCPU = original.IsSupportedWriteSlaveCPU;
            IsUnderDevelopment = original.IsUnderDevelopment;
            KernelBaseAddress = original.KernelBaseAddress;
            KernelMaxBlockSize = original.KernelMaxBlockSize;
            KernelVersionSupport = original.KernelVersionSupport;
            KeyAlgorithm = original.KeyAlgorithm;
            KnownOperatingSystems = original.KnownOperatingSystems;
            LoaderBaseAddress = original.LoaderBaseAddress;
            LoaderRequired = original.LoaderRequired;
            Manufacturer = original.Manufacturer;
        }

        public override ECUBase Clone()
        {
            return new P11(this);
        }
    }
}