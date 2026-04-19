using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace PcmHacking.ECU.Controllers
{
    public class P12_2M : P12
    {
        public P12_2M() {
            this.Description = "P12(2MB)";
            this.HardwareType = PcmType.P12_2M;
            this.BaseHardwareType = PcmType.P12;
            this.ImageSize = 2048 * 1024; // 2MB
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12609805, 12569773, 91) },
                { new OSInfo("GM", 12611642, 12569773, 91) },
                { new OSInfo("GM", 12613422, 12569773, 91) },
                { new OSInfo("GM", 12618164, 12569773, 91) },
            };
        }
    }
}
