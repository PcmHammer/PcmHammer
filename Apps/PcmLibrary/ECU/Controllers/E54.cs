using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class E54 : ECUBase
    {
        public E54() {
            Manufacturer = "GM";
            this.Description = "LB7 Duramax";
            this.HardwareType = PcmType.E54;
            BaseHardwareType = HardwareType;
            this.HardwareSlaveCPU = false;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = true;
            this.IsSupportedWriteBySegment = true;
            this.Description = "E54";
            this.LoaderRequired = false;
            this.KernelBaseAddress = 0xFF9100;
            this.LoaderBaseAddress = 0x0;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 512 * 1024;
            this.KeyAlgorithm = 54;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("EFILive", 01337601, 0, 54) },
                { new OSInfo("EFILive", 01710001, 0, 54) },
                { new OSInfo("EFILive", 01887301, 0, 54) },
                { new OSInfo("EFILive", 02444101, 0, 54) },
                { new OSInfo("EFILive", 02600601, 0, 54) },
                { new OSInfo("EFILive", 02685301, 0, 54) },
                { new OSInfo("EFILive", 03904401, 0, 54) },
                { new OSInfo("EFILive", 01337605, 0, 54) },
                { new OSInfo("EFILive", 01710005, 0, 54) },
                { new OSInfo("EFILive", 01887305, 0, 54) },
                { new OSInfo("EFILive", 02444105, 0, 54) },
                { new OSInfo("EFILive", 02600605, 0, 54) },
                { new OSInfo("EFILive", 02685305, 0, 54) },
                { new OSInfo("EFILive", 03904405, 0, 54) },

                { new OSInfo("GM", 15063376, 9388505, 54) },
                { new OSInfo("GM", 15188873, 9388505, 54) },
                { new OSInfo("GM", 15097100, 9388505, 54) },

                { new OSInfo("GM", 15085499, 12210729, 54) },
                { new OSInfo("GM", 15094441, 12210729, 54) },
                { new OSInfo("GM", 15166853, 12210729, 54) },
                { new OSInfo("GM", 15186006, 12210729, 54) },
                { new OSInfo("GM", 15189044, 12210729, 54) },
            };
        }

        public E54(E54 original)
        {
            BaseHardwareType = original.BaseHardwareType;
            ChecksumSupport = original.ChecksumSupport;
            Description = original.Description;
            FlashCRCSupport = original.FlashCRCSupport;
            FlashIDSupport = original.FlashIDSupport;
            HardwareSlaveCPU = original.HardwareSlaveCPU;
            HardwareType = original.HardwareType;
            HardwareTypeOverridden = original.HardwareTypeOverridden;
            ImageBaseAddress = original.ImageBaseAddress;
            ImageSize = original.ImageSize;
            IsSupported = original.IsSupported;
            IsSupportedRead = original.IsSupportedRead;
            IsSupportedWrite = original.IsSupportedWrite;
            IsSupportedWriteBootSector = original.IsSupportedWriteBootSector;
            IsSupportedWriteBySegment = original.IsSupportedWriteBySegment;
            IsSupportedWriteSlaveCPU = original.IsSupportedWriteSlaveCPU;
            IsUnderDevelopment = original.IsUnderDevelopment;
            KernelBaseAddress = original.KernelBaseAddress;
            KernelMaxBlockSize = original.KernelMaxBlockSize;
            KernelVersionSupport = original.KernelVersionSupport;
            KeyAlgorithm = original.KeyAlgorithm;
            KnownOperatingSystems = original.KnownOperatingSystems;
            LoaderBaseAddress = original.LoaderBaseAddress;
            LoaderRequired = original.LoaderRequired;
            Manufacturer = original.Manufacturer;
        }

        public override ECUBase Clone()
        {
            return new E54(this);
        }
    }
}