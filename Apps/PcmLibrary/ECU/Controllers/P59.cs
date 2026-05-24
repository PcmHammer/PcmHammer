using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P59 : ECUBase {
        public P59() {
            Description = "P59 1MiB";
            HardwareType = PcmType.P59;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = true;
            LoaderRequired = false;
            KernelFileName = "Kernel-P01.bin";
            KernelBaseAddress = 0xFF8000;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KeyAlgorithm = 40;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // P59 EFI Live COS (SN 12200411)
                new OSInfo(3150003, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(3190003, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4073003, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4073103, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4110001, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4110003, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4140003, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(4140004, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(5120002, 12200411, "P59 EFI Live COS", 40),
                new OSInfo(5120003, 12200411, "P59 EFI Live COS", 40),
                // P59a Hybrid Service No 12583560
                new OSInfo(12590777, 12583560, "P59a Hybrid Service No 12583560", 40),
                // P59 Service No 12589463
                new OSInfo(12591725, 12589463, "P59 Service No 12589463", 40),
                new OSInfo(12592618, 12589463, "P59 Service No 12589463", 40),
                new OSInfo(12593555, 12589463, "P59 Service No 12589463", 40),
                new OSInfo(12606961, 12589463, "P59 Service No 12589463", 40),
                new OSInfo(12612115, 12589463, "P59 Service No 12589463", 40),
                // P59 Service No 12586242
                new OSInfo(12564440, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12585950, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12588804, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12592425, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12592433, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12606960, 12589463, "P59 Service No 12586242", 40),
                new OSInfo(12612114, 12589463, "P59 Service No 12586242", 40),
                // P59 Service No 12586243
                new OSInfo(12587603, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(12587604, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030003, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030004, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030005, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030006, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030007, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030008, 12589463, "P59 Service No 12586243", 40),
                new OSInfo(76030009, 12589463, "P59 Service No 12586243", 40),
                // P59 Service No 12582605
                new OSInfo(12578128, 12582605, "P59 Service No 12582605", 40),
                new OSInfo(12579405, 12582605, "P59 Service No 12582605", 40),
                new OSInfo(12580055, 12582605, "P59 Service No 12582605", 40),
                new OSInfo(12593058, 12582605, "P59 Service No 12582605", 40),
                // P59 Service No 12582811
                new OSInfo(12587811, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12605114, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12606807, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12608669, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12613245, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12613246, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12613247, 12582811, "P59 Service No 12582811", 40),
                new OSInfo(12619623, 12582811, "P59 Service No 12582811", 40),
                // P59 Service No 12602802
                new OSInfo(12597120, 12602802, "P59 Service No 12602802", 40),
                new OSInfo(12613248, 12602802, "P59 Service No 12602802", 40),
                new OSInfo(12619624, 12602802, "P59 Service No 12602802", 40),
                // P59 service number unknown
                new OSInfo(12568640, 0, "P59", 40),
                new OSInfo(12584941, 0, "P59", 40),
                new OSInfo(12591347, 0, "P59", 40),
                new OSInfo(12592122, 0, "P59", 40),
                new OSInfo(12594368, 0, "P59", 40),
                new OSInfo(12604949, 0, "P59", 40),
            };
        }

        // VCM Suite COS V2/V3 P59 pattern: 7-digit, starts "12", position 4='0', position 3='2' or '3', position 5='5'
        public override bool ECUSupportsOSID(uint osid) {
            if (base.ECUSupportsOSID(osid)) return true;
            string s = osid.ToString();
            return s.Length == 7 && s.Substring(0, 2) == "12" && s[4] == '0' && (s[3] == '2' || s[3] == '3') && s[5] == '5';
        }
    }
}
