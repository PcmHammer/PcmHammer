using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P12 : ECUBase {
        public P12() {
            Manufacturer = "GM";
            this.Description = "Atlas I4/I5/I6";
            this.HardwareType = PcmType.P12;
            this.HardwareSlaveCPU = true;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = false;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = true;
            this.IsSupportedWriteBootSector = false;
            this.LoaderRequired = false;
            this.KernelFileName = "Kernel-P12.bin";
            this.KernelBaseAddress = 0xFF2000; // or FF0000? https://pcmhacking.net/forums/viewtopic.php?f=42&t=7742&start=450#p115622
            this.LoaderFileName = string.Empty;
            this.LoaderBaseAddress = 0x0;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 1024 * 1024;
            this.KeyAlgorithm = 91;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12587007, 12597521, 91) },
                { new OSInfo("GM", 12588651, 12597521, 91) },
                { new OSInfo("GM", 12589166, 12597521, 91) },
                { new OSInfo("GM", 12589312, 12597521, 91) },
                { new OSInfo("GM", 12589586, 12597521, 91) },
                { new OSInfo("GM", 12592070, 12597521, 91) },
                { new OSInfo("GM", 12593533, 12597521, 91) },
                { new OSInfo("GM", 12596925, 12597521, 91) },
                { new OSInfo("GM", 12597778, 12597521, 91) },
                { new OSInfo("GM", 12597978, 12597521, 91) },
                { new OSInfo("GM", 12598275, 12597521, 91) },
                { new OSInfo("GM", 12598284, 12597521, 91) },
                { new OSInfo("GM", 12601321, 12597521, 91) },
                { new OSInfo("GM", 12601774, 12597521, 91) },
                { new OSInfo("GM", 12601904, 12597521, 91) },
                { new OSInfo("GM", 12605256, 12597521, 91) },
                { new OSInfo("GM", 12605261, 12597521, 91) },
                { new OSInfo("GM", 12610624, 12597521, 91) },
                { new OSInfo("GM", 12610641, 12597521, 91) },
                { new OSInfo("GM", 12610642, 12597521, 91) },
                { new OSInfo("GM", 12610643, 12597521, 91) },
                { new OSInfo("GM", 12610644, 12597521, 91) },
                { new OSInfo("GM", 12610645, 12597521, 91) },
                { new OSInfo("GM", 12623279, 12597521, 91) },
                { new OSInfo("GM", 12627882, 12597521, 91) },
                { new OSInfo("GM", 12627884, 12597521, 91) },
                { new OSInfo("GM", 12631085, 12597521, 91) },
                { new OSInfo("GM", 12604440, 12597521, 91) },
                { new OSInfo("GM", 12606400, 12597521, 91) },
                { new OSInfo("GM", 12606374, 12597521, 91) },
                { new OSInfo("GM", 12606375, 12597521, 91) },
                { new OSInfo("GM", 12627883, 12597521, 91) },
                { new OSInfo("GM", 12627885, 12597521, 91) },
            };

        }
    }
}