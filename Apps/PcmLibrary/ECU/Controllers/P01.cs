// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P01 : ECUBase {
        public P01() {
            Manufacturer = "GM";
            Description = "99+ Gen III V8; 4.3L V6";
            HardwareType = PcmType.P01;
            BaseHardwareType = HardwareType;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = true;
            LoaderRequired = false;
            KernelBaseAddress = 0xFF8000;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 40;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = [
                // HPT COS V1
                new("HPT", 1251001, 0, 3),
                new("HPT", 1261001, 0, 4),
                new("HPT", 1271001, 0, 5),
                new("HPT", 1281001, 0, 6),
                new("HPT", 1271002, 0, 7),
                new("HPT", 1251002, 0, 8),
                new("HPT", 1261002, 0, 9),
                new("HPT", 1281002, 0, 10),
                new("HPT", 1271003, 0, 11),
                new("HPT", 1251003, 0, 12),
                new("HPT", 1261003, 0, 13),
                new("HPT", 1281003, 0, 14),
                // HPT COS - Default key
                new("HPT", 1250013, 0, 40),
                new("HPT", 1250018, 0, 40),
                new("HPT", 1251005, 0, 40),
                new("HPT", 1251006, 0, 40),
                new("HPT", 1251008, 0, 40),
                new("HPT", 1251010, 0, 40),
                new("HPT", 1251011, 0, 40),
                new("HPT", 1251012, 0, 40),
                new("HPT", 1251014, 0, 40),
                new("HPT", 1251016, 0, 40),
                new("HPT", 1251017, 0, 40),
                new("HPT", 1260006, 0, 40),
                new("HPT", 1260011, 0, 40),
                new("HPT", 1261005, 0, 40),
                new("HPT", 1261008, 0, 40),
                new("HPT", 1261014, 0, 40),
                new("HPT", 1261016, 0, 40),
                new("HPT", 1270013, 0, 40),
                new("HPT", 1270017, 0, 40),
                new("HPT", 1271005, 0, 40),
                new("HPT", 1271006, 0, 40),
                new("HPT", 1271008, 0, 40),
                new("HPT", 1271010, 0, 40),
                new("HPT", 1271011, 0, 40),
                new("HPT", 1271012, 0, 40),
                new("HPT", 1271014, 0, 40),
                new("HPT", 1271016, 0, 40),
                new("HPT", 1271018, 0, 40),
                new("HPT", 1273001, 0, 40),
                new("HPT", 1273002, 0, 40),
                new("HPT", 1273003, 0, 40),
                new("HPT", 1273004, 0, 40),
                new("HPT", 1273005, 0, 40),
                new("HPT", 1273006, 0, 40),
                new("HPT", 1273007, 0, 40),
                new("HPT", 1273008, 0, 40),
                new("HPT", 1273009, 0, 40),
                new("HPT", 1273010, 0, 40),
                new("HPT", 1273011, 0, 40),
                new("HPT", 1273012, 0, 40),
                new("HPT", 1273013, 0, 40),
                new("HPT", 1273014, 0, 40),
                new("HPT", 1281005, 0, 40),
                new("HPT", 1281006, 0, 40),
                new("HPT", 1281008, 0, 40),
                new("HPT", 1281010, 0, 40),
                new("HPT", 1281011, 0, 40),
                new("HPT", 1281012, 0, 40),
                new("HPT", 1281014, 0, 40),
                new("HPT", 1281016, 0, 40),
                new("HPT", 1281918, 0, 40),


                new("GM", 9360360, 9354896, 40),
                new("GM", 9360361, 9354896, 40),
                new("GM", 9361140, 9354896, 40),
                new("GM", 9363996, 9354896, 40),
                new("GM", 9365637, 9354896, 40),
                new("GM", 9373372, 9354896, 40),
                new("GM", 9376077, 9354896, 40),
                new("GM", 9378746, 9354896, 40),
                new("GM", 9379910, 9354896, 40),
                new("GM", 9381344, 9354896, 40),
                new("GM", 12205612, 9354896, 40),
                new("GM", 12584929, 9354896, 40),
                new("GM", 12593359, 9354896, 40),
                new("GM", 12597506, 9354896, 40),
                new("GM", 12202088, 12200411, 40),
                new("GM", 12206871, 12200411, 40),
                new("GM", 12208322, 12200411, 40),
                new("GM", 12209203, 12200411, 40),
                new("GM", 12212156, 12200411, 40),
                new("GM", 12216125, 12200411, 40),
                new("GM", 12221588, 12200411, 40),
                new("GM", 12225074, 12200411, 40),
                new("GM", 12593358, 12200411, 40),

                new("EFILive", 1250001, 12200411, 40),
                new("EFILive", 1250002, 12200411, 40),
                new("EFILive", 1250003, 12200411, 40),
                new("EFILive", 1270001, 12200411, 40),
                new("EFILive", 1270002, 12200411, 40),
                new("EFILive", 1270003, 12200411, 40),
                new("EFILive", 1290001, 12200411, 40),
                new("EFILive", 1290002, 12200411, 40),
                new("EFILive", 1290003, 12200411, 40),
                new("EFILive", 1290005, 12200411, 40),
                new("EFILive", 2010001, 12200411, 40),
                new("EFILive", 2020001, 12200411, 40),
                new("EFILive", 2020002, 12200411, 40),
                new("EFILive", 2020003, 12200411, 40),
                new("EFILive", 2020005, 12200411, 40),
                new("EFILive", 2030001, 12200411, 40),
                new("EFILive", 2030002, 12200411, 40),
                new("EFILive", 2030003, 12200411, 40),
                new("EFILive", 2040001, 12200411, 40),
                new("EFILive", 2040002, 12200411, 40),
                new("EFILive", 2040003, 12200411, 40),
                new("EFILive", 3110001, 12200411, 40),
                new("EFILive", 3130001, 12200411, 40),
                new("EFILive", 3150001, 12200411, 40),
                new("EFILive", 3150002, 12200411, 40),
                new("EFILive", 3170001, 12200411, 40),
                new("EFILive", 3190001, 12200411, 40),
                new("EFILive", 3190002, 12200411, 40),
                new("EFILive", 4072901, 12200411, 40),
                new("EFILive", 4072902, 12200411, 40),
                new("EFILive", 4072903, 12200411, 40),
                new("EFILive", 4073001, 12200411, 40),
                new("EFILive", 4073002, 12200411, 40),
                new("EFILive", 4073101, 12200411, 40),
                new("EFILive", 4073102, 12200411, 40),
                new("EFILive", 4080001, 12200411, 40),
                new("EFILive", 4110002, 12200411, 40),
                new("EFILive", 4140001, 12200411, 40),
                new("EFILive", 4140002, 12200411, 40),
                new("EFILive", 5120001, 12200411, 40),            
            ];
        }

        public P01(P01 original)
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
            return new P01(this);
        }

        // VCM Suite COS V2/V3 P01 pattern: 7-digit, starts "12", position 4='0', position 3='2' or '3', position 5='0'
        public override bool ECUSupportsOSID(uint osid) {
            if (base.ECUSupportsOSID(osid)) return true;
            string s = osid.ToString();
            return s.Length == 7 && s.Substring(0, 2) == "12" && s[4] == '0' && (s[3] == '2' || s[3] == '3') && s[5] == '0';
        }

        public override void SetCurrentOSID(uint osid)
        {
            base.SetCurrentOSID(osid);
            if(KeyAlgorithm == 0 && ECUSupportsOSID(osid))
            {
                base.SetOverrideOSID(new("HPT", osid, 0, 40));
            }
        }
    }
}
