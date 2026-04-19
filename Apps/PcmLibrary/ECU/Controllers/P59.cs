using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P59 : P01
    {
        public P59() {
            Description = "P59";
            BaseHardwareType = PcmType.P01;
            HardwareType = PcmType.P59;
            ImageSize = 1024 * 1024;

            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 12590777, 12583560, 40) },

                { new OSInfo("GM", 12591725, 12589463, 40) },
                { new OSInfo("GM", 12592618, 12589463, 40) },
                { new OSInfo("GM", 12593555, 12589463, 40) },
                { new OSInfo("GM", 12606961, 12589463, 40) },
                { new OSInfo("GM", 12612115, 12589463, 40) },

                { new OSInfo("GM", 12564440, 12586242, 40) },
                { new OSInfo("GM", 12585950, 12586242, 40) },
                { new OSInfo("GM", 12588804, 12586242, 40) },
                { new OSInfo("GM", 12592425, 12586242, 40) },
                { new OSInfo("GM", 12592433, 12586242, 40) },
                { new OSInfo("GM", 12606960, 12586242, 40) },
                { new OSInfo("GM", 12612114, 12586242, 40) },

                { new OSInfo("GM", 12587603, 12589462, 40) },
                { new OSInfo("GM", 12587604, 12586243, 40) },
                { new OSInfo("GM", 76030003, 12586243, 40) },
                { new OSInfo("GM", 76030004, 12586243, 40) },
                { new OSInfo("GM", 76030005, 12586243, 40) },
                { new OSInfo("GM", 76030006, 12586243, 40) },
                { new OSInfo("GM", 76030007, 12586243, 40) },
                { new OSInfo("GM", 76030008, 12586243, 40) },
                { new OSInfo("GM", 76030009, 12586243, 40) },

                { new OSInfo("GM", 12578128, 12582605, 40) },
                { new OSInfo("GM", 12579405, 12582605, 40) },
                { new OSInfo("GM", 12580055, 12582605, 40) },
                { new OSInfo("GM", 12593058, 12582605, 40) },

                { new OSInfo("GM", 12587811, 12582811, 40) },
                { new OSInfo("GM", 12605114, 12582811, 40) },
                { new OSInfo("GM", 12606807, 12582811, 40) },
                { new OSInfo("GM", 12608669, 12582811, 40) },
                { new OSInfo("GM", 12613245, 12582811, 40) },
                { new OSInfo("GM", 12613246, 12582811, 40) },
                { new OSInfo("GM", 12613247, 12582811, 40) },
                { new OSInfo("GM", 12619623, 12582811, 40) },

                { new OSInfo("GM", 12597120, 12602802, 40) },
                { new OSInfo("GM", 12613248, 12602802, 40) },
                { new OSInfo("GM", 12619624, 12602802, 40) },
            };                     

        }
    }
}




