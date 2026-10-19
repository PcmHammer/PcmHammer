// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
namespace PcmHacking
{
    public static class TuningLogProfiles
    {
        public static readonly IReadOnlyDictionary<string,string[]> All = new Dictionary<string,string[]>
        {
            ["Stock baseline"] = new[]{"EngineSpeed","VehicleSpeedSensor","ECTSensor","IATSensor","MAPSensor","MAFSensor","IgnitionTiming","KnockRetardDegrees","ShortTermFTLeftBank","LongTermFTLeftBank","ShortTermFTRightBank","LongTermFTRightBank"},
            ["Cam idle & startup"] = new[]{"EngineSpeed","DesiredIdleSpeed","DesiredIACAirflow","ThrottlePosition","IgnitionTiming","MAPSensor","ECTSensor","Ignition1Signal","ShortTermFTLeftBank","LongTermFTLeftBank","ShortTermFTRightBank","LongTermFTRightBank"},
            ["Fuel & airflow"] = new[]{"EngineSpeed","MAFSensor","MAFSensorFrequency","MAPSensor","ShortTermFTLeftBank","LongTermFTLeftBank","ShortTermFTRightBank","LongTermFTRightBank","FuelingMode","PowerEnrichment","TargetAFRatio","EthanolContent"},
            ["Timing & drivability"] = new[]{"EngineSpeed","MAPSensor","EngineLoad","IgnitionTiming","KnockRetardDegrees","ThrottlePosition","AcceleratorPedalPosition","MAFSensor","ECTSensor","IATSensor","VehicleSpeedSensor"},
            ["Misfire investigation"] = new[]{"EngineSpeed","ECTSensor","IgnitionTiming","KnockRetardDegrees","MisfireCurrentCyl1","MisfireCurrentCyl2","MisfireCurrentCyl3","MisfireCurrentCyl4","MisfireCurrentCyl5","MisfireCurrentCyl6","MisfireCurrentCyl7","MisfireCurrentCyl8"}
        };
    }
}
