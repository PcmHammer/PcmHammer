using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P11 : ECUBase
    {
        public P11() {
            Manufacturer = "GM";
            this.Description = "P11";
            this.HardwareType = PcmType.P11;
            BaseHardwareType = HardwareType;
            this.IsUnderDevelopment = true;
            this.HardwareSlaveCPU = false;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = true;
            this.IsSupportedWriteBootSector = true;
            this.LoaderRequired = false;
            this.KernelBaseAddress = 0xFFC000;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 512 * 1024;
            this.KeyAlgorithm = 0x0D;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12218878, 12210553, 0x0D) },
                { new OSInfo("GM", 12593523, 12210553, 0x0D) },

                { new OSInfo("GM", 12586586, 12576162, 0x0D) },
            };

        }
    }
}