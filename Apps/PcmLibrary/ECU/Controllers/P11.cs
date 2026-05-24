using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P11 : ECUBase {
        public P11() {
            Description = "P11";
            HardwareType = PcmType.P11;
            IsUnderDevelopment = true;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = true;
            IsSupportedWriteBootSector = true;
            LoaderRequired = false;
            KernelFileName = "Kernel-P11.bin";
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
                // Service number unknown
                new OSInfo(10384528, 0, "P11", 0x0D),
                new OSInfo(12215090, 0, "P11", 0x0D),
                new OSInfo(12215092, 0, "P11", 0x0D),
                new OSInfo(12217996, 0, "P11", 0x0D),
                new OSInfo(12218876, 0, "P11", 0x0D),
                new OSInfo(12218880, 0, "P11", 0x0D),
                new OSInfo(12218881, 0, "P11", 0x0D),
                new OSInfo(12218882, 0, "P11", 0x0D),
                new OSInfo(12218890, 0, "P11", 0x0D),
                new OSInfo(12218892, 0, "P11", 0x0D),
                new OSInfo(12219185, 0, "P11", 0x0D),
                new OSInfo(12219186, 0, "P11", 0x0D),
                new OSInfo(12224985, 0, "P11", 0x0D),
                new OSInfo(12226745, 0, "P11", 0x0D),
                new OSInfo(12229535, 0, "P11", 0x0D),
                new OSInfo(12235939, 0, "P11", 0x0D),
                new OSInfo(12243363, 0, "P11", 0x0D),
                new OSInfo(12571654, 0, "P11", 0x0D),
                new OSInfo(12571657, 0, "P11", 0x0D),
                new OSInfo(12578583, 0, "P11", 0x0D),
                new OSInfo(12578585, 0, "P11", 0x0D),
                new OSInfo(12579663, 0, "P11", 0x0D),
                new OSInfo(12579665, 0, "P11", 0x0D),
                new OSInfo(12579668, 0, "P11", 0x0D),
                new OSInfo(12579673, 0, "P11", 0x0D),
                new OSInfo(12580049, 0, "P11", 0x0D),
                new OSInfo(12580051, 0, "P11", 0x0D),
                new OSInfo(12582997, 0, "P11", 0x0D),
                new OSInfo(12583591, 0, "P11", 0x0D),
                new OSInfo(12583592, 0, "P11", 0x0D),
                new OSInfo(12583753, 0, "P11", 0x0D),
                new OSInfo(12583755, 0, "P11", 0x0D),
                new OSInfo(12583756, 0, "P11", 0x0D),
                new OSInfo(12583758, 0, "P11", 0x0D),
                new OSInfo(12583762, 0, "P11", 0x0D),
                new OSInfo(12583763, 0, "P11", 0x0D),
                new OSInfo(12583780, 0, "P11", 0x0D),
                new OSInfo(12583782, 0, "P11", 0x0D),
                new OSInfo(12583784, 0, "P11", 0x0D),
                new OSInfo(12583785, 0, "P11", 0x0D),
                new OSInfo(12583786, 0, "P11", 0x0D),
                new OSInfo(12584713, 0, "P11", 0x0D),
                new OSInfo(12584715, 0, "P11", 0x0D),
                new OSInfo(12584717, 0, "P11", 0x0D),
                new OSInfo(12584719, 0, "P11", 0x0D),
                new OSInfo(12585894, 0, "P11", 0x0D),
                new OSInfo(12587615, 0, "P11", 0x0D),
                new OSInfo(12587617, 0, "P11", 0x0D),
                new OSInfo(12593509, 0, "P11", 0x0D),
                new OSInfo(12593510, 0, "P11", 0x0D),
                new OSInfo(12593525, 0, "P11", 0x0D),
                new OSInfo(12593527, 0, "P11", 0x0D),
                new OSInfo(12593529, 0, "P11", 0x0D),
                new OSInfo(12594548, 0, "P11", 0x0D),
                new OSInfo(12594550, 0, "P11", 0x0D),
                new OSInfo(12596603, 0, "P11", 0x0D),
                new OSInfo(12596936, 0, "P11", 0x0D),
                new OSInfo(12597689, 0, "P11", 0x0D),
                new OSInfo(12597690, 0, "P11", 0x0D),
                new OSInfo(12598564, 0, "P11", 0x0D),
                new OSInfo(12598565, 0, "P11", 0x0D),
                new OSInfo(12598583, 0, "P11", 0x0D),
                new OSInfo(12598584, 0, "P11", 0x0D),
                new OSInfo(12598585, 0, "P11", 0x0D),
                new OSInfo(93802334, 0, "P11", 0x0D),
                // Service No 12210553
                new OSInfo(12218878, 0, "P11 Service No 12210553", 0x0D),
                new OSInfo(12593523, 0, "P11 Service No 12210553", 0x0D),
                // Service No 12576162
                new OSInfo(12586586, 0, "P11 Service No 12576162", 0x0D),
            };
        }
    }
}
