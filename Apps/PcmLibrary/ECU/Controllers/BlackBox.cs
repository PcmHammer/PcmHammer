using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers
    {
    public class BlackBox : ECUBase
    {
        public BlackBox() {
            Manufacturer = "GM";
            Description = "98+ GenII Vortec";
            HardwareType = PcmType.BlackBox;
            BaseHardwareType = HardwareType;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            LoaderRequired = false;
            KernelBaseAddress = 0xFFC300;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 16;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = [
                // Service No 9366810 or 9355699
                new("GM", 9365095, 9366810, 16),
                new("GM", 16263425, 9366810, 16),
                // Vortec Black Box Service No 16263494
                new("GM", 9360505, 16263494, 16),
                new("GM", 9365085, 16263494, 16),
                new("GM", 9384185, 16263494, 16), // This OSID and the number below do not exist in PcmInfo?
                new("GM", 16251315, 16263494, 16),
                new("GM", 16265175, 16263494, 16),
                // BlackBox service number unknown
                new("GM", 16258745, 0, 16)
            ];
        }

        public BlackBox(BlackBox original)
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
            return new BlackBox(this);
        }
    }
}