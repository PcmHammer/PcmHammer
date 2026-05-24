using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P08 : ECUBase {
        public P08() {
            Description = "P08 512KiB i4";
            HardwareType = PcmType.P08;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = false;
            LoaderRequired = false;
            KernelFileName = "Kernel-P08.bin";
            KernelBaseAddress = 0xFFAC00;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 13;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service No 9356249
                new OSInfo(9364970, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(9382954, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(9384480, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(9387226, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12202774, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12205537, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12205561, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12206029, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12206030, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12206037, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12206044, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12208154, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12208156, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12208767, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12208773, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12216187, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12216489, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12216571, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12221087, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12221096, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12221111, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12222128, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12222134, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12222135, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12222446, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12225345, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(12225346, 9356249, "P08 Service No 9356249", 13),
                new OSInfo(16257436, 9356249, "P08 Service No 9356249", 13),
                // Service No 12202203
                new OSInfo(9392792, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(9392795, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(9392796, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12201233, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12205552, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12223041, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12223044, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12223046, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12225338, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12225340, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12571886, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12580027, 12202203, "P08 Service No 12202203", 13),
                new OSInfo(12580029, 12202203, "P08 Service No 12202203", 13),
                // Service No 16228016
                new OSInfo(9351310, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9353727, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364359, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364362, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364367, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364370, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364966, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364974, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9364976, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9365280, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9365284, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9365286, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9365312, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9365338, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9374332, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9374334, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9383061, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9383064, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9383067, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9383076, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(9387885, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(12222105, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16254764, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16259649, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16259657, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16259674, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16259706, 16228016, "P08 Service No 16228016", 13),
                new OSInfo(16259718, 16228016, "P08 Service No 16228016", 13),
                // Service number unknown
                new OSInfo(9351290, 0, "P08", 13),
                new OSInfo(9351297, 0, "P08", 13),
                new OSInfo(9351321, 0, "P08", 13),
                new OSInfo(9353418, 0, "P08", 13),
                new OSInfo(9353422, 0, "P08", 13),
                new OSInfo(9353732, 0, "P08", 13),
                new OSInfo(9353738, 0, "P08", 13),
                new OSInfo(9354904, 0, "P08", 13),
                new OSInfo(9354907, 0, "P08", 13),
                new OSInfo(9354910, 0, "P08", 13),
                new OSInfo(9354914, 0, "P08", 13),
                new OSInfo(9355300, 0, "P08", 13),
                new OSInfo(9355302, 0, "P08", 13),
                new OSInfo(9356822, 0, "P08", 13),
                new OSInfo(9364356, 0, "P08", 13),
                new OSInfo(9365276, 0, "P08", 13),
                new OSInfo(9365292, 0, "P08", 13),
                new OSInfo(9365308, 0, "P08", 13),
                new OSInfo(9365310, 0, "P08", 13),
                new OSInfo(9365324, 0, "P08", 13),
                new OSInfo(9367522, 0, "P08", 13),
                new OSInfo(9368451, 0, "P08", 13),
                new OSInfo(9368547, 0, "P08", 13),
                new OSInfo(9372478, 0, "P08", 13),
                new OSInfo(9373177, 0, "P08", 13),
                new OSInfo(9374787, 0, "P08", 13),
                new OSInfo(9382927, 0, "P08", 13),
                new OSInfo(9382928, 0, "P08", 13),
                new OSInfo(9382941, 0, "P08", 13),
                new OSInfo(9382948, 0, "P08", 13),
                new OSInfo(9382957, 0, "P08", 13),
                new OSInfo(9383079, 0, "P08", 13),
                new OSInfo(9383089, 0, "P08", 13),
                new OSInfo(9387205, 0, "P08", 13),
                new OSInfo(9387214, 0, "P08", 13),
                new OSInfo(9387229, 0, "P08", 13),
                new OSInfo(9387552, 0, "P08", 13),
                new OSInfo(12201228, 0, "P08", 13),
                new OSInfo(12201231, 0, "P08", 13),
                new OSInfo(12201238, 0, "P08", 13),
                new OSInfo(12205538, 0, "P08", 13),
                new OSInfo(12205545, 0, "P08", 13),
                new OSInfo(12206019, 0, "P08", 13),
                new OSInfo(12206025, 0, "P08", 13),
                new OSInfo(12206042, 0, "P08", 13),
                new OSInfo(12206045, 0, "P08", 13),
                new OSInfo(12206049, 0, "P08", 13),
                new OSInfo(12208527, 0, "P08", 13),
                new OSInfo(12208535, 0, "P08", 13),
                new OSInfo(12216490, 0, "P08", 13),
                new OSInfo(12216567, 0, "P08", 13),
                new OSInfo(12216568, 0, "P08", 13),
                new OSInfo(12217195, 0, "P08", 13),
                new OSInfo(12221098, 0, "P08", 13),
                new OSInfo(12222110, 0, "P08", 13),
                new OSInfo(12222131, 0, "P08", 13),
                new OSInfo(12223050, 0, "P08", 13),
                new OSInfo(12225336, 0, "P08", 13),
                new OSInfo(12571890, 0, "P08", 13),
                new OSInfo(12578485, 0, "P08", 13),
                new OSInfo(12580025, 0, "P08", 13),
                new OSInfo(12583655, 0, "P08", 13),
                new OSInfo(16253027, 0, "P08", 13),
                new OSInfo(16267097, 0, "P08", 13),
                new OSInfo(16267114, 0, "P08", 13),
            };
        }
    }
}
