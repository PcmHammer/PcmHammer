using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace PcmHacking.ECU.Controllers
{
    public class P12_2M : P12
    {
        public P12_2M() {
            this.Description = "P12(2MB)";
            this.HardwareType = PcmType.P12_2M;
            this.BaseHardwareType = PcmType.P12;
            this.ImageSize = 2048 * 1024; // 2MB
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12609805, 12569773, 91) },
                { new OSInfo("GM", 12611642, 12569773, 91) },
                { new OSInfo("GM", 12613422, 12569773, 91) },
                { new OSInfo("GM", 12618164, 12569773, 91) },
            };
        }

        public P12_2M(P12_2M original)
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
            return new P12_2M(this);
        }
    }
}
