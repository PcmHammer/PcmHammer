using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P05b : ECUBase {
        public P05b() {
            Description = "P05b (VPW+CAN)";
            HardwareType = PcmType.P05b;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            IsSupportedWriteBootSector = false;
            LoaderRequired = false;
            KernelFileName = "Kernel-P05.bin";
            KernelBaseAddress = 0xFFC100;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KeyAlgorithm = 0x35;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service number unknown
                new OSInfo(12585292, 0, "P05b", 0x35),
                new OSInfo(12585293, 0, "P05b", 0x35),
                new OSInfo(12585294, 0, "P05b", 0x35),
                new OSInfo(12589239, 0, "P05b", 0x35),
                new OSInfo(12594884, 0, "P05b", 0x35),
                new OSInfo(12596629, 0, "P05b", 0x35),
                new OSInfo(12596630, 0, "P05b", 0x35),
                new OSInfo(12596631, 0, "P05b", 0x35),
                new OSInfo(12596632, 0, "P05b", 0x35),
                new OSInfo(12596633, 0, "P05b", 0x35),
                new OSInfo(12598122, 0, "P05b", 0x35),
                new OSInfo(12598125, 0, "P05b", 0x35),
                new OSInfo(12598324, 0, "P05b", 0x35),
                new OSInfo(12598325, 0, "P05b", 0x35),
                new OSInfo(12599697, 0, "P05b", 0x35),
                new OSInfo(12600156, 0, "P05b", 0x35),
                new OSInfo(12600158, 0, "P05b", 0x35),
                new OSInfo(12600160, 0, "P05b", 0x35),
                new OSInfo(12603346, 0, "P05b", 0x35),
                new OSInfo(12603347, 0, "P05b", 0x35),
                new OSInfo(12603348, 0, "P05b", 0x35),
                new OSInfo(12608097, 0, "P05b", 0x35),
                new OSInfo(12608100, 0, "P05b", 0x35),
                new OSInfo(12612947, 0, "P05b", 0x35),
                new OSInfo(12612950, 0, "P05b", 0x35),
                new OSInfo(12619714, 0, "P05b", 0x35),
                new OSInfo(12619715, 0, "P05b", 0x35),
                new OSInfo(12619722, 0, "P05b", 0x35),
                new OSInfo(12619724, 0, "P05b", 0x35),
                new OSInfo(12626396, 0, "P05b", 0x35),
                new OSInfo(12626398, 0, "P05b", 0x35),
                new OSInfo(12635873, 0, "P05b", 0x35),
                new OSInfo(12635874, 0, "P05b", 0x35),
                new OSInfo(12635875, 0, "P05b", 0x35),
                new OSInfo(12635876, 0, "P05b", 0x35),
                new OSInfo(12635877, 0, "P05b", 0x35),
                new OSInfo(12635878, 0, "P05b", 0x35),
                new OSInfo(12635879, 0, "P05b", 0x35),
            };
        }
    }
}
