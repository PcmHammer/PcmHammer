using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking.ECU {
    public class OSInfo {
        public uint OSID { get; private set; }
        public int ServiceNumber { get; private set; }
        public int KeyAlgorithm { get; private set; }
        public string Description { get; private set; }

        public OSInfo(uint osid, int serviceNumber = 0, string description = "Unknown ECU", int keyAlgo = 0) {
            OSID = osid;
            ServiceNumber = serviceNumber;
            Description = description;
            KeyAlgorithm = keyAlgo;
        }
    }
}
