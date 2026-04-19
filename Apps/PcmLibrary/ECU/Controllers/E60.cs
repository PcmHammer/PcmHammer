using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class E60 : ECUBase
    {
        public E60() {
            Manufacturer = "GM";
            this.Description = "LLY Duramax";
            this.HardwareType = PcmType.E60;
            BaseHardwareType = HardwareType;
            this.KeyAlgorithm = 2;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 1024 * 1024;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 15141668, 12244189, 2) },
                { new OSInfo("GM", 15193885, 12244189, 2) },
                { new OSInfo("GM", 15228758, 12244189, 2) },
                { new OSInfo("GM", 15231599, 12244189, 2) },
                { new OSInfo("GM", 15231600, 12244189, 2) },
                { new OSInfo("GM", 15879103, 12244189, 2) },
                { new OSInfo("GM", 15087230, 12244189, 2) },

                { new OSInfo("EFILive", 04166801, 0, 2) },
                { new OSInfo("EFILive", 04166805, 0, 2) },
                { new OSInfo("EFILive", 05160001, 0, 2) },
                { new OSInfo("EFILive", 05160005, 0, 2) },
                { new OSInfo("EFILive", 05388501, 0, 2) },
                { new OSInfo("EFILive", 05388505, 0, 2) },
                { new OSInfo("EFILive", 05875801, 0, 2) },
                { new OSInfo("EFILive", 05875805, 0, 2) },


            };

        }
    }
}