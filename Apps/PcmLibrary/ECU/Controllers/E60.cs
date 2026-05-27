// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking.ECU.Controllers {
    public class E60 : ECUBase {
        public E60() {
            Description = "E60 LLY Duramax";
            HardwareType = PcmType.E60;
            HardwareSlaveCPU = false;
            IsSupported = false;
            IsSupportedRead = false;
            IsSupportedWrite = false;
            ImageBaseAddress = 0x0;
            ImageSize = 1024 * 1024;
            KeyAlgorithm = 2;
            KnownOperatingSystems = new List<OSInfo>() {
                // LLY Service No 12244189
                new OSInfo(15141668, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15193885, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15228758, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15231599, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15231600, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15879103, 12244189, "E60 Service No 12244189", 2),
                new OSInfo(15087230, 12244189, "E60 Service No 12244189", 2),
                // LLY EFI Live COS
                new OSInfo(4166801, 0, "LLY EFILive COS", 2),
                new OSInfo(4166805, 0, "LLY EFILive COS", 2),
                new OSInfo(5160001, 0, "LLY EFILive COS", 2),
                new OSInfo(5160005, 0, "LLY EFILive COS", 2),
                new OSInfo(5388501, 0, "LLY EFILive COS", 2),
                new OSInfo(5388505, 0, "LLY EFILive COS", 2),
                new OSInfo(5875801, 0, "LLY EFILive COS", 2),
                new OSInfo(5875805, 0, "LLY EFILive COS", 2),
            };
        }
    }
}
