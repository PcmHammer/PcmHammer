using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers
{
    public class P05 : ECUBase
    {
        public P05()
        {
            Manufacturer = "GM";
            this.Description = "P05 (VPW)";
            this.HardwareType = PcmType.P05;
            BaseHardwareType = HardwareType;
            this.HardwareSlaveCPU = false;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = false;
            this.IsSupportedWriteBootSector = false;
            this.LoaderRequired = false;
            this.KernelBaseAddress = 0xFFC100;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 1024 * 1024;
            this.KeyAlgorithm = 0x35;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12584057, 12581501, 0x35) },
                { new OSInfo("GM", 12584058, 12581501, 0x35) },
                { new OSInfo("GM", 12588933, 12581501, 0x35) },
                { new OSInfo("GM", 12619740, 12581501, 0x35) },
                { new OSInfo("GM", 12619742, 12581501, 0x35) },

                { new OSInfo("GM", 12597270, 12591279, 0x35) },
                { new OSInfo("GM", 12599697, 12591279, 0x35) },
                { new OSInfo("GM", 12608100, 12591279, 0x35) },
                { new OSInfo("GM", 12612950, 12591279, 0x35) },
                { new OSInfo("GM", 12619714, 12591279, 0x35) },
                { new OSInfo("GM", 12619715, 12591279, 0x35) },

                { new OSInfo("GM", 12603217, 12604963, 0x35) },
            };
        }
    }
}