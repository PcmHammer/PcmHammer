using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P12 : ECUBase {
        public P12() {
            Description = "P12 1Mb (Atlas I4/I5/I6)";
            HardwareType = PcmType.P12;
            HardwareSlaveCPU = true;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = false;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = true;
            IsSupportedWriteBootSector = false;
            LoaderRequired = false;
            KernelFileName = "Kernel-P12.bin";
            KernelBaseAddress = 0xFF2000;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KeyAlgorithm = 91;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // P12 1Mb Service No 12597521
                new OSInfo(12587007, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12588651, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12589166, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12589312, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12589586, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12592070, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12593533, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12596925, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12597778, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12597978, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12598275, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12598284, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12601321, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12601774, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12601904, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12604440, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12605256, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12605261, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12606374, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12606375, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12606400, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610624, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610641, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610642, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610643, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610644, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12610645, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12623279, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12627882, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12627883, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12627884, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12627885, 12597521, "P12 1Mb Service No 12597521", 91),
                new OSInfo(12631085, 12597521, "P12 1Mb Service No 12597521", 91),
                // P12 2Mb Service No 12569773
                new OSInfo(12609805, 12569773, "P12b (2Mb) Service No 12569773", 91),
                new OSInfo(12611642, 12569773, "P12b (2Mb) Service No 12569773", 91),
                new OSInfo(12613422, 12569773, "P12b (2Mb) Service No 12569773", 91),
                new OSInfo(12618164, 12569773, "P12b (2Mb) Service No 12569773", 91),
                // Service number unknown
                new OSInfo(5534509, 0, "P12", 91),
                new OSInfo(12587080, 0, "P12", 91),
                new OSInfo(12589734, 0, "P12", 91),
                new OSInfo(12590492, 0, "P12", 91),
                new OSInfo(12590652, 0, "P12", 91),
                new OSInfo(12591649, 0, "P12", 91),
                new OSInfo(12594432, 0, "P12", 91),
                new OSInfo(12594447, 0, "P12", 91),
                new OSInfo(12596301, 0, "P12", 91),
                new OSInfo(12596785, 0, "P12", 91),
                new OSInfo(12597424, 0, "P12", 91),
                new OSInfo(12597635, 0, "P12", 91),
                new OSInfo(12597756, 0, "P12", 91),
                new OSInfo(12598555, 0, "P12", 91),
                new OSInfo(12598559, 0, "P12", 91),
                new OSInfo(12600819, 0, "P12", 91),
                new OSInfo(12606370, 0, "P12", 91),
                new OSInfo(12607148, 0, "P12", 91),
                new OSInfo(12610647, 0, "P12", 91),
                new OSInfo(12610648, 0, "P12", 91),
                new OSInfo(12623277, 0, "P12", 91),
                new OSInfo(12623278, 0, "P12", 91),
                new OSInfo(19171603, 0, "P12", 91),
            };
        }
    }
}
