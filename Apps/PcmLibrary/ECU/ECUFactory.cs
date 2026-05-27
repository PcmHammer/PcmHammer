// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.ECU.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU {
    public static class ECUFactory {
        public static List<ECUBase> StoredECUs = new List<ECUBase>() {
            new P01()
        };

        public static ECUBase GetControllerByOSID(uint osid) {
            ECUBase controller = StoredECUs.FirstOrDefault(x => x.ECUSupportsOSID(osid));
            if (controller != null) {
                controller.SetCurrentOSID(osid);
            }
            return StoredECUs.FirstOrDefault(x => x.ECUSupportsOSID(osid));
        }
    }
}
