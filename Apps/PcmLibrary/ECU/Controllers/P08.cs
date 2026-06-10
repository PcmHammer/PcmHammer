using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers
{
    public class P08 : ECUBase
    {
        public P08() {
            Manufacturer = "GM";
            Description = "I4";
            HardwareType = PcmType.P08;
            BaseHardwareType = HardwareType;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = false;
            LoaderRequired = false;
            KernelBaseAddress = 0xFFAC00;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 13;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                new("GM", 9364970, 9356249, 13),
                new("GM", 9382954, 9356249, 13),
                new("GM", 9384480, 9356249, 13),
                new("GM", 9387226, 9356249, 13),
                new("GM", 12202774, 9356249, 13),
                new("GM", 12205537, 9356249, 13),
                new("GM", 12205561, 9356249, 13),
                new("GM", 12206029, 9356249, 13),
                new("GM", 12206030, 9356249, 13),
                new("GM", 12206037, 9356249, 13),
                new("GM", 12206044, 9356249, 13),
                new("GM", 12208154, 9356249, 13),
                new("GM", 12208156, 9356249, 13),
                new("GM", 12208767, 9356249, 13),
                new("GM", 12208773, 9356249, 13),
                new("GM", 12216187, 9356249, 13),
                new("GM", 12216489, 9356249, 13),
                new("GM", 12216571, 9356249, 13),
                new("GM", 12221096, 9356249, 13),
                new("GM", 12221111, 9356249, 13),
                new("GM", 12222128, 9356249, 13),
                new("GM", 12222134, 9356249, 13),
                new("GM", 12222135, 9356249, 13),
                new("GM", 12225346, 9356249, 13),
                new("GM", 12225345, 9356249, 13),
                new("GM", 16257436, 9356249, 13),
                new("GM", 12222446, 9356249, 13),

                new("GM", 9392792, 12202203, 13),
                new("GM", 9392795, 12202203, 13),
                new("GM", 9392796, 12202203, 13),
                new("GM", 12201233, 12202203, 13),
                new("GM", 12205552, 12202203, 13),
                new("GM", 12223041, 12202203, 13),
                new("GM", 12223044, 12202203, 13),
                new("GM", 12223046, 12202203, 13),
                new("GM", 12225338, 12202203, 13),
                new("GM", 12225340, 12202203, 13),
                new("GM", 12571886, 12202203, 13),
                new("GM", 12580027, 12202203, 13),
                new("GM", 12580029, 12202203, 13),

                new("GM", 12604676, 12605873, 13),
                new("GM", 12607442, 12605873, 13),
                new("GM", 12608370, 12605873, 13),
                new("GM", 12610013, 12605873, 13),
                new("GM", 12611951, 12605873, 13),

                new("GM", 9351310, 16228016, 13),
                new("GM", 9353727, 16228016, 13),
                new("GM", 9364359, 16228016, 13),
                new("GM", 9364362, 16228016, 13),
                new("GM", 9364367, 16228016, 13),
                new("GM", 9364370, 16228016, 13),
                new("GM", 9364966, 16228016, 13),
                new("GM", 9364974, 16228016, 13),
                new("GM", 9364976, 16228016, 13),
                new("GM", 9365280, 16228016, 13),
                new("GM", 9365284, 16228016, 13),
                new("GM", 9365286, 16228016, 13),
                new("GM", 9365312, 16228016, 13),
                new("GM", 9365338, 16228016, 13),
                new("GM", 9374332, 16228016, 13),
                new("GM", 9374334, 16228016, 13),
                new("GM", 9383061, 16228016, 13),
                new("GM", 9383064, 16228016, 13),
                new("GM", 9383067, 16228016, 13),
                new("GM", 9383076, 16228016, 13),
                new("GM", 9387885, 16228016, 13),
                new("GM", 12222105, 16228016, 13),
                new("GM", 16254764, 16228016, 13),
                new("GM", 16259649, 16228016, 13),
                new("GM", 16259657, 16228016, 13),
                new("GM", 16259674, 16228016, 13),
                new("GM", 16259706, 16228016, 13),
                new("GM", 16259718, 16228016, 13),

                new("GM", 9351290, 16228016, 13),
                new("GM", 9351297, 16228016, 13),
                new("GM", 9351321, 16228016, 13),
                new("GM", 9353418, 16228016, 13),
                new("GM", 9353422, 16228016, 13),
                new("GM", 9354904, 16228016, 13),
                new("GM", 9354907, 16228016, 13),
                new("GM", 9354910, 16228016, 13),
                new("GM", 9353732, 16228016, 13),
                new("GM", 9353738, 16228016, 13),
                new("GM", 9355300, 16228016, 13),
                new("GM", 9355302, 16228016, 13),
                new("GM", 9356822, 16228016, 13),
                new("GM", 9364356, 16228016, 13),
                new("GM", 9354914, 16228016, 13),
                new("GM", 9365276, 16228016, 13),
                new("GM", 9365292, 16228016, 13),
                new("GM", 9365308, 16228016, 13),
                new("GM", 9365310, 16228016, 13),
                new("GM", 9365324, 16228016, 13),
                new("GM", 9368451, 16228016, 13),
                new("GM", 9367522, 16228016, 13),
                new("GM", 9368547, 16228016, 13),
                new("GM", 9373177, 16228016, 13),
                new("GM", 9372478, 16228016, 13),
                new("GM", 9374787, 16228016, 13),
                new("GM", 9387229, 16228016, 13),
                new("GM", 9382927, 16228016, 13),
                new("GM", 9382928, 16228016, 13),
                new("GM", 9382941, 16228016, 13),
                new("GM", 9382948, 16228016, 13),
                new("GM", 9382957, 16228016, 13),
                new("GM", 9383079, 16228016, 13),
                new("GM", 9383089, 16228016, 13),
                new("GM", 9387205, 16228016, 13),
                new("GM", 9387214, 16228016, 13),
                new("GM", 9387552, 16228016, 13),
                new("GM", 12201228, 16228016, 13),
                new("GM", 12201231, 16228016, 13),
                new("GM", 12201238, 16228016, 13),
                new("GM", 12205538, 16228016, 13),
                new("GM", 12205545, 16228016, 13),
                new("GM", 12206019, 16228016, 13),
                new("GM", 12206025, 16228016, 13),
                new("GM", 12206042, 16228016, 13),
                new("GM", 12206045, 16228016, 13),
                new("GM", 12206049, 16228016, 13),
                new("GM", 12208535, 16228016, 13),
                new("GM", 12216490, 16228016, 13),
                new("GM", 12208527, 16228016, 13),
                new("GM", 12216567, 16228016, 13),
                new("GM", 12216568, 16228016, 13),
                new("GM", 12217195, 16228016, 13),
                new("GM", 12221098, 16228016, 13),
                new("GM", 12222110, 16228016, 13),
                new("GM", 12222131, 16228016, 13),
                new("GM", 12223050, 16228016, 13),
                new("GM", 12225336, 16228016, 13),
                new("GM", 12571890, 16228016, 13),
                new("GM", 12578485, 16228016, 13),
                new("GM", 12580025, 16228016, 13),
                new("GM", 12583655, 16228016, 13),
                new("GM", 16267114, 16228016, 13),
                new("GM", 16267097, 16228016, 13),

                //Crowbar: Generated from cross-translation of PcmInfo.cs.
				new("GM", 16253027, 16228016, 13),

            };
        }

        public P08(P08 original)
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
            return new P08(this);
        }
    }
}