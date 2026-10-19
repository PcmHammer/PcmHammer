using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;
namespace Tests
{
    [TestClass] public class RecordingCsvReaderTests
    {
        private const string Header="Source,TimestampUTC,ElapsedSeconds,\"Reading, with commas (unit)\"\n";
        [TestMethod] public void OpensQuotedDemoLogAndKeepsUnavailableAsMissing()
        {
            var data=RecordingCsvReader.Read(new StringReader(Header+"DEMO,2026-10-18T00:00:00Z,0,800\nDEMO,2026-10-18T00:00:01Z,1,Unavailable\n"));
            Assert.IsTrue(data.HasDemoData);Assert.AreEqual("Reading, with commas (unit)",data.Headings[0]);
            Assert.AreEqual(800.0,data.Samples[0][0]);Assert.IsTrue(double.IsNaN(data.Samples[1][0]));Assert.AreEqual(1.0,data.Seconds[1]);
        }
        [DataTestMethod] [DataRow("Vehicle,time,1,800\nVehicle,time,0,801")] [DataRow("Vehicle,time,NaN,800")] [DataRow("Vehicle,time,0")] [DataRow("Vehicle,time,0,\"800")]
        public void RejectsMalformedOrOutOfOrderRows(string rows)=>Assert.ThrowsException<InvalidDataException>(()=>RecordingCsvReader.Read(new StringReader(Header+rows)));
        [TestMethod] public void RejectsOtherCsvFormatsAndEmptyLogs()
        {
            Assert.ThrowsException<InvalidDataException>(()=>RecordingCsvReader.Read(new StringReader("Time,RPM\n0,800")));
            Assert.ThrowsException<InvalidDataException>(()=>RecordingCsvReader.Read(new StringReader(Header)));
        }
        [TestMethod] public void ProfilesUseKnownDefinitionsAndAtMostTwelveUniqueChannels()
        {
            var database=new ParameterDatabase(AppContext.BaseDirectory);database.LoadDatabase();
            foreach(var profile in TuningLogProfiles.All)
            {
                Assert.IsTrue(profile.Value.Length<=12);Assert.AreEqual(profile.Value.Length,profile.Value.Distinct().Count());
                foreach(string id in profile.Value)Assert.IsTrue(database.TryGetParameter<PidParameter>(id,out _),profile.Key+": "+id);
            }
        }
    }
}
