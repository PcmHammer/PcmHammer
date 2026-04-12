using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P01 : ECUBase {
        public P01() {
            Description = "P01 512K";
            HardwareType = PcmType.P01_P59;
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
            ImageSize = 512 * 1024; //for the P01, override for a P59 by OSID
            KeyAlgorithm = 40;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo(1251001, 0, Description + "; HPT 2 Bar", 3) },
                { new OSInfo(1261001, 0, Description + "; HPT 3 Bar", 4) },
                { new OSInfo(1271001, 0, Description + "; HPT Mafless", 5) },
                { new OSInfo(1273002, 0, Description + "; HPT SD RTT", 40) },
                { new OSInfo(1281001, 0, Description + "; HPT MAF RTT", 6) },
                { new OSInfo(1271002, 0, Description + "; HPT Mafless", 7) },
                { new OSInfo(1251002, 0, Description + "; HPT 2 Bar", 8) },
                { new OSInfo(1261002, 0, Description + "; HPT MAF RTT", 9) },
                { new OSInfo(1281002, 0, Description + "; HPT 3 Bar", 10) },
                { new OSInfo(1271003, 0, Description + "; HPT Mafless", 11) },
                { new OSInfo(1251003, 0, Description + "; HPT 2 Bar", 12) },
                { new OSInfo(1261003, 0, Description + "; HPT 3 Bar", 13) },
                { new OSInfo(1281003, 0, Description + "; HPT MAF RTT", 14) },
                { new OSInfo(1250013, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1250018, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251005, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251006, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251008, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251010, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251011, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251012, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251014, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251016, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251017, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1260006, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1260011, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261005, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261008, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261014, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261016, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1270013, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1270017, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271005, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271006, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271008, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271010, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271011, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271012, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271014, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271016, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271018, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281005, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281006, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281008, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281010, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281011, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281012, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281014, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281016, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281918, 0, Description + "; HPT UnkOS", 40) },
                { new OSInfo(9360360, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9360361, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9361140, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9363996, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9365637, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9373372, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9379910, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(9381344, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(12205612, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(12584929, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(12593359, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(12597506, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(16253027, 0, Description + "; Srv #9354896", 40) },
                { new OSInfo(12202088, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12206871, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12208322, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12209203, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12212156, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12216125, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12221588, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12225074, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(12593358, 0, Description + "; Srv #12200411", 40) },
                { new OSInfo(01250001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072901, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073101, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01250002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072902, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073102, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01250003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020005, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03170001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072903, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073103, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290005, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03110001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04080001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02010001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03130001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150001, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073003, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110002, 0, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120002, 0, Description + "; EFI Live COS", 40) },
            };

        }
    }
}