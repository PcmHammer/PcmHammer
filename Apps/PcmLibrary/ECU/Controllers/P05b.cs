// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P05b : P05 {
        public P05b() {
            Description = "P05b (VPW+CAN)";
            HardwareType = PcmType.P05b;
            BaseHardwareType = PcmType.P05;
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
            KeyAlgorithm = 0x35;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service number unknown
                new("GM", 12585292, 0, 0x35),
                new("GM", 12585293, 0, 0x35),
                new("GM", 12585294, 0, 0x35),
                new("GM", 12589239, 0, 0x35),
                new("GM", 12594884, 0, 0x35),
                new("GM", 12596629, 0, 0x35),
                new("GM", 12596630, 0, 0x35),
                new("GM", 12596631, 0, 0x35),
                new("GM", 12596632, 0, 0x35),
                new("GM", 12596633, 0, 0x35),
                new("GM", 12598122, 0, 0x35),
                new("GM", 12598125, 0, 0x35),
                new("GM", 12598324, 0, 0x35),
                new("GM", 12598325, 0, 0x35),
                new("GM", 12599697, 0, 0x35),
                new("GM", 12600156, 0, 0x35),
                new("GM", 12600158, 0, 0x35),
                new("GM", 12600160, 0, 0x35),
                new("GM", 12603346, 0, 0x35),
                new("GM", 12603347, 0, 0x35),
                new("GM", 12603348, 0, 0x35),
                new("GM", 12608097, 0, 0x35),
                new("GM", 12608100, 0, 0x35),
                new("GM", 12612947, 0, 0x35),
                new("GM", 12612950, 0, 0x35),
                new("GM", 12619714, 0, 0x35),
                new("GM", 12619715, 0, 0x35),
                new("GM", 12619722, 0, 0x35),
                new("GM", 12619724, 0, 0x35),
                new("GM", 12626396, 0, 0x35),
                new("GM", 12626398, 0, 0x35),
                new("GM", 12635873, 0, 0x35),
                new("GM", 12635874, 0, 0x35),
                new("GM", 12635875, 0, 0x35),
                new("GM", 12635876, 0, 0x35),
                new("GM", 12635877, 0, 0x35),
                new("GM", 12635878, 0, 0x35),
                new("GM", 12635879, 0, 0x35),

                //Crowbar: Generated from cross-translation of PcmInfo.cs.
				new("GM", 12596631, 0, 0x35),
                new("GM", 12599697, 0, 0x35),
                new("GM", 12608100, 0, 0x35),
                new("GM", 12612950, 0, 0x35),
                new("GM", 12619714, 0, 0x35),
                new("GM", 12619715, 0, 0x35),

            };
        }
    }
}
