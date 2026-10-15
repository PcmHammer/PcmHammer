// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// The .plz package: importing from ADX, automatic binding, and the round trip.
    /// </summary>
    [TestClass]
    public class LoggerPackageTests
    {
        /// <summary>
        /// Two gauges and a monitor, plus two entries that cannot be resolved: an item hash of zero
        /// and one naming a parameter that is never defined. Real files contain both.
        /// </summary>
        private const string SampleAdx = @"<ADXFORMAT version=""1.01"">
  <ADXHEADER>
    <guid>11112222-3333-4444-5555-666677778888</guid>
    <DEFAULTS datasizeinbits=""8"" sigdigits=""2"" />
  </ADXHEADER>
  <ADXVALUE id=""rpm"" idhash=""0x00000001"" title=""GM RPM"">
    <units>RPM</units>
    <range low=""0.000000"" high=""8000.000000"" />
    <alarms low=""0.000000"" high=""6000.000000"" />
    <digcount>0</digcount>
  </ADXVALUE>
  <ADXVALUE id=""map"" idhash=""0x00000002"" title=""MAP SAE"">
    <units>kPa</units>
    <range low=""0.000000"" high=""255.000000"" />
  </ADXVALUE>
  <ADXVALUE id=""odd"" idhash=""0x00000003"" title=""Flux Capacitor Charge"">
    <units>GW</units>
  </ADXVALUE>
  <ADXDASHBOARD id=""d1"" idhash=""0x000000D1"" title=""Main Dash"">
    <ADXDGENTRY gaugetype=""0"" itemidhash=""0x00000001"" left=""0"" top=""0"" right=""50"" bottom=""40"" arcmax=""270"" />
    <ADXDGENTRY gaugetype=""1"" itemidhash=""0x00000002"" left=""50"" top=""0"" right=""100"" bottom=""40"" />
    <ADXDGENTRY gaugetype=""1"" itemidhash=""0x00000003"" left=""0"" top=""40"" right=""50"" bottom=""80"" />
    <ADXDGENTRY gaugetype=""1"" itemidhash=""0x00000000"" left=""50"" top=""40"" right=""100"" bottom=""80"" />
    <ADXDGENTRY gaugetype=""1"" itemidhash=""0xDEADBEEF"" left=""0"" top=""80"" right=""50"" bottom=""100"" />
  </ADXDASHBOARD>
  <ADXMONITOR id=""m1"" idhash=""0x0000AA01"" title=""Monitor1"">
    <ADXMONSERIES linecolor=""0x00FF0000"" axiscolor=""0x00FF0000"" itemidhash=""0x00000001"" />
  </ADXMONITOR>
</ADXFORMAT>";

        private static LoggerPackage Import()
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleAdx)))
            {
                return LoggerPackage.FromAdx(AdxDocument.Load(stream), "sample.adx");
            }
        }

        /// <summary>The parameters a logger might offer, named the way the database names them.</summary>
        private static IReadOnlyList<BindingCandidate> Candidates()
        {
            return new[]
            {
                new BindingCandidate("EngineSpeed", "Engine Speed", "RPM"),
                new BindingCandidate("MAPSensor", "MAP Sensor", "kPa"),
                new BindingCandidate("TPSensor", "TP Sensor", "Volts"),
                new BindingCandidate("KnockRetardDegrees", "Knock Retard Degrees", "deg"),
            };
        }

        [TestMethod]
        public void ImportKeepsProvenanceAndSkipsUnresolvableEntries()
        {
            LoggerPackage package = Import();

            Assert.AreEqual("adx", package.Source.Kind);
            Assert.AreEqual("sample.adx", package.Source.Name);
            Assert.AreEqual("11112222-3333-4444-5555-666677778888", package.Source.Guid);
            Assert.AreEqual(3, package.Dashboards[0].Gauges.Count, "zero hash and unknown hash dropped");
        }

        [TestMethod]
        public void ImportLeavesEverythingUnbound()
        {
            LoggerPackage package = Import();

            Assert.AreEqual(3, package.UnboundGaugeCount);
            Assert.AreEqual(0, package.Pids.Count, "nothing to log until something is bound");
        }

        /// <summary>
        /// "GM RPM" and "MAP SAE" should find Engine Speed and MAP Sensor: the vendor prefixes are
        /// noise and RPM is a known synonym for engine speed.
        /// </summary>
        [TestMethod]
        public void BindingMatchesThroughNoiseWordsAndSynonyms()
        {
            LoggerPackage package = Import();

            int bound = PidBinder.BindPackage(package, Candidates());

            Assert.AreEqual(2, bound);
            Assert.AreEqual("EngineSpeed", package.Dashboards[0].Gauges.Single(g => g.Title == "GM RPM").PidId);
            Assert.AreEqual("MAPSensor", package.Dashboards[0].Gauges.Single(g => g.Title == "MAP SAE").PidId);
        }

        /// <summary>Anything without a confident match stays unbound rather than being guessed at.</summary>
        [TestMethod]
        public void UnknownTitlesAreLeftUnbound()
        {
            LoggerPackage package = Import();

            PidBinder.BindPackage(package, Candidates());

            GaugeLayout odd = package.Dashboards[0].Gauges.Single(g => g.Title == "Flux Capacitor Charge");
            Assert.IsFalse(odd.IsBound);
            Assert.AreEqual(1, package.UnboundGaugeCount);
        }

        /// <summary>A bound display has to have something in the PID list, or it can never show a value.</summary>
        [TestMethod]
        public void BindingAddsThePidsThatAreNowReferenced()
        {
            LoggerPackage package = Import();

            PidBinder.BindPackage(package, Candidates());

            Assert.AreEqual(2, package.Pids.Count);
            CollectionAssert.AreEquivalent(
                new[] { "EngineSpeed", "MAPSensor" },
                package.Pids.Select(p => p.Id).ToArray());
            Assert.AreEqual("RPM", package.FindPid("EngineSpeed")!.Units, "the conversion travels with the pid");
            Assert.IsFalse(package.DanglingPidReferences().Any());
        }

        [TestMethod]
        public void MonitorSeriesAreBoundToo()
        {
            LoggerPackage package = Import();

            PidBinder.BindPackage(package, Candidates());

            Assert.AreEqual("EngineSpeed", package.Monitors[0].Series[0].PidId);
        }

        [TestMethod]
        public void BindingIsNotRepeatedForAlreadyBoundGauges()
        {
            LoggerPackage package = Import();
            PidBinder.BindPackage(package, Candidates());

            int again = PidBinder.BindPackage(package, Candidates());

            Assert.AreEqual(0, again, "nothing left to bind");
        }

        [TestMethod]
        public void RoundTripsThroughAPackageFile()
        {
            LoggerPackage package = Import();
            PidBinder.BindPackage(package, Candidates());
            package.Description = "Round trip";
            package.Communications.Protocol = "VPW";
            package.Communications.Osid = 12345678;

            LoggerPackage reloaded;
            using (MemoryStream stream = new MemoryStream())
            {
                PlzFormat.Save(stream, package);
                stream.Position = 0;
                reloaded = PlzFormat.Load(stream);
            }

            Assert.AreEqual(LoggerPackage.CurrentFormatVersion, reloaded.FormatVersion);
            Assert.AreEqual("Round trip", reloaded.Description);
            Assert.AreEqual("VPW", reloaded.Communications.Protocol);
            Assert.AreEqual(12345678u, reloaded.Communications.Osid);
            Assert.AreEqual("sample.adx", reloaded.Source.Name);
            Assert.AreEqual(package.Pids.Count, reloaded.Pids.Count);
            Assert.AreEqual(package.Dashboards[0].Gauges.Count, reloaded.Dashboards[0].Gauges.Count);
            Assert.AreEqual("EngineSpeed", reloaded.Dashboards[0].Gauges.Single(g => g.Title == "GM RPM").PidId);
            Assert.AreEqual("EngineSpeed", reloaded.Monitors[0].Series[0].PidId);
        }

        /// <summary>Visibility and ordering survive, so a hidden dashboard stays hidden.</summary>
        [TestMethod]
        public void VisibilityAndOrderRoundTrip()
        {
            LoggerPackage package = Import();
            package.Dashboards[0].Visible = false;
            package.Dashboards[0].Order = 7;

            LoggerPackage reloaded;
            using (MemoryStream stream = new MemoryStream())
            {
                PlzFormat.Save(stream, package);
                stream.Position = 0;
                reloaded = PlzFormat.Load(stream);
            }

            Assert.IsFalse(reloaded.Dashboards[0].Visible);
            Assert.AreEqual(7, reloaded.Dashboards[0].Order);
        }

        /// <summary>The histogram section exists from the start so adding one later is not a new shape.</summary>
        [TestMethod]
        public void HistogramsRoundTrip()
        {
            LoggerPackage package = new LoggerPackage();
            HistogramLayout histogram = new HistogramLayout
            {
                Id = "h1",
                Title = "VE Learn",
                XPidId = "MAPSensor",
                YPidId = "EngineSpeed",
                CellPidId = "LongTermFTBank1",
                Aggregation = "Average",
                MinimumSamples = 3,
            };
            histogram.XBins.AddRange(new double[] { 20, 40, 60 });
            histogram.YBins.AddRange(new double[] { 800, 1600 });
            package.Histograms.Add(histogram);

            LoggerPackage reloaded;
            using (MemoryStream stream = new MemoryStream())
            {
                PlzFormat.Save(stream, package);
                stream.Position = 0;
                reloaded = PlzFormat.Load(stream);
            }

            Assert.AreEqual(1, reloaded.Histograms.Count);
            Assert.AreEqual("VE Learn", reloaded.Histograms[0].Title);
            Assert.AreEqual(3, reloaded.Histograms[0].XBins.Count);
            Assert.AreEqual(2, reloaded.Histograms[0].YBins.Count);
            Assert.AreEqual(3, reloaded.Histograms[0].MinimumSamples);
        }

        /// <summary>
        /// A file from a newer build may hold sections this one cannot represent. Opening it anyway
        /// would quietly drop them on the next save.
        /// </summary>
        [TestMethod]
        public void NewerFormatVersionIsRefused()
        {
            LoggerPackage package = new LoggerPackage { FormatVersion = LoggerPackage.CurrentFormatVersion + 1 };

            using (MemoryStream stream = new MemoryStream())
            {
                PlzFormat.Save(stream, package);
                stream.Position = 0;
                Assert.ThrowsException<PackageException>(() => PlzFormat.Load(stream));
            }
        }

        /// <summary>
        /// Opening the wrong file should say so, rather than surfacing ZIP's complaint about a
        /// central directory record.
        /// </summary>
        [TestMethod]
        public void SomethingThatIsNotAPackageIsRejected()
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip")))
            {
                PackageException exception = Assert.ThrowsException<PackageException>(() => PlzFormat.Load(stream));
                StringAssert.Contains(exception.Message, "not a PcmLogger package");
            }
        }

        /// <summary>A ZIP with no manifest is an archive, but not one of ours.</summary>
        [TestMethod]
        public void ZipWithoutAManifestIsRejected()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.IO.Compression.ZipArchive archive =
                    new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    archive.CreateEntry("something-else.txt");
                }

                stream.Position = 0;
                PackageException exception = Assert.ThrowsException<PackageException>(() => PlzFormat.Load(stream));
                StringAssert.Contains(exception.Message, "manifest.json");
            }
        }

        [TestMethod]
        public void DanglingReferencesAreReported()
        {
            LoggerPackage package = Import();
            package.Dashboards[0].Gauges[0].PidId = "NeverDefined";

            CollectionAssert.AreEqual(new[] { "NeverDefined" }, package.DanglingPidReferences().ToArray());
        }
    }
}
