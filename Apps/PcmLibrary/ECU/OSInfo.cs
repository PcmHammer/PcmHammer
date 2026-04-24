using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking.ECU {
    public class OSInfo {
        public string Manufacturer { get; private set; }
        public uint OSID { get; private set; }
        public int ServiceNumber { get; private set; }
        public int KeyAlgorithm { get; private set; }
        public bool IdOverridePresent { get; private set; } = false;

        public OSInfo(string manufacturer, uint osid, int serviceNumber = 0, int keyAlgo = 0)
        {
            Manufacturer = manufacturer;
            OSID = osid;
            ServiceNumber = serviceNumber;
            KeyAlgorithm = keyAlgo;
        }

        public void SetOverridePresent() => IdOverridePresent = true;
    }
}
