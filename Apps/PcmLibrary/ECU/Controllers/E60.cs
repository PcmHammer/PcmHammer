using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers 
{
    public class E60 : ECUBase
    {
        public E60() {
            Manufacturer = "GM";
            Description = "LLY Duramax";
            HardwareType = PcmType.E60;
            BaseHardwareType = HardwareType;
            KeyAlgorithm = 2;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KnownOperatingSystems = new List<OSInfo>() {
                // LLY Service No 12244189
                new("GM", 15141668, 12244189, 2),
                new("GM", 15193885, 12244189, 2),
                new("GM", 15228758, 12244189, 2),
                new("GM", 15231599, 12244189, 2),
                new("GM", 15231600, 12244189, 2),
                new("GM", 15879103, 12244189, 2),
                new("GM", 15087230, 12244189, 2),
                // LLY EFI Live COS
                new("EFILive", 04166801, 0, 2),
                new("EFILive", 04166805, 0, 2),
                new("EFILive", 05160001, 0, 2),
                new("EFILive", 05160005, 0, 2),
                new("EFILive", 05388501, 0, 2),
                new("EFILive", 05388505, 0, 2),
                new("EFILive", 05875801, 0, 2),
                new("EFILive", 05875805, 0, 2),
            };
        }

        public E60(E60 original)
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
            return new E60(this);
        }
    }
}