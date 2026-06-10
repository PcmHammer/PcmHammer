using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P59 : P01
    {
        public P59() {
            Description = base.Description;
            BaseHardwareType = base.HardwareType;
            HardwareType = PcmType.P59;
            ImageSize = 1024 * 1024;

            KnownOperatingSystems = new List<OSInfo>() {
                new("GM", 12590777, 12583560, 40),

                new("GM", 12591725, 12589463, 40),
                new("GM", 12592618, 12589463, 40),
                new("GM", 12593555, 12589463, 40),
                new("GM", 12606961, 12589463, 40),
                new("GM", 12612115, 12589463, 40),
                //Crowbar: Generated from cross-translation of PcmInfo.cs.
                new("GM", 12564440, 12589463, 40),
                new("GM", 12585950, 12589463, 40),
                new("GM", 12587603, 12589463, 40),
                new("GM", 12587604, 12589463, 40),
                new("GM", 12588804, 12589463, 40),
                new("GM", 12591725, 12589463, 40),
                new("GM", 12592425, 12589463, 40),
                new("GM", 12592433, 12589463, 40),
                new("GM", 12592618, 12589463, 40),
                new("GM", 12593555, 12589463, 40),
                new("GM", 12606960, 12589463, 40),
                new("GM", 12606961, 12589463, 40),
                new("GM", 12612114, 12589463, 40),
                new("GM", 12612115, 12589463, 40),

                new("GM", 12587603, 12589462, 40),
                new("GM", 12587604, 12586243, 40),
                new("GM", 76030003, 12586243, 40),
                new("GM", 76030004, 12586243, 40),
                new("GM", 76030005, 12586243, 40),
                new("GM", 76030006, 12586243, 40),
                new("GM", 76030007, 12586243, 40),
                new("GM", 76030008, 12586243, 40),
                new("GM", 76030009, 12586243, 40),

                new("GM", 12578128, 12582605, 40),
                new("GM", 12579405, 12582605, 40),
                new("GM", 12580055, 12582605, 40),
                new("GM", 12593058, 12582605, 40),

                new("GM", 12587811, 12582811, 40),
                new("GM", 12605114, 12582811, 40),
                new("GM", 12606807, 12582811, 40),
                new("GM", 12608669, 12582811, 40),
                new("GM", 12613245, 12582811, 40),
                new("GM", 12613246, 12582811, 40),
                new("GM", 12613247, 12582811, 40),
                new("GM", 12619623, 12582811, 40),

                new("GM", 12597120, 12602802, 40),
                new("GM", 12613248, 12602802, 40),
                new("GM", 12619624, 12602802, 40),

                //Crowbar: Generated from cross-translation of PcmInfo.cs.
				new("GM", 12590777, 12583560, 40),

                new("EFILive", 3150003, 0, 40),
                new("EFILive", 3190003, 0, 40),
                new("EFILive", 4073003, 0, 40),
                new("EFILive", 4073103, 0, 40),
                new("EFILive", 4110001, 0, 40),
                new("EFILive", 4110003, 0, 40),
                new("EFILive", 4140003, 0, 40),
                new("EFILive", 4140004, 0, 40),
                new("EFILive", 5120002, 0, 40),
                new("EFILive", 5120003, 0, 40),
            };                     
        }

        public P59(P59 original)
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
            return new P59(this);
        }

        // VCM Suite COS V2/V3 P59 pattern: 7-digit, starts "12", position 4='0', position 3='2' or '3', position 5='5'
        public override bool ECUSupportsOSID(uint osid) {
            if (base.ECUSupportsOSID(osid)) return true;
            string s = osid.ToString();
            return s.Length == 7 && s.Substring(0, 2) == "12" && s[4] == '0' && (s[3] == '2' || s[3] == '3') && s[5] == '5';
        }

        public override void SetCurrentOSID(uint osid)
        {
            base.SetCurrentOSID(osid);
            if (KeyAlgorithm == 0 && ECUSupportsOSID(osid))
            {
                base.SetOverrideOSID(new("HPT", osid, 0, 40));
            }
        }
    }
}




