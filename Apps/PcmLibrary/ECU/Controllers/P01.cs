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
                { new OSInfo(1251001, Description + "; HPT 2 Bar", 3) },
                { new OSInfo(1261001, Description + "; HPT 3 Bar", 4) },
                { new OSInfo(1271001, Description + "; HPT Mafless", 5) },
                { new OSInfo(1273002, Description + "; HPT SD RTT", 40) },
                { new OSInfo(1281001, Description + "; HPT MAF RTT", 6) },
                { new OSInfo(1271002, Description + "; HPT Mafless", 7) },
                { new OSInfo(1251002, Description + "; HPT 2 Bar", 8) },
                { new OSInfo(1261002, Description + "; HPT MAF RTT", 9) },
                { new OSInfo(1281002, Description + "; HPT 3 Bar", 10) },
                { new OSInfo(1271003, Description + "; HPT Mafless", 11) },
                { new OSInfo(1251003, Description + "; HPT 2 Bar", 12) },
                { new OSInfo(1261003, Description + "; HPT 3 Bar", 13) },
                { new OSInfo(1281003, Description + "; HPT MAF RTT", 14) },
                { new OSInfo(1250013, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1250018, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251005, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251006, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251008, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251010, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251011, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251012, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251014, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251016, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1251017, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1260006, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1260011, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261005, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261008, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261014, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1261016, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1270013, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1270017, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271005, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271006, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271008, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271010, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271011, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271012, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271014, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271016, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1271018, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281005, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281006, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281008, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281010, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281011, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281012, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281014, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281016, Description + "; HPT UnkOS", 40) },
                { new OSInfo(1281918, Description + "; HPT UnkOS", 40) },
                { new OSInfo(9360360, Description + "; Srv #9354896", 40) },
                { new OSInfo(9360361, Description + "; Srv #9354896", 40) },
                { new OSInfo(9361140, Description + "; Srv #9354896", 40) },
                { new OSInfo(9363996, Description + "; Srv #9354896", 40) },
                { new OSInfo(9365637, Description + "; Srv #9354896", 40) },
                { new OSInfo(9373372, Description + "; Srv #9354896", 40) },
                { new OSInfo(9379910, Description + "; Srv #9354896", 40) },
                { new OSInfo(9381344, Description + "; Srv #9354896", 40) },
                { new OSInfo(12205612, Description + "; Srv #9354896", 40) },
                { new OSInfo(12584929, Description + "; Srv #9354896", 40) },
                { new OSInfo(12593359, Description + "; Srv #9354896", 40) },
                { new OSInfo(12597506, Description + "; Srv #9354896", 40) },
                { new OSInfo(16253027, Description + "; Srv #9354896", 40) },
                { new OSInfo(12202088, Description + "; Srv #12200411", 40) },
                { new OSInfo(12206871, Description + "; Srv #12200411", 40) },
                { new OSInfo(12208322, Description + "; Srv #12200411", 40) },
                { new OSInfo(12209203, Description + "; Srv #12200411", 40) },
                { new OSInfo(12212156, Description + "; Srv #12200411", 40) },
                { new OSInfo(12216125, Description + "; Srv #12200411", 40) },
                { new OSInfo(12221588, Description + "; Srv #12200411", 40) },
                { new OSInfo(12225074, Description + "; Srv #12200411", 40) },
                { new OSInfo(12593358, Description + "; Srv #12200411", 40) },
                { new OSInfo(01250001, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290001, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020002, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040001, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150002, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072901, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073101, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110003, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120003, Description + "; EFI Live COS", 40) },
                { new OSInfo(01250002, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290002, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020003, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040002, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150003, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072902, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073102, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140001, Description + "; EFI Live COS", 40) },
                { new OSInfo(01250003, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290003, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020005, Description + "; EFI Live COS", 40) },
                { new OSInfo(02040003, Description + "; EFI Live COS", 40) },
                { new OSInfo(03170001, Description + "; EFI Live COS", 40) },
                { new OSInfo(04072903, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073103, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140002, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270001, Description + "; EFI Live COS", 40) },
                { new OSInfo(01290005, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030001, Description + "; EFI Live COS", 40) },
                { new OSInfo(03110001, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190001, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073001, Description + "; EFI Live COS", 40) },
                { new OSInfo(04080001, Description + "; EFI Live COS", 40) },
                { new OSInfo(04140003, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270002, Description + "; EFI Live COS", 40) },
                { new OSInfo(02010001, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030002, Description + "; EFI Live COS", 40) },
                { new OSInfo(03130001, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190002, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073002, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110001, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120001, Description + "; EFI Live COS", 40) },
                { new OSInfo(01270003, Description + "; EFI Live COS", 40) },
                { new OSInfo(02020001, Description + "; EFI Live COS", 40) },
                { new OSInfo(02030003, Description + "; EFI Live COS", 40) },
                { new OSInfo(03150001, Description + "; EFI Live COS", 40) },
                { new OSInfo(03190003, Description + "; EFI Live COS", 40) },
                { new OSInfo(04073003, Description + "; EFI Live COS", 40) },
                { new OSInfo(04110002, Description + "; EFI Live COS", 40) },
                { new OSInfo(05120002, Description + "; EFI Live COS", 40) },
            };

        }
    }
}