using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU.Controllers {
    public class P04_Early : ECUBase {
        public P04_Early() {
            Manufacturer = "GM";
            this.Description = "1996/1997 V6";
            this.HardwareType = PcmType.P04_Early;
            this.HardwareSlaveCPU = false;
            this.IsSupported = true;
            this.IsSupportedRead = true;
            this.IsSupportedWrite = true;
            this.IsSupportedWriteSlaveCPU = true;
            this.IsSupportedWriteBySegment = false;
            this.LoaderRequired = true;
            this.KernelFileName = "Kernel-P04_Early.bin";
            this.KernelBaseAddress = 0xFF8000;
            this.LoaderFileName = "Loader-P04.bin";
            this.LoaderBaseAddress = 0xFF9890;
            this.ImageBaseAddress = 0x0;
            this.ImageSize = 256 * 1024;
            this.KeyAlgorithm = 6;
            this.ChecksumSupport = true;
            this.FlashCRCSupport = true;
            this.FlashIDSupport = true;
            this.KernelVersionSupport = true;
            this.KernelMaxBlockSize = 4096;
            KnownOperatingSystems = new List<OSInfo>() {
                { new OSInfo("GM", 9352140, 16207326, 6) },
                { new OSInfo("GM", 9352142, 16207326, 6) },
                { new OSInfo("GM", 9352143, 16207326, 6) },
                { new OSInfo("GM", 9352146, 16207326, 6) },
                { new OSInfo("GM", 9352149, 16207326, 6) },
                { new OSInfo("GM", 9352150, 16207326, 6) },
                { new OSInfo("GM", 9352340, 16207326, 6) },
                { new OSInfo("GM", 9352342, 16207326, 6) },
                { new OSInfo("GM", 9352346, 16207326, 6) },
                { new OSInfo("GM", 9352347, 16207326, 6) },
                { new OSInfo("GM", 9352553, 16207326, 6) },
                { new OSInfo("GM", 9354241, 16207326, 6) },
                { new OSInfo("GM", 9354242, 16207326, 6) },
                { new OSInfo("GM", 9355430, 16207326, 6) },
                { new OSInfo("GM", 9355433, 16207326, 6) },
                { new OSInfo("GM", 9355435, 16207326, 6) },
                { new OSInfo("GM", 9365005, 16207326, 6) },
                { new OSInfo("GM", 9365007, 16207326, 6) },
                { new OSInfo("GM", 9365012, 16207326, 6) },
                { new OSInfo("GM", 9365017, 16207326, 6) },
                { new OSInfo("GM", 9365650, 16207326, 6) },
                { new OSInfo("GM", 9365651, 16207326, 6) },
                { new OSInfo("GM", 9365652, 16207326, 6) },
                { new OSInfo("GM", 9365655, 16207326, 6) },
                { new OSInfo("GM", 9367092, 16207326, 6) },
                { new OSInfo("GM", 16210002, 16207326, 6) },
                { new OSInfo("GM", 16210010, 16207326, 6) },
                { new OSInfo("GM", 16219361, 16207326, 6) },
                { new OSInfo("GM", 16219368, 16207326, 6) },
                { new OSInfo("GM", 16226502, 16207326, 6) },
                { new OSInfo("GM", 16227925, 16207326, 6) },
                { new OSInfo("GM", 16230131, 16207326, 6) },
                { new OSInfo("GM", 16230375, 16207326, 6) },
                { new OSInfo("GM", 16230378, 16207326, 6) },
                { new OSInfo("GM", 16230380, 16207326, 6) },
                { new OSInfo("GM", 16230383, 16207326, 6) },
                { new OSInfo("GM", 16230385, 16207326, 6) },
                { new OSInfo("GM", 16231872, 16207326, 6) },
                { new OSInfo("GM", 16231877, 16207326, 6) },
                { new OSInfo("GM", 16231889, 16207326, 6) },
                { new OSInfo("GM", 16231890, 16207326, 6) },
                { new OSInfo("GM", 16231940, 16207326, 6) },
                { new OSInfo("GM", 16231941, 16207326, 6) },
                { new OSInfo("GM", 16232010, 16207326, 6) },
                { new OSInfo("GM", 16232466, 16207326, 6) },
                { new OSInfo("GM", 16232477, 16207326, 6) },
                { new OSInfo("GM", 16233470, 16207326, 6) },
                { new OSInfo("GM", 16233471, 16207326, 6) },
                { new OSInfo("GM", 16233472, 16207326, 6) },
                { new OSInfo("GM", 16233478, 16207326, 6) },
                { new OSInfo("GM", 16234097, 16207326, 6) },
                { new OSInfo("GM", 16234124, 16207326, 6) },
                { new OSInfo("GM", 16234438, 16207326, 6) },
                { new OSInfo("GM", 16234440, 16207326, 6) },
                { new OSInfo("GM", 16234441, 16207326, 6) },
                { new OSInfo("GM", 16234751, 16207326, 6) },
                { new OSInfo("GM", 16235667, 16207326, 6) },
                { new OSInfo("GM", 16238373, 16207326, 6) },
                { new OSInfo("GM", 16238443, 16207326, 6) },
                { new OSInfo("GM", 16238446, 16207326, 6) },
                { new OSInfo("GM", 16238447, 16207326, 6) },
                { new OSInfo("GM", 16238448, 16207326, 6) },
                { new OSInfo("GM", 16238450, 16207326, 6) },
                { new OSInfo("GM", 16238452, 16207326, 6) },
                { new OSInfo("GM", 16238453, 16207326, 6) },
                { new OSInfo("GM", 16238456, 16207326, 6) },
                { new OSInfo("GM", 16238457, 16207326, 6) },
                { new OSInfo("GM", 16238458, 16207326, 6) },
                { new OSInfo("GM", 16238517, 16207326, 6) },
                { new OSInfo("GM", 16238518, 16207326, 6) },
                { new OSInfo("GM", 16238519, 16207326, 6) },
                { new OSInfo("GM", 16238520, 16207326, 6) },
                { new OSInfo("GM", 16238523, 16207326, 6) },
                { new OSInfo("GM", 16238525, 16207326, 6) },
                { new OSInfo("GM", 16238532, 16207326, 6) },
                { new OSInfo("GM", 16238533, 16207326, 6) },
                { new OSInfo("GM", 16240637, 16207326, 6) },
                { new OSInfo("GM", 16241229, 16207326, 6) },
                { new OSInfo("GM", 16245321, 16207326, 6) },
                { new OSInfo("GM", 16246063, 16207326, 6) },
                { new OSInfo("GM", 16246066, 16207326, 6) },
                { new OSInfo("GM", 16249992, 16207326, 6) },
                { new OSInfo("GM", 16249995, 16207326, 6) },
                { new OSInfo("GM", 16251203, 16207326, 6) },
                { new OSInfo("GM", 16251205, 16207326, 6) },
                { new OSInfo("GM", 16251975, 16207326, 6) },
                { new OSInfo("GM", 16257917, 16207326, 6) },
                { new OSInfo("GM", 16257918, 16207326, 6) },
                { new OSInfo("GM", 16257922, 16207326, 6) },
                { new OSInfo("GM", 16257926, 16207326, 6) },
                { new OSInfo("GM", 16257935, 16207326, 6) },
                { new OSInfo("GM", 16257936, 16207326, 6) },
                { new OSInfo("GM", 16257937, 16207326, 6) },
                { new OSInfo("GM", 16257938, 16207326, 6) },
                { new OSInfo("GM", 16257940, 16207326, 6) },
                { new OSInfo("GM", 16257942, 16207326, 6) },
                { new OSInfo("GM", 16257947, 16207326, 6) },
                { new OSInfo("GM", 16257950, 16207326, 6) },
                { new OSInfo("GM", 16257952, 16207326, 6) },
                { new OSInfo("GM", 16257953, 16207326, 6) },
                { new OSInfo("GM", 16257955, 16207326, 6) },
                { new OSInfo("GM", 16257956, 16207326, 6) }
            };

        }
    }
}