// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class P10 : ECUBase {
        public P10() {
            Description = "P10 1Mb";
            HardwareType = PcmType.P10;
            // No slave CPU; see the note on PcmType.P10 in PcmInfo.
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = true;
            LoaderRequired = false;
            KernelFileName = "Kernel-P10.bin";
            KernelBaseAddress = 0xFFB800;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 66;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // Service No 12576463
                new OSInfo(12213305, 12576463, "P10 Service No 12576463", 66),
                new OSInfo(12571911, 12576463, "P10 Service No 12576463", 66),
                new OSInfo(12575262, 12576463, "P10 Service No 12576463", 66),
                new OSInfo(12579238, 12576463, "P10 Service No 12576463", 66),
                new OSInfo(12587430, 12576463, "P10 Service No 12576463", 66),
                // Service No 12574976
                new OSInfo(12577956, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12579357, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12584138, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12584594, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12587608, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12588012, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12589825, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12590965, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12595726, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12597031, 12574976, "P10 Service No 12574976", 66),
                new OSInfo(12623317, 12574976, "P10 Service No 12574976", 66),
            };
        }
    }
}
