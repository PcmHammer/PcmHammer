using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class UndefinedECU : ECUBase {
        public UndefinedECU() {
            Manufacturer = "---";
            this.Description = "Undefined ECU";
            this.HardwareType = PcmType.Undefined;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("undefined", 0, 0, 0) }
            };
        }

        public override ECUBase Clone()
        {
            return new UndefinedECU();
        }
    }
}