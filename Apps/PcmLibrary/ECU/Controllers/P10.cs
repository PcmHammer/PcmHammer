using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers
{
    public class P10 : ECUBase
    {
        public P10()
        {
            Manufacturer = "GM";
            this.Description = "P10 1Mb";
            this.HardwareType = PcmType.P10;
            BaseHardwareType = HardwareType;
            this.HardwareSlaveCPU = true;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = true;
            this.LoaderRequired = false;
            this.KernelBaseAddress = 0xFFB800;
            this.LoaderBaseAddress = 0x0;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 512 * 1024;
            this.KeyAlgorithm = 66;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12213305, 12576463, 66) },
                { new OSInfo("GM", 12571911, 12576463, 66) },
                { new OSInfo("GM", 12575262, 12576463, 66) },
                { new OSInfo("GM", 12579238, 12576463, 66) },
                { new OSInfo("GM", 12587430, 12576463, 66) },

                { new OSInfo("GM", 12577956, 12574976, 66) },
                { new OSInfo("GM", 12579357, 12574976, 66) },
                { new OSInfo("GM", 12584138, 12574976, 66) },
                { new OSInfo("GM", 12584594, 12574976, 66) },
                { new OSInfo("GM", 12587608, 12574976, 66) },
                { new OSInfo("GM", 12588012, 12574976, 66) },
                { new OSInfo("GM", 12589825, 12574976, 66) },
                { new OSInfo("GM", 12590965, 12574976, 66) },
                { new OSInfo("GM", 12595726, 12574976, 66) },
                { new OSInfo("GM", 12597031, 12574976, 66) },
                { new OSInfo("GM", 12623317, 12574976, 66) }
            };
        }
    }
}