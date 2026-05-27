// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P05 : ECUBase {
        public P05() {
            Description = "P05 (VPW)";
            HardwareType = PcmType.P05;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            IsSupportedWriteBootSector = false;
            LoaderRequired = false;
            KernelFileName = "Kernel-P05.bin";
            KernelBaseAddress = 0xFFC100;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KeyAlgorithm = 0x35;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service No 12581501
                new OSInfo(12584056, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12584057, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12584058, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12588931, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12588932, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12588933, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12619740, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12619742, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                new OSInfo(12619744, 12581501, "2004 P05 (VPW) Service No 12581501", 0x35),
                // Service No 12591279
                new OSInfo(12597270, 12591279, "2005 P05 (VPW) Service No 12591279", 0x35),
                // Service No 12604963
                new OSInfo(12603217, 12604963, "2005 P05 (VPW+CAN) Service No 12604963", 0x35),
                // Service number unknown
                new OSInfo(12592928, 0, "P05", 0x35),
                new OSInfo(12596136, 0, "P05", 0x35),
                new OSInfo(12596138, 0, "P05", 0x35),
                new OSInfo(12600367, 0, "P05", 0x35),
                new OSInfo(12603291, 0, "P05", 0x35),
            };
        }
    }
}
