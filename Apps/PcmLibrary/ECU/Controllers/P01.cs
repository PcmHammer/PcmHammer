using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P01 : ECUBase {
        public P01() {
            Description = "P01 512KiB";
            HardwareType = PcmType.P01;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = true;
            LoaderRequired = false;
            KernelFileName = "Kernel-P01.bin";
            KernelBaseAddress = 0xFF8000;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 40;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // VCM Suite COS Version 1
                new OSInfo(1251001, 0, "VCM Suite 2 Bar", 3),
                new OSInfo(1261001, 0, "VCM Suite 3 Bar", 4),
                new OSInfo(1271001, 0, "VCM Suite Mafless", 5),
                new OSInfo(1281001, 0, "VCM Suite MAF RTT", 6),
                new OSInfo(1271002, 0, "VCM Suite Mafless", 7),
                new OSInfo(1251002, 0, "VCM Suite 2 Bar", 8),
                new OSInfo(1261002, 0, "VCM Suite MAF RTT", 9),
                new OSInfo(1281002, 0, "VCM Suite 3 Bar", 10),
                new OSInfo(1271003, 0, "VCM Suite Mafless", 11),
                new OSInfo(1251003, 0, "VCM Suite 2 Bar", 12),
                new OSInfo(1261003, 0, "VCM Suite 3 Bar", 13),
                new OSInfo(1281003, 0, "VCM Suite MAF RTT", 14),
                // HPT COS
                new OSInfo(1250013, 0, "VCM Suite COS", 40),
                new OSInfo(1250018, 0, "VCM Suite COS", 40),
                new OSInfo(1251005, 0, "VCM Suite COS", 40),
                new OSInfo(1251006, 0, "VCM Suite COS", 40),
                new OSInfo(1251008, 0, "VCM Suite COS", 40),
                new OSInfo(1251010, 0, "VCM Suite COS", 40),
                new OSInfo(1251011, 0, "VCM Suite COS", 40),
                new OSInfo(1251012, 0, "VCM Suite COS", 40),
                new OSInfo(1251014, 0, "VCM Suite COS", 40),
                new OSInfo(1251016, 0, "VCM Suite COS", 40),
                new OSInfo(1251017, 0, "VCM Suite COS", 40),
                new OSInfo(1260006, 0, "VCM Suite COS", 40),
                new OSInfo(1260011, 0, "VCM Suite COS", 40),
                new OSInfo(1261005, 0, "VCM Suite COS", 40),
                new OSInfo(1261008, 0, "VCM Suite COS", 40),
                new OSInfo(1261014, 0, "VCM Suite COS", 40),
                new OSInfo(1261016, 0, "VCM Suite COS", 40),
                new OSInfo(1270013, 0, "VCM Suite COS", 40),
                new OSInfo(1270017, 0, "VCM Suite COS", 40),
                new OSInfo(1271005, 0, "VCM Suite COS", 40),
                new OSInfo(1271006, 0, "VCM Suite COS", 40),
                new OSInfo(1271008, 0, "VCM Suite COS", 40),
                new OSInfo(1271010, 0, "VCM Suite COS", 40),
                new OSInfo(1271011, 0, "VCM Suite COS", 40),
                new OSInfo(1271012, 0, "VCM Suite COS", 40),
                new OSInfo(1271014, 0, "VCM Suite COS", 40),
                new OSInfo(1271016, 0, "VCM Suite COS", 40),
                new OSInfo(1271018, 0, "VCM Suite COS", 40),
                new OSInfo(1273001, 0, "VCM Suite COS", 40),
                new OSInfo(1273002, 0, "VCM Suite COS", 40),
                new OSInfo(1273003, 0, "VCM Suite COS", 40),
                new OSInfo(1273004, 0, "VCM Suite COS", 40),
                new OSInfo(1273005, 0, "VCM Suite COS", 40),
                new OSInfo(1273006, 0, "VCM Suite COS", 40),
                new OSInfo(1273007, 0, "VCM Suite COS", 40),
                new OSInfo(1273008, 0, "VCM Suite COS", 40),
                new OSInfo(1273009, 0, "VCM Suite COS", 40),
                new OSInfo(1273010, 0, "VCM Suite COS", 40),
                new OSInfo(1273011, 0, "VCM Suite COS", 40),
                new OSInfo(1273012, 0, "VCM Suite COS", 40),
                new OSInfo(1273013, 0, "VCM Suite COS", 40),
                new OSInfo(1273014, 0, "VCM Suite COS", 40),
                new OSInfo(1281005, 0, "VCM Suite COS", 40),
                new OSInfo(1281006, 0, "VCM Suite COS", 40),
                new OSInfo(1281008, 0, "VCM Suite COS", 40),
                new OSInfo(1281010, 0, "VCM Suite COS", 40),
                new OSInfo(1281011, 0, "VCM Suite COS", 40),
                new OSInfo(1281012, 0, "VCM Suite COS", 40),
                new OSInfo(1281014, 0, "VCM Suite COS", 40),
                new OSInfo(1281016, 0, "VCM Suite COS", 40),
                new OSInfo(1281918, 0, "VCM Suite COS", 40),
                // P01 EFI Live COS (SN 12200411)
                new OSInfo(1250001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1250002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1250003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1270001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1270002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1270003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1290001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1290002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1290003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(1290005, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2010001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2020001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2020002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2020003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2020005, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2030001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2030002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2030003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2040001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2040002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(2040003, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3110001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3130001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3150001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3150002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3170001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3190001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(3190002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4072901, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4072902, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4072903, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4073001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4073002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4073101, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4073102, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4080001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4110002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4140001, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(4140002, 12200411, "P01 EFI Live COS", 40),
                new OSInfo(5120001, 12200411, "P01 EFI Live COS", 40),
                // P01 Service No 9354896
                new OSInfo(9360360, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9360361, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9361140, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9363996, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9365637, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9373372, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9376077, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9378746, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9379910, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(9381344, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(12205612, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(12584929, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(12593359, 9354896, "P01 Service No 9354896", 40),
                new OSInfo(12597506, 9354896, "P01 Service No 9354896", 40),
                // P01 Service No 12200411
                new OSInfo(12202088, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12206871, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12208322, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12209203, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12212156, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12213218, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12216125, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12220483, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12221588, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12225074, 12200411, "P01 Service No 12200411", 40),
                new OSInfo(12593358, 12200411, "P01 Service No 12200411", 40),
            };
        }

        // VCM Suite COS V2/V3 P01 pattern: 7-digit, starts "12", position 4='0', position 3='2' or '3', position 5='0'
        public override bool ECUSupportsOSID(uint osid) {
            if (base.ECUSupportsOSID(osid)) return true;
            string s = osid.ToString();
            return s.Length == 7 && s.Substring(0, 2) == "12" && s[4] == '0' && (s[3] == '2' || s[3] == '3') && s[5] == '0';
        }
    }
}
