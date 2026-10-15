// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The dashboard a fresh install opens with, checked against the definitions that ship beside
    /// it.
    /// </summary>
    /// <remarks>
    /// The point of these is that a default dashboard fails quietly. A gauge naming a parameter that
    /// does not exist, or a conversion the parameter does not offer, draws a face and no value - and
    /// looks exactly like a PCM that has not answered yet. Nothing else in the build would notice.
    /// </remarks>
    [TestClass]
    public class DefaultLoggerPackageTests
    {
        /// <summary>
        /// The shipped definitions, loaded from the copies this test project links in.
        /// </summary>
        private static ParameterDatabase Definitions()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            ParameterDatabase database = new ParameterDatabase(directory);
            database.LoadDatabase();
            return database;
        }

        [TestMethod]
        public void DefaultPackageHasADashboardAndMonitors()
        {
            LoggerPackage package = DefaultLoggerPackage.Create();

            Assert.AreEqual(1, package.Dashboards.Count);
            Assert.IsTrue(package.Dashboards[0].Visible, "The one dashboard has to be the visible one.");
            Assert.IsTrue(package.Dashboards[0].Gauges.Count >= 8, "A default dashboard with a handful of gauges is not worth shipping.");
            Assert.AreEqual(2, package.Monitors.Count);
            Assert.IsTrue(package.Monitors.All(m => m.Visible && m.Series.Count > 0));
        }

        /// <summary>
        /// Nothing arrives unbound, which is what makes this different from an import: the gauges
        /// are already pointed at parameters, so the first run polls them without being asked.
        /// </summary>
        [TestMethod]
        public void EveryGaugeAndTraceIsBoundToAPackagedPid()
        {
            LoggerPackage package = DefaultLoggerPackage.Create();

            Assert.AreEqual(0, package.UnboundGaugeCount);
            Assert.IsTrue(package.Monitors.SelectMany(m => m.Series).All(s => s.IsBound));
            Assert.AreEqual(
                string.Empty,
                string.Join(", ", package.DanglingPidReferences()),
                "A gauge or trace names a PID the package does not list.");
        }

        /// <summary>
        /// The test that matters: every id really is in the definitions that ship with the app.
        /// </summary>
        [TestMethod]
        public void EveryPackagedPidExistsInTheShippedDefinitions()
        {
            ParameterDatabase database = Definitions();
            List<string> missing = new List<string>();

            foreach (PackagedPid pid in DefaultLoggerPackage.Create().Pids)
            {
                if (!database.TryGetParameter(pid.Id, out Parameter _))
                {
                    missing.Add(pid.Id);
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", missing));
        }

        /// <summary>
        /// Fuel trims are standard OBD PIDs, not the similarly named GM-enhanced definitions.
        /// Keeping this explicit prevents a later display-name cleanup from silently changing what
        /// the default dashboard requests.
        /// </summary>
        [TestMethod]
        public void DefaultPackageUsesTheStandardFuelTrimPids()
        {
            ParameterDatabase database = Definitions();
            LoggerPackage package = DefaultLoggerPackage.Create();

            Dictionary<string, uint> expected = new Dictionary<string, uint>
            {
                { "ShortTermFTLeftBank", 0x0006 },
                { "LongTermFTLeftBank", 0x0007 },
                { "ShortTermFTRightBank", 0x0008 },
                { "LongTermFTRightBank", 0x0009 },
            };

            foreach (KeyValuePair<string, uint> item in expected)
            {
                Assert.IsNotNull(package.FindPid(item.Key), item.Key + " is not in the default package.");
                Assert.IsTrue(database.TryGetParameter(item.Key, out Parameter parameter), item.Key + " is not defined.");
                Assert.AreEqual(item.Value, ((PidParameter)parameter).PID, item.Key + " has the wrong PID.");
            }
        }

        /// <summary>
        /// A gauge face drawn for one conversion must not be fed another, so the units each PID
        /// names have to be ones its parameter actually offers.
        /// </summary>
        [TestMethod]
        public void EveryPackagedPidNamesAConversionItsParameterOffers()
        {
            ParameterDatabase database = Definitions();
            List<string> wrong = new List<string>();

            foreach (PackagedPid pid in DefaultLoggerPackage.Create().Pids)
            {
                if (!database.TryGetParameter(pid.Id, out Parameter parameter))
                {
                    continue;
                }

                bool offered = parameter.Conversions.Any(
                    c => string.Equals(c.Units, pid.Units, StringComparison.OrdinalIgnoreCase));

                if (!offered)
                {
                    wrong.Add(pid.Id + " in " + pid.Units);
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", wrong));
        }

        /// <summary>
        /// Gauges and traces name the same units as the PID they read, so the scale under a reading
        /// is the scale the reading is in.
        /// </summary>
        [TestMethod]
        public void DisplayedUnitsMatchThePidTheyRead()
        {
            LoggerPackage package = DefaultLoggerPackage.Create();
            List<string> wrong = new List<string>();

            foreach (GaugeLayout gauge in package.Dashboards.SelectMany(d => d.Gauges))
            {
                PackagedPid? pid = package.FindPid(gauge.PidId);
                if (pid != null && !string.Equals(pid.Units, gauge.Units, StringComparison.Ordinal))
                {
                    wrong.Add(gauge.Title);
                }
            }

            foreach (MonitorSeriesLayout trace in package.Monitors.SelectMany(m => m.Series))
            {
                PackagedPid? pid = package.FindPid(trace.PidId);
                if (pid != null && !string.Equals(pid.Units, trace.Units, StringComparison.Ordinal))
                {
                    wrong.Add(trace.Title);
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", wrong));
        }

        /// <summary>
        /// The whole default has to fit what a PCM will send, or a fresh install starts by failing
        /// to log.
        /// </summary>
        /// <remarks>
        /// DPIDs hold six bytes each and three of them is the practical limit, so eighteen bytes is
        /// the budget. Sixteen-bit parameters are packed first and the current set is an exact
        /// multiple of a group, so there is no fragmentation to account for - and a change that
        /// introduced some would show up here as going over.
        /// </remarks>
        [TestMethod]
        public void DefaultPackageFitsThreeDpids()
        {
            ParameterDatabase database = Definitions();
            int bytes = 0;

            foreach (PackagedPid pid in DefaultLoggerPackage.Create().Pids)
            {
                if (database.TryGetParameter(pid.Id, out Parameter parameter)
                    && parameter is PcmParameter pcmParameter)
                {
                    bytes += pcmParameter.ByteCount;
                }
            }

            Assert.IsTrue(
                bytes <= DpidConfiguration.MaxGroups * ParameterGroup.MaxBytes,
                "The default dashboard polls " + bytes + " bytes, which is more than three DPIDs hold.");
        }

        /// <summary>
        /// Everything comes from the PCM, so one interface and a vehicle is the whole setup.
        /// </summary>
        /// <remarks>
        /// This is what the AVT wideband gauge failed: it was an input on an interface box, so the
        /// gauge read nothing for anyone who did not own one. A broadcast parameter here would be
        /// the same mistake, since it needs a second device watching another bus.
        /// </remarks>
        [TestMethod]
        public void DefaultPackageNeedsNothingButThePcm()
        {
            ParameterDatabase database = Definitions();
            List<string> elsewhere = new List<string>();

            foreach (PackagedPid pid in DefaultLoggerPackage.Create().Pids)
            {
                if (database.TryGetParameter(pid.Id, out Parameter parameter)
                    && !ParameterSources.PcmConnection.Contains(parameter.Source))
                {
                    elsewhere.Add(pid.Id + " from " + parameter.Source);
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", elsewhere));
        }

        [TestMethod]
        public void DefaultPackageSurvivesTheRoundTrip()
        {
            LoggerPackage package = DefaultLoggerPackage.Create();
            LoggerPackage reloaded;

            using (MemoryStream stream = new MemoryStream())
            {
                PlzFormat.Save(stream, package);
                stream.Position = 0;
                reloaded = PlzFormat.Load(stream);
            }

            Assert.AreEqual(package.Pids.Count, reloaded.Pids.Count);
            Assert.AreEqual(
                package.Dashboards[0].Gauges.Count,
                reloaded.Dashboards[0].Gauges.Count);
            Assert.AreEqual(
                package.Monitors.Sum(m => m.Series.Count),
                reloaded.Monitors.Sum(m => m.Series.Count));
            Assert.AreEqual(DefaultLoggerPackage.SourceKind, reloaded.Source.Kind);
        }
    }
}
