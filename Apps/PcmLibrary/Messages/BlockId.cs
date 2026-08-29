// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class BlockId
    {
        public const byte Vin1               = 0x01; // 5 bytes of VIN
        public const byte Vin2               = 0x02; // 6 bytes of VIN
        public const byte Vin3               = 0x03; // 6 bytes of VIN
        public const byte HardwareID         = 0x04; // Hardware ID
        public const byte Serial1            = 0x05; // 4 bytes of Serial
        public const byte Serial2            = 0x06; // 4 bytes of Serial
        public const byte Serial3            = 0x07; // 4 bytes of Serial
        public const byte CalibrationID      = 0x08; // Calibration ID
        public const byte OperatingSystemID  = 0x0A; // Operating System ID aka OSID
        public const byte EngineCalID        = 0x0B; // Engine Segment Calibration ID
        public const byte EngineDiagCalID    = 0x0C; // Engine Diagnostic Calibration ID
        public const byte TransCalID         = 0x0D; // Transmission Segment Calibration ID
        public const byte TransDiagID        = 0x0E; // Transmission Diagnostic Calibration ID
        public const byte FuelCalID          = 0x0F; // Fuel Segment Calibration ID
        public const byte SystemCalID        = 0x10; // System Segment Calibration ID
        public const byte SpeedCalID         = 0x11; // Speed Calibration ID
        public const byte BCC                = 0x14; // Broad Cast Code
        public const byte OilLifePerc        = 0x6D; // Oil Life Remaining Percent
        public const byte OperatingSystemLvl = 0x93; // Operating System Level
        public const byte EngineCalLvl       = 0x94; // Engine Segment Calibration Level
        public const byte EngineDiagCalLvl   = 0x95; // Engine Diagnostic Calibration Level
        public const byte TransCalLvl        = 0x96; // Transmission Segment Calibration Level
        public const byte TransDiagLvl       = 0x97; // Transmission Diagnostic Calibration Level
        public const byte FuelCalLvl         = 0x98; // Fuel Segment Calibration Level
        public const byte SystemCalLvl       = 0x99; // System Segment Calibration Level
        public const byte SpeedCalLvl        = 0x9A; // Speed Calibration Level
        public const byte MEC                = 0xA0; // Manufacturers Enable Counter

        /// <summary>
        /// Display names for a sweep of the 0x3C block ids.
        /// </summary>
        /// <remarks>
        /// These names are the P01/P59 convention. Later VPW PCMs reuse the same ids for their own
        /// segments - a P10 answers 0x0A-0x0E with its five segments (operating system, engine
        /// calibration, transmission calibration, system, speedometer) - so treat a name as a hint
        /// about which id it is, not about what this PCM keeps there.
        /// </remarks>
        public static IReadOnlyDictionary<byte, string> Names { get; } = new Dictionary<byte, string>
        {
            { Vin1,               "VIN part 1" },
            { Vin2,               "VIN part 2" },
            { Vin3,               "VIN part 3" },
            { HardwareID,         "Hardware ID" },
            { Serial1,            "Serial part 1" },
            { Serial2,            "Serial part 2" },
            { Serial3,            "Serial part 3" },
            { CalibrationID,      "Calibration ID" },
            { OperatingSystemID,  "Operating system ID" },
            { EngineCalID,        "Engine calibration ID" },
            { EngineDiagCalID,    "Engine diagnostic calibration ID" },
            { TransCalID,         "Transmission calibration ID" },
            { TransDiagID,        "Transmission diagnostic calibration ID" },
            { FuelCalID,          "Fuel calibration ID" },
            { SystemCalID,        "System calibration ID" },
            { SpeedCalID,         "Speedometer calibration ID" },
            { BCC,                "Broadcast code" },
            { OilLifePerc,        "Oil life remaining" },
            { OperatingSystemLvl, "Operating system level" },
            { EngineCalLvl,       "Engine calibration level" },
            { EngineDiagCalLvl,   "Engine diagnostic calibration level" },
            { TransCalLvl,        "Transmission calibration level" },
            { TransDiagLvl,       "Transmission diagnostic calibration level" },
            { FuelCalLvl,         "Fuel calibration level" },
            { SystemCalLvl,       "System calibration level" },
            { SpeedCalLvl,        "Speedometer calibration level" },
            { MEC,                "Manufacturers enable counter" },
        };
    }
}
