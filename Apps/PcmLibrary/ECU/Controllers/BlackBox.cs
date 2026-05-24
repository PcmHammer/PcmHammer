using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class BlackBox : ECUBase {
        public BlackBox() {
            Description = "Vortec BlackBox";
            HardwareType = PcmType.BlackBox;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            LoaderRequired = false;
            KernelFileName = "Kernel-BlackBox.bin";
            KernelBaseAddress = 0xFFC300;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 16;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service No 9366810
                new OSInfo(9365095, 9366810, "Vortec Black Box 98/99 Service No 9366810 or 9355699", 16),
                new OSInfo(16263425, 9366810, "Vortec Black Box 98/99 Service No 9366810 or 9355699", 16),
                // Service No 16263494
                new OSInfo(9360505, 16263494, "Vortec Black Box 98-02, 4 Plug, Service No 16263494", 16),
                new OSInfo(9365085, 16263494, "Vortec Black Box 98-02, 4 Plug, Service No 16263494", 16),
                new OSInfo(16265175, 16263494, "Vortec Black Box 98-02, 4 Plug, Service No 16263494", 16),
                // Service number unknown
                new OSInfo(16258745, 0, "BlackBox", 16),
            };
        }
    }
}
