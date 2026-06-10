// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.ECU.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace PcmHacking.ECU
{
    public static class ECUFactory
    {
        public static List<ECUBase> StoredECUs = new List<ECUBase>() {
            new P01(),
            new P59(),
            new P04(),
            new P04_Early(),
            new P04_Early_512k(),
            new P05(),
            new P05b(),
            new P05c(),
            new P08(),
            new P10(),
            new P11(),
            new P12(),
            new P12_2M(),
            new E54(),
            new E60(),
            new BlackBox(),
            new UnsupportedECU()
        };

        public static ECUBase GetControllerByOSID(uint osid)
        {
            ECUBase controller = StoredECUs.FirstOrDefault(x => x.ECUSupportsOSID(osid));
            if (controller != null)
            {
                ECUBase newController = controller.Clone();
                newController.SetCurrentOSID(osid);
                return newController;
            }
            controller = new UndefinedECU();
            controller.SetCurrentOSID(osid);
            return controller;
        }

        public static ECUBase GetControllerOverride(PcmType type, uint osid = 0)
        {
            ECUBase controller = StoredECUs.First(x => x.HardwareType == type).Clone();
            controller.SetCurrentOSID(osid);
            controller.SetOverriddenState();
            return controller;
        }
    }
}
