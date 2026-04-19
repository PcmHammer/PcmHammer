using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class BlackBox : ECUBase
    {
        public BlackBox() {
            Manufacturer = "GM";
            this.Description = "GenII Vortec";
            this.HardwareType = PcmType.BlackBox;
            BaseHardwareType = HardwareType;
            this.HardwareSlaveCPU = false;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = false;
            this.LoaderRequired = false;
            this.KernelBaseAddress = 0xFFC300;
            this.LoaderBaseAddress = 0x0;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 512 * 1024;
            this.KeyAlgorithm = 16;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 9355699, 9366810, 16) },
                { new OSInfo("GM", 9365095, 9366810, 16) },
                { new OSInfo("GM", 16263425, 9366810, 16) },

                { new OSInfo("GM", 9360505, 16263494, 16) },
                { new OSInfo("GM", 9365085, 16263494, 16) },
                { new OSInfo("GM", 9384185, 16263494, 16) },
                { new OSInfo("GM", 16251315, 16263494, 16) },
                { new OSInfo("GM", 16265175, 16263494, 16) },
            };

        }
    }
}