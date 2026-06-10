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
            Description = "P05 (VPW)";
            HardwareType = PcmType.P05;
            BaseHardwareType = HardwareType;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            IsSupportedWriteBootSector = false;
            LoaderRequired = false;
            KernelBaseAddress = 0xFFC100;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                new("GM", 12584057, 12581501, 0x35),
                new("GM", 12584058, 12581501, 0x35),
                new("GM", 12588933, 12581501, 0x35),
                new("GM", 12619740, 12581501, 0x35),
                new("GM", 12619742, 12581501, 0x35),

                new("GM", 12597270, 12591279, 0x35),

                new("GM", 12603217, 12604963, 0x35),
                // Service number unknown
                new("GM", 12592928, 0, 0x35),
                new("GM", 12596136, 0, 0x35),
                new("GM", 12596138, 0, 0x35),
                new("GM", 12600367, 0, 0x35),
                new("GM", 12603291, 0, 0x35),

                //Crowbar: Generated from cross-translation of PcmInfo.cs.
				new("GM", 12588932, 12581501, 53),

            };
        }

        public P05(P05 original)
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
            return new P05(this);
        }
    }
}