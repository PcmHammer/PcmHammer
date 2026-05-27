// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class E54 : ECUBase {
        public E54() {
            Description = "E54 LB7 Duramax";
            HardwareType = PcmType.E54;
            HardwareSlaveCPU = false;
            IsSupported = true;
            IsSupportedRead = true;
            IsSupportedWrite = true;
            IsSupportedWriteSlaveCPU = true;
            IsSupportedWriteBySegment = true;
            LoaderRequired = false;
            KernelFileName = "Kernel-E54.bin";
            KernelBaseAddress = 0xFF9100;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            ImageSize = 512 * 1024;
            KeyAlgorithm = 54;
            ChecksumSupport = true;
            FlashCRCSupport = true;
            FlashIDSupport = true;
            KernelVersionSupport = true;
            KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                // LB7 EFI Live COS
                new OSInfo(1337601, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(1337605, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(1710001, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(1710005, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(1887301, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(1887305, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2444101, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2444105, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2600601, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2600605, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2685301, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(2685305, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(3904401, 0, "E54 LB7 EFILive COS", 54),
                new OSInfo(3904405, 0, "E54 LB7 EFILive COS", 54),
                // Service No 9388505
                new OSInfo(15063376, 9388505, "E54 Service No 9388505", 54),
                new OSInfo(15097100, 9388505, "E54 Service No 9388505", 54),
                new OSInfo(15188873, 9388505, "E54 Service No 9388505", 54),
                // Service No 12210729
                new OSInfo(15085499, 12210729, "E54 Service No 12210729", 54),
                new OSInfo(15094441, 12210729, "E54 Service No 12210729", 54),
                new OSInfo(15166853, 12210729, "E54 Service No 12210729", 54),
                new OSInfo(15186006, 12210729, "E54 Service No 12210729", 54),
                new OSInfo(15189044, 12210729, "E54 Service No 12210729", 54),
                // Service number unknown
                new OSInfo(9393838, 0, "E54", 54),
            };
        }
    }
}
