using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P01 : ECUBase {
        public P01() {
            Manufacturer = "GM";
            Description = "P01";
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
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("HPT", 1251001, 0, 3) },
                { new OSInfo("HPT", 1261001, 0, 4) },
                { new OSInfo("HPT", 1271001, 0, 5) },
                { new OSInfo("HPT", 1281001, 0, 6) },
                { new OSInfo("HPT", 1271002, 0, 7) },
                { new OSInfo("HPT", 1251002, 0, 8) },
                { new OSInfo("HPT", 1261002, 0, 9) },
                { new OSInfo("HPT", 1281002, 0, 10) },
                { new OSInfo("HPT", 1271003, 0, 11) },
                { new OSInfo("HPT", 1251003, 0, 12) },
                { new OSInfo("HPT", 1261003, 0, 13) },
                { new OSInfo("HPT", 1281003, 0, 14) },
                { new OSInfo("HPT", 1250013, 0, 40) },
                { new OSInfo("HPT", 1250018, 0, 40) },
                { new OSInfo("HPT", 1251005, 0, 40) },
                { new OSInfo("HPT", 1251006, 0, 40) },
                { new OSInfo("HPT", 1251008, 0, 40) },
                { new OSInfo("HPT", 1251010, 0, 40) },
                { new OSInfo("HPT", 1251011, 0, 40) },
                { new OSInfo("HPT", 1251012, 0, 40) },
                { new OSInfo("HPT", 1251014, 0, 40) },
                { new OSInfo("HPT", 1251016, 0, 40) },
                { new OSInfo("HPT", 1251017, 0, 40) },
                { new OSInfo("HPT", 1260006, 0, 40) },
                { new OSInfo("HPT", 1260011, 0, 40) },
                { new OSInfo("HPT", 1261005, 0, 40) },
                { new OSInfo("HPT", 1261008, 0, 40) },
                { new OSInfo("HPT", 1261014, 0, 40) },
                { new OSInfo("HPT", 1261016, 0, 40) },
                { new OSInfo("HPT", 1270013, 0, 40) },
                { new OSInfo("HPT", 1270017, 0, 40) },
                { new OSInfo("HPT", 1271005, 0, 40) },
                { new OSInfo("HPT", 1271006, 0, 40) },
                { new OSInfo("HPT", 1271008, 0, 40) },
                { new OSInfo("HPT", 1271010, 0, 40) },
                { new OSInfo("HPT", 1271011, 0, 40) },
                { new OSInfo("HPT", 1271012, 0, 40) },
                { new OSInfo("HPT", 1271014, 0, 40) },
                { new OSInfo("HPT", 1271016, 0, 40) },
                { new OSInfo("HPT", 1271018, 0, 40) },
                { new OSInfo("HPT", 1273001, 0, 40) },
                { new OSInfo("HPT", 1273002, 0, 40) },
                { new OSInfo("HPT", 1273003, 0, 40) },
                { new OSInfo("HPT", 1273004, 0, 40) },
                { new OSInfo("HPT", 1273005, 0, 40) },
                { new OSInfo("HPT", 1273006, 0, 40) },
                { new OSInfo("HPT", 1273007, 0, 40) },
                { new OSInfo("HPT", 1273008, 0, 40) },
                { new OSInfo("HPT", 1273009, 0, 40) },
                { new OSInfo("HPT", 1273010, 0, 40) },
                { new OSInfo("HPT", 1273011, 0, 40) },
                { new OSInfo("HPT", 1273012, 0, 40) },
                { new OSInfo("HPT", 1273013, 0, 40) },
                { new OSInfo("HPT", 1273014, 0, 40) },
                { new OSInfo("HPT", 1281005, 0, 40) },
                { new OSInfo("HPT", 1281006, 0, 40) },
                { new OSInfo("HPT", 1281008, 0, 40) },
                { new OSInfo("HPT", 1281010, 0, 40) },
                { new OSInfo("HPT", 1281011, 0, 40) },
                { new OSInfo("HPT", 1281012, 0, 40) },
                { new OSInfo("HPT", 1281014, 0, 40) },
                { new OSInfo("HPT", 1281016, 0, 40) },
                { new OSInfo("HPT", 1281918, 0, 40) },

                { new OSInfo("GM", 9360360, 9354896, 40) },   
                { new OSInfo("GM", 9360361, 9354896, 40) },
                { new OSInfo("GM", 9361140, 9354896, 40) },
                { new OSInfo("GM", 9363996, 9354896, 40) },
                { new OSInfo("GM", 9365637, 9354896, 40) },
                { new OSInfo("GM", 9373372, 9354896, 40) },
                { new OSInfo("GM", 9379910, 9354896, 40) },
                { new OSInfo("GM", 9381344, 9354896, 40) },
                { new OSInfo("GM", 12205612, 9354896, 40) },
                { new OSInfo("GM", 12584929, 9354896, 40) },
                { new OSInfo("GM", 12593359, 9354896, 40) },
                { new OSInfo("GM", 12597506, 9354896, 40) },
                { new OSInfo("GM", 16253027, 9354896, 40) },
                { new OSInfo("GM", 12202088, 12200411, 40) },
                { new OSInfo("GM", 12206871, 12200411, 40) },
                { new OSInfo("GM", 12208322, 12200411, 40) },
                { new OSInfo("GM", 12209203, 12200411, 40) },
                { new OSInfo("GM", 12212156, 12200411, 40) },
                { new OSInfo("GM", 12216125, 12200411, 40) },
                { new OSInfo("GM", 12221588, 12200411, 40) },
                { new OSInfo("GM", 12225074, 12200411, 40) },
                { new OSInfo("GM", 12593358, 12200411, 40) },

                { new OSInfo("EFILive", 01250001, 0, 40) },
                { new OSInfo("EFILive", 01290001, 0, 40) },
                { new OSInfo("EFILive", 02020002, 0, 40) },
                { new OSInfo("EFILive", 02040001, 0, 40) },
                { new OSInfo("EFILive", 03150002, 0, 40) },
                { new OSInfo("EFILive", 04072901, 0, 40) },
                { new OSInfo("EFILive", 04073101, 0, 40) },
                { new OSInfo("EFILive", 04110003, 0, 40) },
                { new OSInfo("EFILive", 05120003, 0, 40) },
                { new OSInfo("EFILive", 01250002, 0, 40) },
                { new OSInfo("EFILive", 01290002, 0, 40) },
                { new OSInfo("EFILive", 02020003, 0, 40) },
                { new OSInfo("EFILive", 02040002, 0, 40) },
                { new OSInfo("EFILive", 03150003, 0, 40) },
                { new OSInfo("EFILive", 04072902, 0, 40) },
                { new OSInfo("EFILive", 04073102, 0, 40) },
                { new OSInfo("EFILive", 04140001, 0, 40) },
                { new OSInfo("EFILive", 01250003, 0, 40) },
                { new OSInfo("EFILive", 01290003, 0, 40) },
                { new OSInfo("EFILive", 02020005, 0, 40) },
                { new OSInfo("EFILive", 02040003, 0, 40) },
                { new OSInfo("EFILive", 03170001, 0, 40) },
                { new OSInfo("EFILive", 04072903, 0, 40) },
                { new OSInfo("EFILive", 04073103, 0, 40) },
                { new OSInfo("EFILive", 04140002, 0, 40) },
                { new OSInfo("EFILive", 01270001, 0, 40) },
                { new OSInfo("EFILive", 01290005, 0, 40) },
                { new OSInfo("EFILive", 02030001, 0, 40) },
                { new OSInfo("EFILive", 03110001, 0, 40) },
                { new OSInfo("EFILive", 03190001, 0, 40) },
                { new OSInfo("EFILive", 04073001, 0, 40) },
                { new OSInfo("EFILive", 04080001, 0, 40) },
                { new OSInfo("EFILive", 04140003, 0, 40) },
                { new OSInfo("EFILive", 01270002, 0, 40) },
                { new OSInfo("EFILive", 02010001, 0, 40) },
                { new OSInfo("EFILive", 02030002, 0, 40) },
                { new OSInfo("EFILive", 03130001, 0, 40) },
                { new OSInfo("EFILive", 03190002, 0, 40) },
                { new OSInfo("EFILive", 04073002, 0, 40) },
                { new OSInfo("EFILive", 04110001, 0, 40) },
                { new OSInfo("EFILive", 05120001, 0, 40) },
                { new OSInfo("EFILive", 01270003, 0, 40) },
                { new OSInfo("EFILive", 02020001, 0, 40) },
                { new OSInfo("EFILive", 02030003, 0, 40) },
                { new OSInfo("EFILive", 03150001, 0, 40) },
                { new OSInfo("EFILive", 03190003, 0, 40) },
                { new OSInfo("EFILive", 04073003, 0, 40) },
                { new OSInfo("EFILive", 04110002, 0, 40) },
                { new OSInfo("EFILive", 05120002, 0, 40) },
            };

        }
    }
}